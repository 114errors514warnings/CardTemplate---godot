# 工程规范

## UI 图片资源目录

- 所有运行时图片资源统一放在根目录 `Images/` 下，不得写入 `Assets/`、`Resources/`、`DataBase/` 或功能脚本目录。
- UI 图片按功能建立 `Images/UI/` 子目录，例如剧情背景使用 `Images/UI/Story/Backgrounds/`，人物立绘使用 `Images/UI/Story/Portraits/`，图标使用 `Images/UI/Icons/`。
- JSON 与代码只保存资源 ID；由所属系统按约定目录解析资源路径。新图片必须放入对应目录后再写入配置。

- [开发记录](开发记录.md)
