# Godot 2D 卡牌游戏：AI 辅助美术素材与骨骼动画生产管道方案

本方案旨在针对基于 Godot 引擎开发的 2D 卡牌类游戏，梳理从**美术素材规范**、**骨骼动画选型**、**AI 友好型文件格式**到 **AI 自动化组装与绑定**的完整工作流。

---

## 一、 素材图片规范与分类指南

在卡牌游戏中，需要兼顾**画质表现、内存占用（UI & 动画性能）**以及**自动化工作流的效率**。

| 素材类型 | 推荐格式 | 分辨率/规范要求 | 核心要点与用途 |
| --- | --- | --- | --- |
| **卡面立绘 / 动态角色** | PNG (分层) / PSD | 原画 2048×2048+<br>

<br>导出按实际卡面尺寸 | 需具备无损透明通道（RGBA 8-bit）。角色必须进行部件拆分（头发、五官、肢体、饰品），且被遮挡区域需进行像素补全。 |
| **背景图 / 大型 UI 遮罩** | WebP / PNG | 匹配设计分辨率（如 1080p/4K） | 无复杂透明边缘的背景可采用高品质 WebP，降低游戏安装包与运行时显存占用。 |
| **纹理图集 (TextureAtlas)** | PNG + Atlas / JSON | 建议最大尺寸不超过 4096×4096 | 用于降低 Draw Calls。Spine/DragonBones 导出或 Godot 自动处理图集。 |

---

## 二、 2D 骨骼动画实现方案对比

在 Godot 引擎中实现卡面微动、呼吸感或高动态技能表现时，主流方案对比分析如下：

```
       +-------------------------------------------------------+
       |             2D 骨骼动画方案选型 (Godot)               |
       +-------------------------------------------------------+
                                  |
         +------------------------+------------------------+
         |                                                 |
[ Spine 2D (推荐) ]                              [ Godot 原生 Skeleton2D ]
  • 商业行业标准                                   • 完全免费且零外部依赖
  • 拥有官方 spine-godot 扩展                     • 深入集成 AnimationPlayer
  • 支持物理碰撞、高级网格变形                      • 适合基础呼吸感 / 轻量动画

```

| 方案 | 优势 | 劣势 | 适用场景 |
| --- | --- | --- | --- |
| **Spine 2D**<br>

<br>*(强烈推荐)* | • 行业标准，功能极强（网格 Mesh 变形、IK 约束、2.5D 透视效果）<br>

<br>• 拥有官方维护的 **spine-godot (GDExtension)** 插件 | • 软件商业授权费用较高<br>

<br>• GDExtension 需要配置特定导出环境 | **高端动态卡面**、高质量角色技能动作 |
| **Godot 内置 2D 骨骼**<br>

<br>(`Skeleton2D` + `Polygon2D`) | • **完全免费**，零外部依赖，与引擎原生 API、`AnimationPlayer` 深度结合<br>

<br>• 无版本更新冲突或插件兼容问题 | • 缺乏高级软骨骼物理与便捷的网格蒙皮编辑体验，制作效率略低 | 基础卡牌呼吸动画、简单 UI 动态 |
| **Live2D / DragonBones** | • Live2D 在二次元立绘微表情上表现极佳 | • DragonBones 停止维护，Godot 4 适配不稳定<br>

<br>• Live2D Godot 扩展多由社区维护，维护度参差不齐 | DragonBones 不推荐新项目选用；主打 VTuber 级互动可考虑 Live2D |

---

## 三、 面向 AI 自动化组装的图片/矢量格式选择

要实现生成素材后的 **AI 自动化组装与对齐**，纯粹的独立 PNG 缺乏层级与空间数据。推荐使用带有**结构元数据**的格式：

```
[素材拆分/生成]  --->  [矢量/元数据结构化]  --->  [大模型/脚本推理]  --->  [Godot 场景/动画]
  (PNG 分层)            (SVG / PSD)             (对齐/锚点计算)            (.tscn / .json)

```

