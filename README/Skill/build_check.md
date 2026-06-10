# 构建检查规则 (Build Check Rule)

## 规则
每次完成代码修改后，自动调用 `dotnet build` 命令检查编译是否通过。

## 执行方式
在项目根目录 (`D:\MY\My Game\卡牌模拟器`) 下执行：

```
dotnet build 2>&1
```

## 要求
- 编译必须 **0 错误、0 警告**
- 在提交 (commit) 或发布前必须通过此检查
- 如果编译失败，需要修复错误后再继续

## 备注
- 使用 `--no-restore` 可以跳过 NuGet 恢复（如果本地包已缓存）
- 编译输出位于 `.godot/mono/temp/bin/Debug/卡牌模拟器.dll`
