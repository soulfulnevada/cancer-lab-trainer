class_name BrowserLabWorld
extends Node3D

# Original symbolic 3D bench. It displays model state but never determines outcomes.
var camera: Camera3D
var pipette: MeshInstance3D
var liquid: MeshInstance3D
var reader: MeshInstance3D
var plates: Array[MeshInstance3D] = []
var well_meshes: Dictionary = {}
var plate_colonies: Dictionary = {}
var plate_bases: Dictionary = {}
var level1_nodes: Array[Node3D] = []
var level2_tubes: Array[Node3D] = []
var level2_labels: Array[Label3D] = []
var source_positions := {"Blank":Vector3(-2.8, 1.55, -1.45), "Vehicle control":Vector3(-2.0, 1.55, -1.45), "Fictional treatment":Vector3(-1.2, 1.55, -1.45)}
var closeup := false
var closeup_override: Variant = null # learner's camera toggle; cleared when the phase changes
var current_level := ""
var current_phase := ""
const COLONIES_PER_PLATE := 12
const ROLE_COLORS := {"Blank":Color("b9c4cc"), "Vehicle control":Color("4f9fe8"), "Fictional treatment":Color("f0a04b")}

func _ready() -> void:
	var environment := Environment.new()
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = Color("081629")
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color("7696bd")
	environment.ambient_light_energy = 0.7
	var world_environment := WorldEnvironment.new()
	world_environment.environment = environment
	add_child(world_environment)
	var key := DirectionalLight3D.new()
	key.rotation_degrees = Vector3(-48, -28, 0)
	key.light_energy = 1.3
	add_child(key)
	var fill := OmniLight3D.new()
	fill.position = Vector3(-2, 4, 3)
	fill.light_color = Color("56d7e8")
	fill.light_energy = 3.2
	fill.omni_range = 16.0
	add_child(fill)
	camera = Camera3D.new()
	camera.current = true
	# The canvas is often portrait (beside the task panel), so frame by width to keep the bench in view.
	camera.keep_aspect = Camera3D.KEEP_WIDTH
	camera.fov = 62.0
	add_child(camera)
	_box(Vector3(12, 0.3, 6), Vector3(0, 0, 0), Color("31485c"))
	_box(Vector3(11, 1.1, 5.4), Vector3(0, -0.7, 0), Color("1e3247"))
	reader = _box(Vector3(2.8, 1.6, 2), Vector3(3.3, 0.95, -0.7), Color("c9dde1"))
	level1_nodes.append(reader)
	level1_nodes.append(_box(Vector3(1.35, 0.65, 0.07), Vector3(3.3, 1.15, 0.34), Color("2d7689")))
	for x in [-2.8, -2.0, -1.2]:
		level1_nodes.append(_cylinder(0.22, 0.8, Vector3(x, 0.58, -1.45), Color("64bec2")))
	for index in 2:
		var tube := _cylinder(0.29, 1.25, Vector3(-3.4 + index * 0.9, 0.72, -1.18), Color("6ed2d3"))
		var tube_label := Label3D.new()
		tube_label.text = "T" if index == 0 else "NC"
		tube_label.position = tube.position + Vector3(0, 0.9, 0)
		tube_label.font_size = 48
		add_child(tube_label)
		level2_tubes.append(tube)
		level2_tubes.append(tube_label)
	# Four separate symbolic Level 2 plates. Their seeded colonies appear only after a recorded view.
	# Labels sit in front of each plate so they never cover the colonies.
	for index in 4:
		var plate_position := Vector3(-2.55 + index * 1.7, 0.24, 0.9)
		plate_bases["P%s" % (index + 1)] = plate_position
		var plate := _cylinder(0.78, 0.14, plate_position, Color("438fbd"))
		plates.append(plate)
		var plate_label := Label3D.new()
		plate_label.text = "P%s" % (index + 1)
		plate_label.position = plate_position + Vector3(0, 0.18, 1.08)
		plate_label.font_size = 54
		add_child(plate_label)
		level2_labels.append(plate_label)
		var colonies: Array[MeshInstance3D] = []
		for colony_index in COLONIES_PER_PLATE:
			var angle := float((colony_index * 137 + index * 47 + 17) % 360) * PI / 180.0
			var radius := 0.14 + float((colony_index + index) % 4) * 0.13
			var colony := _cylinder(0.07, 0.04, plate_position + Vector3(cos(angle) * radius, 0.09, sin(angle) * radius), Color("f5ead1"))
			colony.visible = false
			colonies.append(colony)
		plate_colonies["P%s" % (index + 1)] = colonies
	# A full 96-well plate with the six active Level 1 wells visibly highlighted.
	level1_nodes.append(_box(Vector3(4.4, 0.12, 3.1), Vector3(-0.75, 0.2, 0.05), Color("d5e2e6")))
	for row in 8:
		for column in 12:
			var id := "%s%s" % [char(65 + row), column + 1]
			var active_well := row == 1 and column < 6
			var mesh := _cylinder(0.13 if active_well else 0.075, 0.05 if active_well else 0.035, Vector3(-2.62 + column * 0.34, 0.29, -1.05 + row * 0.30), Color("1f4056"))
			well_meshes[id] = mesh
			level1_nodes.append(mesh)
	pipette = _cylinder(0.18, 1.65, Vector3(-4.0, 1.6, 0.35), Color("e0eef2"))
	liquid = _cylinder(0.05, 0.45, Vector3(-4.0, 0.65, 0.35), Color("41e2c6"))
	liquid.visible = false
	_set_camera()

func show_level(_level: String) -> void:
	for plate in plates:
		plate.visible = true