1. **SVG (Scalable Vector Graphics) —— 最适合 AI 解析与自动组装**
* **代码化属性**：SVG 本质是 XML 文本。大语言模型（LLM）或 Python 脚本可直接读取 XML DOM 树（如 `<g id="arm_left">`），无需图像识别即可直接理解图层关系。
* **锚点定位**：矢量路径与节点可直接计算出关键连接点（如手腕、脖子枢轴点）。


2. **PSD / PSB (Photoshop Document) —— 行业标准结构载体**
* 保留完整的图层树、图层命名、坐标偏移量（X/Y Offset）、混合模式与边界框（Bounding Box）。
* 方便 AI 自动化提取元数据，并通过脚本导出为 Spine / Godot 可读取的配置文件。


3. **Spine / DragonBones JSON (JSON + PNG Atlas)**
* 像素纹理与空间结构解耦，文本型 JSON 极度适合大语言模型进行读取、理解与参数纠偏。



---

## 四、 AI 辅助素材生成与自动化组装工作流

目前尚无“一键生成全套带骨骼 Godot 组装包”的单体 AI，但可通过 **AI 工具链组合** 搭建高效生产线：

### 1. 拆图与补画（解决分层痛点）

* **LayerDiffusion (ComfyUI / SD)**：生成原生带透明通道（Alpha）的角色图像，减少手动抠图。
* **Meta SAM (Segment Anything Model)**：一键精准框选角色手部、头发、服装配件并提取为独立图层。
* **Inpainting (SD / PS Firefly)**：对提取后留下的背景空白区域进行 AI 局部重绘补全。

### 2. AI 自动化组装三大路径

#### 路径 A：代码与视觉 AI 自动定位组装（Python + SAM + Vision Model）

1. 使用 **SAM** 识别出各个部件并生成掩码（Mask）。
2. 投喂至视觉大模型（如 GPT-4 Vision），让 AI 推断部件间的枢轴点位置（Pivot Points，例如：“`arm_left` 旋转中心位于图片 `(x:20, y:15)`”）。
3. Python 脚本自动计算偏移量，生成组装好的卡面与 Godot 场景结构。

#### 路径 B：结构元数据驱动（PSD/SVG + LLM 生成场景结构）

1. 读取分层 PSD/SVG 的图层名称与尺寸。
2. 引导 LLM 推断标准层级顺序（`背景` < `后发` < `躯干` < `脖子` < `头部` < `前发` < `武器`）。
3. AI 自动输出 Godot `.tscn` 场景节点表或 Spine `.json` 配置文件：
```json
{
  "bone": "arm_R",
  "parent": "torso",
  "x": 45,
  "y": 120,
  "rotation": 0
}

```



#### 路径 C：ComfyUI 扩散缝合组装（缝合处画风统一）

* 将分层部件粗略拼合后，通过 **ControlNet (OpenPose/Tile) + Inpainting** 遮罩重绘，让 AI 自动缝合边缘并统一光影。

---

## 五、 Godot 引擎落地方案推荐总结

1. **中间格式**：统一采用 **分层 PSD** 或 **SVG** 作为 AI 与引擎间的中转格式，保留坐标与层级信息。
2. **场景生成**：编写 Python 或 Godot `EditorScript` 脚本，读取 AI 生成的元数据（JSON），一键在 Godot 编辑器内自动生成包含正确层级关系的 `Sprite2D` 节点树。
3. **动画制作**：根据预算，使用 **Spine** 挂载 Mesh / IK 导出动态卡面，或直接在 Godot 中使用 `Skeleton2D` + `AnimationPlayer` 完成极简呼吸感动画制作。

下面为你提供一个在 Godot 4 中可以直接运行的 **`EditorScript` 示例脚本**。

这个脚本的作用是：**在 Godot 编辑器内部直接执行**，读取 AI 生成的图层 JSON 描述文件，自动创建 `Node2D` 根节点、在内部构建对应的 `Sprite2D` 层级树、加载对应的图片纹理，并自动设置好位置、缩放和层级（`z_index`）。


