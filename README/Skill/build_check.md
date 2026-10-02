# 构建检查规则 (Build Check Rule) — 仅 C# 代码改动适用

## 规则
每次完成 **C# 代码**修改后，自动调用 `dotnet build` 命令检查编译是否通过。
改**配置**（`DataBase/**`）与改**文档**（`README/**`）**不做编译检测**；代码改动还需在 build 之后跑 `dotnet test`。分级口径与按需检查清单见 [编译验证规则.md](编译验证规则.md)。

## 执行方式
在项目根目录 (`D:\MY\My Game\卡牌模拟器`) 下执行：

```
dotnet build 卡牌模拟器.csproj -v q --nologo
```

## 要求
- 编译必须 **0 错误、0 警告**
- 在提交 (commit) 或发布前必须通过此检查
- 如果编译失败，需要修复错误后再继续

## 备注
- 使用 `--no-restore` 可以跳过 NuGet 恢复（如果本地包已缓存）
- 编译输出位于 `.godot/mono/temp/bin/Debug/卡牌模拟器.dll`
