extends SceneTree

## Offline-only packing of complete equipped poses. The game only loads .tres files.
const OUT = "res://Resources/Images/Characters/FrameV2/"
const PIXEL = "res://Resources/Images/Characters/Pixel/"
const FEET = 228

func _initialize() -> void:
	call_deferred("run")

func run() -> void:
	var report = []
	report.append(pack("swordmaster_spear_equipped", OUT + "Sources/swordmaster_spear_equipped_source.png", 384, 0.60,
		[0, 250, 500, 850, 1180, 1450, 1900, 2172], [135, 378, 693, 1013, 1308, 1652, 2030], 509))
	report.append(pack("swordmaster_bow_equipped", PIXEL + "swordmaster_bow_action_sheet_v2.png", 256, 0.43,
		[0, 290, 580, 870, 1160, 1450, 1740, 2032], [144, 429, 731, 1019, 1288, 1592, 1856], 616))
	report.append(pack("swordmaster_tome_equipped", PIXEL + "swordmaster_tome_action_sheet_v2.png", 256, 0.42,
		[0, 290, 580, 870, 1160, 1450, 1740, 2032], [146, 439, 726, 1024, 1318, 1613, 1886], 616))
	report.append(pack_connected("isera_spear_equipped", 384, 0.46, [120, 345, 604, 880, 1184, 1587, 2013]))
	report.append(pack_connected("isera_tome_equipped", 256, 0.40, [155, 445, 753, 1065, 1340, 1678, 2048]))
	report.append(pack_connected("isera_greatsword_equipped", 320, 0.43, [145, 379, 690, 1029, 1320, 1661, 1994]))
	var file = FileAccess.open(OUT + "equipment_expansion_report.json", FileAccess.WRITE)
	file.store_string(JSON.stringify(report, "\t"))
	print("EQUIPMENT_FRAME_PACK_PASS: six sheets; 7 poses each")
	quit()

func pack(name: String, source_path: String, cell_width: int, factor: float, cuts: Array, anchors: Array, foot_y: int) -> Dictionary:
	var source = Image.load_from_file(source_path)
	assert(source != null, "Missing source: " + source_path)
	source.convert(Image.FORMAT_RGBA8)
	var result = Image.create(cell_width * 7, 256, false, Image.FORMAT_RGBA8)
	var frames = []
	for index in range(7):
		var left = source.get_width()
		var top = source.get_height()
		var right = 0
		var bottom = 0
		for y in range(source.get_height()):
			for x in range(cuts[index], cuts[index + 1]):
				if source.get_pixel(x, y).a8 < 32:
					continue
				left = mini(left, x)
				top = mini(top, y)
				right = maxi(right, x + 1)
				bottom = maxi(bottom, y + 1)
		assert(right > left and bottom > top, "Empty frame: " + name + " " + str(index))
		left = maxi(cuts[index], left - 2)
		top = maxi(0, top - 2)
		right = mini(cuts[index + 1], right + 2)
		bottom = mini(source.get_height(), bottom + 2)
		var pose = source.get_region(Rect2i(left, top, right - left, bottom - top))
		pose.resize(roundi(pose.get_width() * factor), roundi(pose.get_height() * factor), Image.INTERPOLATE_NEAREST)
		var dest = Vector2i(roundi(cell_width * 0.5 + (left - anchors[index]) * factor), FEET - visible_bottom(pose))
		assert(dest.x >= 0 and dest.y >= 0 and dest.x + pose.get_width() <= cell_width and dest.y + pose.get_height() <= 256,
			"Frame leaves cell: " + name + " " + str(index) + " " + str(dest))
		result.blit_rect(pose, Rect2i(Vector2i.ZERO, pose.get_size()), dest + Vector2i(index * cell_width, 0))
		frames.append({"frame": index, "source_bounds": [left, top, right, bottom], "source_anchor": [anchors[index], foot_y], "destination": [dest.x, dest.y]})
	assert(result.save_png(OUT + name + ".png") == OK, "Failed to save: " + name)
	write_resource(name, cell_width)
	return {"name": name, "cell": [cell_width, 256], "anchor": [cell_width / 2, FEET], "source_sha256": FileAccess.get_sha256(source_path), "frames": frames}