---

### 1. JSON 配置文件结构示例 (`card_layers.json`)

将此文件保存在 Godot 项目根目录或素材目录下（例如 `res://assets/card_layers.json`）：

```json
{
  "card_name": "HeroCard",
  "layers": [
    {
      "name": "Background",
      "texture_path": "res://assets/layers/background.png",
      "position": [0, 0],
      "scale": [1.0, 1.0],
      "z_index": 0
    },
    {
      "name": "Body_Torso",
      "texture_path": "res://assets/layers/torso.png",
      "position": [0, 20],
      "scale": [1.0, 1.0],
      "z_index": 1
    },
    {
      "name": "Arm_Right",
      "texture_path": "res://assets/layers/arm_r.png",
      "position": [45, 15],
      "scale": [1.0, 1.0],
      "z_index": 2
    },
    {
      "name": "Head",
      "texture_path": "res://assets/layers/head.png",
      "position": [0, -80],
      "scale": [1.0, 1.0],
      "z_index": 3
    },
    {
      "name": "Hair_Front",
      "texture_path": "res://assets/layers/hair_front.png",
      "position": [0, -95],
      "scale": [1.0, 1.0],
      "z_index": 4
    }
  ]
}

```

---

### 2. Godot EditorScript 脚本 (`build_card_scene.gd`)

在 Godot 中新建一个 GDScript 文件（继承自 `EditorScript`）：

```gdscript
@tool
extends EditorScript

# 配置 JSON 文件路径和场景保存路径
const JSON_PATH: String = "res://assets/card_layers.json"
const SAVE_SCENE_PATH: String = "res://scenes/generated_card.tscn"

func _run() -> void:
	print("=== 开始解析 JSON 并构建 Godot 场景树 ===")
	
	# 1. 读取并解析 JSON 文件
	if not FileAccess.file_exists(JSON_PATH):
		printerr("错误：找不到 JSON 文件 -> ", JSON_PATH)
		return
		
	var file = FileAccess.open(JSON_PATH, FileAccess.READ)
	var json_string = file.get_as_text()
	file.close()
	
	var json = JSON.new()
	var parse_result = json.parse(json_string)
	if parse_result != OK:
		printerr("JSON 解析失败: ", json.get_error_message(), " 行: ", json.get_error_line())
		return
		
	var data: Dictionary = json.data
	var card_name: String = data.get("card_name", "GeneratedCard")
	var layers: Array = data.get("layers", [])
	
	# 2. 创建卡牌根节点 Node2D
	var root_node = Node2D.new()
	root_node.name = card_name
	
	# 3. 遍历图层列表并生成 Sprite2D 子节点
	for layer_data in layers:
		var layer_name: String = layer_data.get("name", "Layer")
		var tex_path: String = layer_data.get("texture_path", "")
		var pos_arr: Array = layer_data.get("position", [0, 0])
		var scale_arr: Array = layer_data.get("scale", [1.0, 1.0])
		var z_idx: int = layer_data.get("z_index", 0)
		
		var sprite = Sprite2D.new()
		sprite.name = layer_name
		
		# 设置位置与缩放
		sprite.position = Vector2(pos_arr[0], pos_arr[1])
		sprite.scale = Vector2(scale_arr[0], scale_arr[1])
		sprite.z_index = z_idx
		
		# 加载并赋予纹理资源
		if ResourceLoader.exists(tex_path):
			var texture = load(tex_path) as Texture2D
			sprite.texture = texture
		else:
			print("警告：图层 '%s' 指定的图片资源不存在 -> %s" % [layer_name, tex_path])
			
		# 将 Sprite 添加为根节点的子节点
		root_node.add_child(sprite)
		
		# 【关键】将节点所有者设为 root_node，否则保存成 .tscn 场景文件时子节点会丢失
		sprite.owner = root_node
	
	# 4. 将生成的节点树保存为 .tscn 场景文件
	var packed_scene = PackedScene.new()
	var pack_result = packed_scene.pack(root_node)
	
	if pack_result == OK:
		# 确保保存目录存在
		var dir_path = SAVE_SCENE_PATH.get_base_dir()
		if not DirAccess.dir_exists_absolute(dir_path):
			DirAccess.make_dir_recursive_absolute(dir_path)
			
		var save_err = ResourceSaver.save(packed_scene, SAVE_SCENE_PATH)
		if save_err == OK:
			print("成功！已生成卡牌场景文件 -> ", SAVE_SCENE_PATH)
			# 刷新 Godot 文件系统，让左侧文件面板能即时看到新生成的场景
			get_editor_interface().get_resource_filesystem().scan()
		else:
			printerr("保存场景文件失败，错误码：", save_err)
	else:
		printerr("打包节点树失败")
		
	# 释放内存（因为未把 root_node 添加到当前编辑器运行树中）
	root_node.free()

```

