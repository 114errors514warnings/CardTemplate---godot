extends SceneTree
## Offline sprite packing. Retains the generated source colors and alpha;
## isolates disconnected poses and puts them on a common 256 px canvas.

const BASE = "res://Resources/Images/Characters/FrameV2/"
const CELL = 256
const FEET = 228

func _initialize() -> void:
	call_deferred("run")

func run() -> void:
	var reports = []
	for spec in [
		["swordmaster_equipped", 128.0 / 338.0, [127, 404, 697, 982, 1243, 1528, 1900], [626,626,626,626,626,626,628], [0,3,2,2,-4,-6,-8], [1,1,1,1,1,1,0]],
		["swordmaster_body", 128.0 / 338.0, [127, 404, 697, 982, 1243, 1528, 1900], [626,626,626,626,626,626,628], [0,3,2,2,-4,-6,-8], [1,1,1,1,1,1,0]],
		["isera_equipped", 128.0 / 436.0, [145,435,727,1014,1300,1600,1875], [673,671,671,672,674,674,673], [-5,1,2,3,4,6,-2], [1,1,1,1,1,1,4]],
		["isera_body", 128.0 / 436.0, [145,435,727,1014,1300,1600,1875], [673,671,671,672,674,674,673], [-5,1,2,3,4,6,-2], [1,1,1,1,1,1,4]],
	]:
		reports.append(pack_sheet(spec))
	var report_file = FileAccess.open(BASE + "packing_report.json", FileAccess.WRITE)
	report_file.store_string(JSON.stringify(reports, "\t"))
	print("FRAME_PACK_PASS: four sheets, seven isolated poses each")
	quit()

func pack_sheet(spec: Array) -> Dictionary:
	var sheet_name: String = spec[0]
	var factor: float = spec[1]
	var source_path = BASE + "Sources/" + sheet_name + "_source.png"
	var source = Image.load_from_file(source_path)
	assert(source != null, "Missing frame source: " + source_path)
	source.convert(Image.FORMAT_RGBA8)
	var width = source.get_width()
	var height = source.get_height()
	var data = source.get_data()
	var labels = PackedInt32Array()
	labels.resize(width * height)
	labels.fill(-1)
	var components: Array = []
	for pixel in range(width * height):
		if labels[pixel] >= 0 or data[pixel*4+3] < 32:
			continue
		var component_id = components.size()
		var queue = PackedInt32Array([pixel])
		labels[pixel] = component_id
		var head = 0
		var min_x = width
		var min_y = height
		var max_x = 0
		var max_y = 0
		while head < queue.size():
			var current = queue[head]
			head += 1
			var x = current % width
			var y = current / width
			min_x = mini(min_x,x)
			max_x = maxi(max_x,x)
			min_y = mini(min_y,y)
			max_y = maxi(max_y,y)
			for offset in [-1,1,-width,width]:
				var next = current + offset
				if next < 0 or next >= labels.size(): continue
				if (offset == -1 and x == 0) or (offset == 1 and x == width-1): continue
				if labels[next] >= 0 or data[next*4+3] < 32: continue
				labels[next] = component_id
				queue.append(next)
		components.append({"id":component_id,"count":queue.size(),"left":min_x,"top":min_y,"right":max_x+1,"bottom":max_y+1})
	components.sort_custom(func(a,b): return a.count > b.count)
	assert(components.size() >= 7, "Fewer than seven poses: " + sheet_name)
	components = components.slice(0,7)
	components.sort_custom(func(a,b): return a.left < b.left)
	var packed = Image.create(CELL*7,CELL,false,Image.FORMAT_RGBA8)
	var frame_report: Array = []
	for frame in range(7):
		var item = components[frame]
		assert(item.count > 10000,"Incomplete pose: " + sheet_name)
		var left = maxi(0,item.left-2)
		var top = maxi(0,item.top-2)
		var right = mini(width,item.right+2)
		var bottom = mini(height,item.bottom+2)
		var isolated = Image.create(right-left,bottom-top,false,Image.FORMAT_RGBA8)
		for y in range(top,bottom):
			for x in range(left,right):
				var belongs = labels[y*width+x] == item.id
				if not belongs:
					for dy in range(-2,3):
						for dx in range(-2,3):
							var xx = x+dx
							var yy = y+dy
							if xx >= 0 and xx < width and yy >= 0 and yy < height and labels[yy*width+xx] == item.id:
								belongs = true
				if belongs:
					isolated.set_pixel(x-left,y-top,source.get_pixel(x,y))
		isolated.resize(maxi(1,roundi(isolated.get_width()*factor)),maxi(1,roundi(isolated.get_height()*factor)),Image.INTERPOLATE_NEAREST)
		var location = Vector2i(roundi(CELL*.5+(left-spec[2][frame])*factor)+spec[4][frame],roundi(FEET+(top-spec[3][frame])*factor)+spec[5][frame])
		assert(location.x >= 0 and location.y >= 0 and location.x+isolated.get_width() <= CELL and location.y+isolated.get_height() <= CELL,"Pose leaves cell: " + sheet_name)
		packed.blit_rect(isolated,Rect2i(Vector2i.ZERO,isolated.get_size()),location+Vector2i(frame*CELL,0))
		frame_report.append({"frame":frame,"source_bounds":[left,top,right,bottom],"source_anchor":[spec[2][frame],spec[3][frame]],"correction":[spec[4][frame],spec[5][frame]],"destination":[location.x,location.y],"scale":factor})
	assert(packed.save_png(BASE+sheet_name+".png") == OK,"Cannot save packed sheet")
	write_frames_resource(sheet_name)
	return {"name":sheet_name,"cell":[CELL,CELL],"foot_anchor":[CELL/2,FEET],"source_sha256":FileAccess.get_sha256(source_path),"frames":frame_report}

func write_frames_resource(sheet_name: String) -> void:
	var text = '[gd_resource type="SpriteFrames" load_steps=9 format=3]\n\n'
	text += '[ext_resource type="Texture2D" path="'+BASE+sheet_name+'.png" id="1"]\n\n'
	for frame in range(7):
		text += '[sub_resource type="AtlasTexture" id="frame'+str(frame)+'"]\natlas = ExtResource("1")\nregion = Rect2('+str(frame*CELL)+', 0, 256, 256)\n\n'
	text += '[resource]\nanimations = [\n'
	var animations = [["idle",[0,1],true,1.5],["walk",[2,3],true,8.0],["attack",[4,5,0],false,9.0],["hurt",[3 if sheet_name.begins_with("isera") else 6,0],false,8.0],["death",[6],false,1.0]]
	for animation in animations:
		text += '{"name": &"'+animation[0]+'", "loop": '+str(animation[2]).to_lower()+', "speed": '+str(animation[3])+', "frames": ['
		for frame in animation[1]:
			text += '{"duration": 1.0, "texture": SubResource("frame'+str(frame)+'")},'
		text += ']},\n'
	text += ']\n'
	var resource_file = FileAccess.open(BASE+sheet_name+'.tres',FileAccess.WRITE)
	resource_file.store_string(text)
