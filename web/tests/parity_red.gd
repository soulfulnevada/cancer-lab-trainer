extends SceneTree

# Deliberately proves the rejected first GD draft is not a C# parity implementation.
func _init() -> void:
	var oracle: Variant = JSON.parse_string(FileAccess.get_file_as_string("../tests/parity/fixtures/csharp-oracle.json"))
	if typeof(oracle) != TYPE_DICTIONARY:
		push_error("RED: oracle fixture unavailable")
		quit(2)
		return
	var model: Level1Simulation = Level1Simulation.new()
	model.start("GuidedPractice")
	var gd: Dictionary = model.submit("SetVolume", "", 50)
	var cs: Dictionary = oracle.level1.trace[1]
	if gd.accepted == cs.accepted and gd.message == cs.message:
		push_error("RED test unexpectedly matched the rejected draft")
		quit(1)
	else:
		print("EXPECTED RED: Level 1 mismatch. C# message='%s'; GD message='%s'" % [cs.message, gd.message])
		quit(1)