func refresh(level: String, state: Dictionary, selected_well: String) -> void:
	current_level = level
	var phase := str(state.get("phase", ""))
	if phase != current_phase:
		current_phase = phase
		closeup_override = null
	# Observation phases default to a close view of the plates; the camera button still overrides.
	closeup = bool(closeup_override) if closeup_override != null else ((level == "level2" and phase in ["Observe", "Explain", "DiagnosticCase", "Complete"]) or (level == "level1" and phase == "Transfers"))
	if level == "level1":
		for node in level1_nodes: node.visible = true
		for node in level2_tubes: node.visible = false
		for node in level2_labels: node.visible = false
		pipette.visible = true
		for plate in plates: plate.visible = false
		liquid.visible = bool(state.get("tipAttached", false)) and float(state.get("aspiratedVolumeUl", 0.0)) > 0.0
		var stage := str(state.get("pipetteStage", "ReadyInAir"))
		if stage.contains("Source"):
			pipette.position = source_positions.get(str(state.get("selectedSource", "")), Vector3(-2.0, 1.55, -1.45))
		elif stage.contains("Destination"):
			var bound_destination := str(state.get("boundDestination", selected_well))
			pipette.position = well_meshes.get(bound_destination, well_meshes["B1"]).position + Vector3(0, 1.2, 0)
		else:
			pipette.position = Vector3(-4.0, 1.6, 0.35)
		liquid.position = pipette.position + Vector3(0, -0.95, 0)
		reader.material_override = _material(Color("9ed8cf") if bool(state.get("readerConfigured", false)) else Color("c9dde1"))
		var filled := {}
		for well in state.get("wells", []):
			if well.get("transferVolumeUl") != null: filled[str(well.get("well"))] = str(well.get("sourceRole", ""))
		for id in well_meshes:
			var active: bool = id in ["B1", "B2", "B3", "B4", "B5", "B6"]
			var color := Color("1f4056")
			if filled.has(id): color = ROLE_COLORS.get(filled[id], Color("49acc9"))
			elif active: color = Color("49acc9")
			if id == selected_well and not filled.has(id): color = Color("f7df72")
			well_meshes[id].material_override = _material(color)
	else:
		for node in level1_nodes: node.visible = false
		for node in level2_tubes: node.visible = true
		for node in level2_labels: node.visible = true
		pipette.visible = false
		for plate in plates: plate.visible = true
		liquid.visible = false
		var displayed: Array = state.get("casePlates", []) if state.get("casePlates", []).size() > 0 else state.get("plates", [])
		var viewed: Array = []
		for plate_state in displayed:
			if plate_state.get("activeView") != null and plate_state.get("growth") == "Present": viewed.append(str(plate_state.get("id")))
		var seed := int(state.get("scenarioSeed", 0))
		for index in plates.size():
			var plate_state: Variant = displayed[index] if displayed.size() > index else {}
			var glow: bool = typeof(plate_state) == TYPE_DICTIONARY and plate_state.get("activeView") == "excitation" and plate_state.get("fluorescence") == "Detected"
			plates[index].material_override = _material(Color("e8d7a1"))
			var plate_id := "P%s" % (index + 1)
			var colony_index := 0
			for colony in plate_colonies.get(plate_id, []):
				var angle := float((seed + index * 59 + colony_index * 137) % 360) * PI / 180.0
				var radius := 0.14 + float((seed + colony_index * 7) % 4) * 0.13
				colony.position = plate_bases[plate_id] + Vector3(cos(angle) * radius, 0.09, sin(angle) * radius)
				colony.visible = typeof(plate_state) == TYPE_DICTIONARY and bool(plate_state.get("normalViewSeen", false)) and plate_state.get("growth") == "Present"
				colony.material_override = _glow_material() if glow else _material(Color("f5ead1"))
				colony_index += 1
	_set_camera()

func toggle_closeup() -> void:
	closeup = not closeup
	closeup_override = closeup
	_set_camera()

func _set_camera() -> void:
	if closeup:
		if current_level == "level2":
			camera.position = Vector3(0, 4.2, 5.9)
			camera.look_at(Vector3(0, 0.2, 0.9))
		else:
			camera.position = Vector3(-1.7, 3.4, 4.4)
			camera.look_at(Vector3(-1.5, 0.5, -0.5))
	else:
		camera.position = Vector3(0, 4.3, 8.0)
		camera.look_at(Vector3(0, 0.4, 0))

func _well_index(well: String) -> int:
	return clampi(well.substr(1).to_int() - 1, 0, 5)

func _box(size: Vector3, position: Vector3, color: Color) -> MeshInstance3D:
	var node := MeshInstance3D.new()
	var mesh := BoxMesh.new()
	mesh.size = size
	node.mesh = mesh
	node.position = position
	node.material_override = _material(color)
	add_child(node)
	return node

func _cylinder(radius: float, height: float, position: Vector3, color: Color) -> MeshInstance3D:
	var node := MeshInstance3D.new()
	var mesh := CylinderMesh.new()
	mesh.top_radius = radius
	mesh.bottom_radius = radius
	mesh.height = height
	node.mesh = mesh
	node.position = position
	node.material_override = _material(color)
	add_child(node)
	return node

func _glow_material() -> StandardMaterial3D:
	# Unshaded so the bench's blue fill light cannot tint GFP-associated colonies toward cyan.
	var material := StandardMaterial3D.new()
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	material.albedo_color = Color("3dff5c")
	return material

func _material(color: Color) -> StandardMaterial3D:
	var material := StandardMaterial3D.new()
	material.albedo_color = color
	material.metallic = 0.12
	material.roughness = 0.42
	return material
