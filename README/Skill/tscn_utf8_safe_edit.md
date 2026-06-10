# TSCN 文件安全编辑规则（UTF-8 中文编码保护）

## 问题
使用 PowerShell 的 `Set-Content` 或 `Out-File` 写入包含中文字符的 `.tscn` 文件时，会破坏 UTF-8 多字节汉字编码。损坏后的文件在 Godot 中加载时会报错：

```
Cannot open file 'res://Scripts/Interact/修改属�?SetPlayerHealth.cs'
```

## 根因
- `Set-Content -NoNewline` 默认使用系统编码（Windows 上是 ANSI/GBK），而非 UTF-8
- 被破坏的字符 `性`（UTF-8 字节 `E6 80 A7`）被写入为 `EF BF BD 3F`（UTF-8 替换字符 `�` + `?`）

## 规则
**任何情况下都不要用 `Set-Content` 或 `Out-File` 写入含中文的 `.tscn` 文件。**

### 正确做法

**写入 UTF-8 文本（无 BOM）：**
```powershell
$path = "/path/to/file.tscn"
$utf8 = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($path, $content, $utf8)
```

**写入 UTF-8 二进制（无 BOM）：**
```powershell
$path = "/path/to/file.tscn"
[System.IO.File]::WriteAllBytes($path, $bytes)
```

**读入时保留正确编码：**
```powershell
$bytes = [System.IO.File]::ReadAllBytes($path)
$text = [System.Text.Encoding]::UTF8.GetString($bytes)
```

### 编辑 .tscn 文件的标准流程
1. 用 `[System.IO.File]::ReadAllBytes` 读入二进制
2. 用 `[System.Text.Encoding]::UTF8.GetString` 转为字符串
3. 使用 `-replace` 完成文本修改
4. 用 `[System.IO.File]::WriteAllText` + `UTF8Encoding($false)` 写回（无 BOM）

### 验证方法
写回后检查是否引入损坏：
```powershell
$text = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($path))
# 不应出现任何 � (U+FFFD) 替代字符
if ($text.Contains([char]0xFFFD)) { Write-Host "编码损坏!" }
```