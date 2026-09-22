# 工程规范

## 资源目录

- 运行时资源统一放在根目录 `Resources/` 下，**图片资源放 `Resources/Images/`**；不得写入 `Assets/`、`DataBase/` 或功能脚本目录。
- `res://` 路径固定以 `res://Resources/Images/` 开头；代码与 JSON 都不写绝对磁盘路径。
- UI 图片按功能建立 `Resources/Images/UI/` 子目录，例如剧情背景使用 `Resources/Images/UI/Story/Backgrounds/`，人物立绘使用 `Resources/Images/UI/Story/Portraits/`，图标使用 `Resources/Images/UI/Icons/`；角色像素资源使用 `Resources/Images/Characters/Pixel/`。
- JSON 与代码只保存资源 ID；由所属系统按约定目录解析资源路径。新图片必须放入对应目录后再写入配置。
- 迁移记录：2026-09-22 根目录 `Images/` 整体移动到 `Resources/Images/`（当时其中只有图片）；旧路径 `res://Images/…` 作废，勿再新增该目录。

- [开发记录](开发记录.md)
