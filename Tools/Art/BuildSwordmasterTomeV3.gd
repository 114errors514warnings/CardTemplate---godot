extends SceneTree

## Offline export only: seven action poses and four dedicated death poses.
const BASE = "res://Resources/Images/Characters/FrameV3/"
const NAME = "swordmaster_tome"
const CELL = 384
const HEIGHT = 256
const FEET = 228
const SCALE = 0.38

func _initialize() -> void:
	call_deferred("run")

func run() -> void:
	var actions_path = BASE + "Sources/swordmaster_tome_actions_source.png"
	var death_path = BASE + "Sources/swordmaster_tome_death_source.png"
	var actions = Image.load_from_file(actions_path)
	var death = Image.load_from_file(death_path)
	assert(actions != null and death != null, "Missing tome source image")
	actions.convert(Image.FORMAT_RGBA8)
	death.convert(Image.FORMAT_RGBA8)
	var atlas = Image.create(CELL * 11, HEIGHT, false, Image.FORMAT_RGBA8)
	var report = []
	var components = find_main_components(actions)
	var action_anchors = [140, 410, 700, 1000, 1280, 1610, 1900]
	for i in range(7):
		var part = components[i]
		var source_rect = Rect2i(part.left, part.top, part.right - part.left, part.bottom - part.top)
		var pose = Image.create(source_rect.size.x, source_rect.size.y, false, Image.FORMAT_RGBA8)
		for y in range(source_rect.position.y, source_rect.end.y):
			for x in range(source_rect.position.x, source_rect.end.x):
				if part.labels[y * actions.get_width() + x] == part.id:
					pose.set_pixel(x - source_rect.position.x, y - source_rect.position.y, actions.get_pixel(x, y))
		report.append(add_pose(atlas, pose, i, part.left, action_anchors[i], "action"))
	var death_components = find_death_components(death)
	var death_anchors = [210, 650, 1170, 1670]
	var groups = [[0], [1, 2], [3], [4, 5]]
	for i in range(4):
		var selected = []
		var left = death.get_width(); var top = death.get_height(); var right = 0; var bottom = 0
		for component_index in groups[i]:
			var part = death_components[component_index]
			selected.append(part.id)
			left = mini(left, part.left); top = mini(top, part.top)
			right = maxi(right, part.right); bottom = maxi(bottom, part.bottom)
		var pose = Image.create(right - left, bottom - top, false, Image.FORMAT_RGBA8)
		for y in range(top, bottom):
			for x in range(left, right):
				if death_components[0].labels[y * death.get_width() + x] in selected:
					pose.set_pixel(x - left, y - top, death.get_pixel(x, y))
		report.append(add_pose(atlas, pose, i + 7, left, death_anchors[i], "death"))
	assert(atlas.save_png(BASE + NAME + ".png") == OK)
	write_resource()
	var file = FileAccess.open(BASE + NAME + "_report.json", FileAccess.WRITE)
	file.store_string(JSON.stringify({"actions_sha256": FileAccess.get_sha256(actions_path), "death_sha256": FileAccess.get_sha256(death_path), "cell": [CELL, HEIGHT], "frames": report}, "\t"))
	print("SWORDMASTER_TOME_V3_PASS: 7 action + 4 death frames")
	quit()

func find_main_components(source: Image) -> Array:
	var width = source.get_width()
	var height = source.get_height()
	var pixels = source.get_data()
	var labels = PackedInt32Array()
	labels.resize(width * height)
	labels.fill(-1)
	var parts = []
	for origin in range(width * height):
		if labels[origin] >= 0 or pixels[origin * 4 + 3] < 32: continue
		var id = parts.size()
		var queue = PackedInt32Array([origin])
		labels[origin] = id
		var head = 0
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
		parts.append({"id": id, "count": queue.size(), "left": left, "top": top, "right": right, "bottom": bottom, "labels": labels})
	parts.sort_custom(func(a, b): return a.count > b.count)
	assert(parts.size() >= 7 and parts[6].count > 10000, "Missing action pose")
	parts = parts.slice(0, 7)
	parts.sort_custom(func(a, b): return a.left < b.left)
	return parts