---

### 3. 如何在 Godot 编辑器中使用此脚本？

1. 在 Godot 中准备好你的图层图片文件和对应的 `card_layers.json`。
2. 将上面的 GDScript 脚本保存到项目中（如 `res://scripts/build_card_scene.gd`）。
3. 在 Godot 编辑器顶部菜单栏中，点击：**文件 (File) -> 运行脚本 (Run File)**（快捷键 `Ctrl + Shift + X`）。
4. 选择 `build_card_scene.gd` 并运行。
5. 运行完成后，你会在 `res://scenes/` 目录下看到新生成的 `generated_card.tscn`，双击打开即可直接进行绑定、加骨骼或制作动画！


这里为你提供一个完整的 Python 自动化处理脚本。

脚本利用 `psd-tools` 解析 PSD 文件，**提取每个图层（包含隐藏层可选项）的独立透明 PNG 图片**，同时计算每个图层相对于画布中心的 X/Y 偏移量与绘制顺序（`z_index`），最后导出配套的 `JSON` 结构文件。

---

### 1. 环境准备

运行脚本前，需要安装 `psd-tools` 和 `Pillow`：

```bash
pip install psd-tools Pillow

```

---

### 2. Python 导出脚本 (`export_psd_for_godot.py`)

将以下代码保存为 Python 脚本运行：