func pack_connected(name: String, cell_width: int, factor: float, anchors: Array) -> Dictionary:
	var source_path = OUT + "Sources/" + name + "_source.png"
	var source = Image.load_from_file(source_path)
	assert(source != null, "Missing source: " + source_path)
	source.convert(Image.FORMAT_RGBA8)
	var width = source.get_width()
	var height = source.get_height()
	var pixels = source.get_data()
	var labels = PackedInt32Array()
	labels.resize(width * height)
	labels.fill(-1)
	var components = []
	for origin in range(width * height):
		if labels[origin] >= 0 or pixels[origin * 4 + 3] < 32: continue
		var id = components.size()
		var queue = PackedInt32Array([origin])
		labels[origin] = id
		var head = 0
		var bounds = Rect2i(origin % width, origin / width, 1, 1)
		var left = width
		var top = height
		var right = 0
		var bottom = 0
		while head < queue.size():
			var current = queue[head]
			head += 1
			var x = current % width
			var y = current / width
			left = mini(left, x); top = mini(top, y)
			right = maxi(right, x + 1); bottom = maxi(bottom, y + 1)
			for offset in [-1, 1, -width, width]:
				var next = current + offset
				if next < 0 or next >= labels.size(): continue
				if (offset == -1 and x == 0) or (offset == 1 and x == width - 1): continue
				if labels[next] >= 0 or pixels[next * 4 + 3] < 32: continue
				labels[next] = id
				queue.append(next)
		components.append({"id": id, "count": queue.size(), "left": left, "top": top, "right": right, "bottom": bottom})
	components.sort_custom(func(a, b): return a.count > b.count)
	assert(components.size() >= 7, "Fewer than seven connected poses: " + name)
	components = components.slice(0, 7)
	components.sort_custom(func(a, b): return a.left < b.left)
	var result = Image.create(cell_width * 7, 256, false, Image.FORMAT_RGBA8)
	var frames = []
	for index in range(7):
		var item = components[index]
		assert(item.count > 10000, "Incomplete pose: " + name + " " + str(index))
		var left = maxi(0, item.left - 2)
		var top = maxi(0, item.top - 2)
		var right = mini(width, item.right + 2)
		var bottom = mini(height, item.bottom + 2)
		var pose = Image.create(right - left, bottom - top, false, Image.FORMAT_RGBA8)
		for y in range(top, bottom):
			for x in range(left, right):
				if labels[y * width + x] == item.id:
					pose.set_pixel(x - left, y - top, source.get_pixel(x, y))
		pose.resize(roundi(pose.get_width() * factor), roundi(pose.get_height() * factor), Image.INTERPOLATE_NEAREST)
		var dest = Vector2i(roundi(cell_width * 0.5 + (left - anchors[index]) * factor),
			FEET - visible_bottom(pose))
		assert(dest.x >= 0 and dest.y >= 0 and dest.x + pose.get_width() <= cell_width and dest.y + pose.get_height() <= 256,
			"Frame leaves cell: " + name + " " + str(index) + " " + str(dest))
		result.blit_rect(pose, Rect2i(Vector2i.ZERO, pose.get_size()), dest + Vector2i(index * cell_width, 0))
		frames.append({"frame": index, "source_bounds": [left, top, right, bottom], "source_anchor": [anchors[index], item.bottom - 1], "destination": [dest.x, dest.y]})
	assert(result.save_png(OUT + name + ".png") == OK, "Failed to save: " + name)
	write_resource(name, cell_width)
	return {"name": name, "cell": [cell_width, 256], "anchor": [cell_width / 2, FEET], "source_sha256": FileAccess.get_sha256(source_path), "frames": frames}

func visible_bottom(image: Image) -> int:
	for y in range(image.get_height() - 1, -1, -1):
		for x in range(image.get_width()):
			if image.get_pixel(x, y).a8 > 64: return y
	return 0

func write_resource(name: String, cell_width: int) -> void:
	var data = '[gd_resource type="SpriteFrames" load_steps=9 format=3]\n\n'
	data += '[ext_resource type="Texture2D" path="' + OUT + name + '.png" id="1"]\n\n'
	for index in range(7):
		data += '[sub_resource type="AtlasTexture" id="frame' + str(index) + '"]\natlas = ExtResource("1")\nregion = Rect2(' + str(index * cell_width) + ', 0, ' + str(cell_width) + ', 256)\n\n'
	data += '[resource]\nanimations = [\n'
	for animation in [["idle", [0, 1], true, 1.5], ["walk", [2, 3], true, 8.0], ["attack", [4, 5, 0], false, 9.0], ["hurt", [6, 0], false, 8.0], ["death", [6], false, 1.0]]:
		data += '{"name": &"' + animation[0] + '", "loop": ' + str(animation[2]).to_lower() + ', "speed": ' + str(animation[3]) + ', "frames": ['
		for index in animation[1]:
			data += '{"duration": 1.0, "texture": SubResource("frame' + str(index) + '")},'
		data += ']},\n'
	data += ']\n'
	var file = FileAccess.open(OUT + name + '.tres', FileAccess.WRITE)
	file.store_string(data)