func find_death_components(source: Image) -> Array:
	var width = source.get_width()
	var height = source.get_height()
	var pixels = source.get_data()
	var labels = PackedInt32Array()
	labels.resize(width * height)
	labels.fill(-1)
	var parts = []
	for origin in range(width * height):
		if labels[origin] >= 0 or pixels[origin * 4 + 3] < 32: continue
		var id = parts.size()
		var queue = PackedInt32Array([origin])
		labels[origin] = id
		var head = 0
		var left = width; var top = height; var right = 0; var bottom = 0
		while head < queue.size():
			var current = queue[head]
			head += 1
			var x = current % width; var y = current / width
			left = mini(left, x); top = mini(top, y)
			right = maxi(right, x + 1); bottom = maxi(bottom, y + 1)
			for offset in [-1, 1, -width, width]:
				var next = current + offset
				if next < 0 or next >= labels.size(): continue
				if (offset == -1 and x == 0) or (offset == 1 and x == width - 1): continue
				if labels[next] >= 0 or pixels[next * 4 + 3] < 32: continue
				labels[next] = id
				queue.append(next)
		parts.append({"id": id, "count": queue.size(), "left": left, "top": top, "right": right, "bottom": bottom, "labels": labels})
	parts.sort_custom(func(a, b): return a.count > b.count)
	assert(parts.size() >= 6 and parts[5].count > 5000, "Missing tome death body or book")
	parts = parts.slice(0, 6)
	parts.sort_custom(func(a, b): return a.left < b.left)
	return parts

func visible_bounds(image: Image, from_x: int, to_x: int) -> Rect2i:
	var left = image.get_width(); var top = image.get_height()
	var right = 0; var bottom = 0
	for y in range(image.get_height()):
		for x in range(from_x, to_x):
			if image.get_pixel(x, y).a8 < 32: continue
			left = mini(left, x); top = mini(top, y)
			right = maxi(right, x + 1); bottom = maxi(bottom, y + 1)
	assert(right > left and bottom > top, "Empty death pose")
	return Rect2i(left, top, right - left, bottom - top)

func add_pose(atlas: Image, pose: Image, index: int, source_left: int, anchor_x: int, kind: String) -> Dictionary:
	pose.resize(roundi(pose.get_width() * SCALE), roundi(pose.get_height() * SCALE), Image.INTERPOLATE_NEAREST)
	var bottom = 0
	for y in range(pose.get_height()):
		for x in range(pose.get_width()):
			if pose.get_pixel(x, y).a8 > 64: bottom = y
	var dest = Vector2i(roundi(CELL * 0.5 + (source_left - anchor_x) * SCALE), FEET - bottom)
	assert(dest.x >= 0 and dest.y >= 0 and dest.x + pose.get_width() <= CELL and dest.y + pose.get_height() <= HEIGHT,
		"Pose leaves atlas cell " + str(index) + " at " + str(dest))
	atlas.blit_rect(pose, Rect2i(Vector2i.ZERO, pose.get_size()), dest + Vector2i(index * CELL, 0))
	return {"frame": index, "kind": kind, "source_left": source_left, "anchor_x": anchor_x, "destination": [dest.x, dest.y]}

func write_resource() -> void:
	var data = '[gd_resource type="SpriteFrames" load_steps=13 format=3]\n\n'
	data += '[ext_resource type="Texture2D" path="' + BASE + NAME + '.png" id="1"]\n\n'
	for index in range(11):
		data += '[sub_resource type="AtlasTexture" id="frame' + str(index) + '"]\natlas = ExtResource("1")\nregion = Rect2(' + str(index * CELL) + ', 0, ' + str(CELL) + ', 256)\n\n'
	data += '[resource]\nanimations = [\n'
	for animation in [["idle", [0, 1], true, 1.5], ["walk", [2, 3], true, 8.0], ["attack", [4, 5, 0], false, 9.0], ["hurt", [6, 0], false, 8.0], ["death", [7, 8, 9, 10], false, 7.0]]:
		data += '{"name": &"' + animation[0] + '", "loop": ' + str(animation[2]).to_lower() + ', "speed": ' + str(animation[3]) + ', "frames": ['
		for index in animation[1]:
			data += '{"duration": 1.0, "texture": SubResource("frame' + str(index) + '")},'
		data += ']},\n'
	data += ']\n'
	var file = FileAccess.open(BASE + NAME + '.tres', FileAccess.WRITE)
	file.store_string(data)