```python
import os
import json
from psd_tools import PSDImage

def export_psd_to_godot(psd_path, output_dir, res_prefix="res://assets/layers/"):
    """
    解析 PSD 文件，导出透明 PNG 图层和 Godot 可读的 JSON 结构
    
    :param psd_path: PSD 文件路径
    :param output_dir: 导出的图片和 JSON 保存目录
    :param res_prefix: 在 Godot 中读取时的资源路径前缀
    """
    if not os.path.exists(psd_path):
        print(f"错误: 找不到 PSD 文件 -> {psd_path}")
        return

    # 创建输出文件夹
    png_out_dir = os.path.join(output_dir, "layers")
    os.makedirs(png_out_dir, exist_ok=True)

    print(f"开始加载 PSD: {psd_path}")
    psd = PSDImage.open(psd_path)

    # 画布中心点坐标 (以 PSD 画布中心作为 Godot 场景的 (0, 0) 原点)
    canvas_center_x = psd.width / 2.0
    canvas_center_y = psd.height / 2.0

    layers_data = []
    
    # 递归/遍历所有图层 (按照 PSD 从底到顶的物理渲染顺序)
    # psd.descendants() 可以深度遍历包含图层组在内的所有叶子图层
    z_index = 0
    for layer in psd.descendants():
        # 跳过图层组(Group)节点和空白/无效图层
        if layer.is_group() or layer.width == 0 or layer.height == 0:
            continue

        # 清理图层名称，防止非法文件名字符
        safe_layer_name = "".join([c if c.isalnum() or c in ("_", "-") else "_" for c in layer.name])
        if not safe_layer_name:
            safe_layer_name = f"layer_{z_index}"

        png_file_name = f"{z_index:02d}_{safe_layer_name}.png"
        png_save_path = os.path.join(png_out_dir, png_file_name)

        # 1. 渲染并保存当前图层为 RGBA 透明 PNG
        layer_image = layer.composite()
        if layer_image:
            layer_image.save(png_save_path)
            print(f"导出图层图片 [{z_index}]: {png_file_name}")

        # 2. 计算相对于 PSD 画布中心点的原点偏移量
        # psd-tools 得到的 layer.offset 是图层左上角的全局绝对像素坐标
        layer_left, layer_top = layer.offset
        layer_center_x = layer_left + (layer.width / 2.0)
        layer_center_y = layer_top + (layer.height / 2.0)

        # Godot 中的 2D 坐标系：基于原点 (0,0) 的相对偏移
        offset_x = layer_center_x - canvas_center_x
        offset_y = layer_center_y - canvas_center_y

        # 3. 构造组装节点元数据
        godot_tex_path = f"{res_prefix.rstrip('/')}/{png_file_name}"
        layer_info = {
            "name": safe_layer_name,
            "texture_path": godot_tex_path,
            "position": [round(offset_x, 2), round(offset_y, 2)],
            "scale": [1.0, 1.0],
            "z_index": z_index
        }

        layers_data.append(layer_info)
        z_index += 1

    # 4. 构建主 JSON 结构
    psd_filename_stem = os.path.splitext(os.path.basename(psd_path))[0]
    json_data = {
        "card_name": psd_filename_stem,
        "canvas_size": [psd.width, psd.height],
        "layers": layers_data
    }

    # 保存 JSON 文件
    json_save_path = os.path.join(output_dir, "card_layers.json")
    with open(json_save_path, "w", encoding="utf-8") as f:
        json.dump(json_data, f, ensure_ascii=False, indent=2)

    print(f"\n成功！全套资源已导出至 -> {output_dir}")
    print(f"JSON 配置文件路径 -> {json_save_path}")

# ================= 脚本执行入口 =================
if __name__ == "__main__":
    # PSD 文件路径
    INPUT_PSD = "my_card_character.psd"
    
    # 导出的目标目录
    OUTPUT_FOLDER = "./exported_card_assets"
    
    # 在 Godot 中配置的图片路径前缀 (需与 Godot 项目内的实际保存路径一致)
    GODOT_RES_PREFIX = "res://assets/layers/"

    export_psd_to_godot(INPUT_PSD, OUTPUT_FOLDER, GODOT_RES_PREFIX)

```

---

### 3. 生成的 JSON 数据结构样例 (`card_layers.json`)

运行脚本后生成的 JSON 输出如下，完美兼容此前提供的 Godot `EditorScript` 脚本解析格式：

```json
{
  "card_name": "my_card_character",
  "canvas_size": [2048, 2048],
  "layers": [
    {
      "name": "Background",
      "texture_path": "res://assets/layers/00_Background.png",
      "position": [0.0, 0.0],
      "scale": [1.0, 1.0],
      "z_index": 0
    },
    {
      "name": "Torso",
      "texture_path": "res://assets/layers/01_Torso.png",
      "position": [12.5, 45.0],
      "scale": [1.0, 1.0],
      "z_index": 1
    },
    {
      "name": "Arm_Left",
      "texture_path": "res://assets/layers/02_Arm_Left.png",
      "position": [-120.0, 15.0],
      "scale": [1.0, 1.0],
      "z_index": 2
    }
  ]
}

```

---

### 4. 关键对齐逻辑说明

1. **原点锚定（Center-based Alignment）**：
在 2D 游戏引擎（如 Godot）中，卡牌场景通常将原点 `(0, 0)` 设在卡片几何中心。脚本会自动将 PSD 的像素绝对位置转换为以 **PSD 画布中心点** 为参考系的相对 `(X, Y)` 偏移。
2. **绘制渲染顺序（Draw Order）**：
`psd.descendants()` 遵循从底部图层到顶部图层的读取顺序，导出时递增赋值 `z_index`（`0, 1, 2...`），保证直接导入 Godot 后不会出现图层遮挡错乱。