extends SceneTree

# Public parity seam: independently generated C# action traces are replayed by
# the browser model. Only generated attempt IDs and numeric representation are
# normalized; every other snapshot and report field must remain identical.
const MODES := ["GuidedPractice", "Assessment"]
const CASES := ["missing-arabinose", "swapped-tube-labels", "missing-dna", "failed-antibiotic-negative-control"]

func _init() -> void:
	var oracle: Variant = JSON.parse_string(FileAccess.get_file_as_string("../tests/parity/fixtures/csharp-oracle.json"))
	if typeof(oracle) != TYPE_DICTIONARY:
		_fail("oracle fixture unavailable")
		return
	if Level2Simulation.new().restore(str(oracle.csharpPersistenceSamples.level2)):
		_fail("C# Level 2 save must not cross the web persistence boundary")
		return
	for mode in MODES:
		for case_id in CASES:
			if not _compare_trace(oracle, mode, case_id):
				return
	if not _linked_retry_round_trip():
		return
	print("PASS: Level 2 full snapshot/report parity: 8 traces, 448 action checkpoints, replay, tamper rejection, and linked retry")
	quit(0)

func _compare_trace(oracle: Dictionary, mode: String, case_id: String) -> bool:
	var expected_case: Dictionary = oracle.level2[mode][case_id]
	var model := Level2Simulation.new()
	var actual_start := model.start(mode)
	var expected_start: Dictionary = expected_case.trace[0].start
	if not _same_normalized(actual_start, expected_start):
		_fail("%s/%s start snapshot" % [mode, case_id])
		return false
	if not _same_normalized(model.build_report(), expected_case.trace[0].report):
		_fail("%s/%s start report: %s" % [mode, case_id, _first_difference(_without_generated_ids(model.build_report()), _without_generated_ids(expected_case.trace[0].report))])
		return false
	for index in range(1, expected_case.trace.size()):
		var expected: Dictionary = expected_case.trace[index]
		var action: Dictionary = expected.action
		var actual := model.submit(action.type, action.get("target"), action.get("value"))
		if actual.accepted != expected.accepted or actual.message != expected.message or actual.coaching != expected.get("coaching", ""):
			_fail("%s/%s step %s %s action result" % [mode, case_id, index, action.type])
			return false
		if not _same_normalized(actual.snapshot, expected.snapshot):
			_fail("%s/%s step %s %s snapshot" % [mode, case_id, index, action.type])
			return false
		if not _same_normalized(model.build_report(), expected.report):
			_fail("%s/%s step %s %s report" % [mode, case_id, index, action.type])
			return false
	var expected_report: Dictionary = expected_case.report
	if not _same_normalized(model.build_report(), expected_report):
		_fail("%s/%s final report" % [mode, case_id])
		return false
	var saved := model.save()
	var restored := Level2Simulation.new()
	if not restored.restore(saved):
		_fail("%s/%s restore rejected valid save" % [mode, case_id])
		return false
	if not _same_normalized(restored.snapshot(), model.snapshot()) or not _same_normalized(restored.build_report(), model.build_report()):
		_fail("%s/%s replay state/report" % [mode, case_id])
		return false
	var before := model.snapshot()
	var synthetic_csharp_save: Dictionary = JSON.parse_string(saved)
	synthetic_csharp_save.erase("webEnvelope")
	synthetic_csharp_save.erase("levelId")
	if model.restore(JSON.stringify(synthetic_csharp_save, "", true, true)) or not _same_normalized(model.snapshot(), before):
		_fail("%s/%s C#-shaped save atomic rejection" % [mode, case_id])
		return false
	var tampered := saved.replace("\"formatVersion\":1", "\"formatVersion\":99")
	if model.restore(tampered) or not _same_normalized(model.snapshot(), before):
		_fail("%s/%s tampered restore atomic rejection" % [mode, case_id])
		return false
	var state_tampered: Dictionary = JSON.parse_string(saved)
	state_tampered.snapshot.tubeContents.T = "Buffer"
	if model.restore(JSON.stringify(state_tampered)) or not _same_normalized(model.snapshot(), before):
		_fail("%s/%s action-history tampered restore atomic rejection" % [mode, case_id])
		return false
	var definition_tamper: Dictionary = JSON.parse_string(saved)
	definition_tamper.scenario.seed = int(definition_tamper.scenario.seed) + 1
	if model.restore(JSON.stringify(definition_tamper)) or not _same_normalized(model.snapshot(), before):
		_fail("%s/%s seed tampered restore atomic rejection" % [mode, case_id])
		return false
	definition_tamper = JSON.parse_string(saved)
	definition_tamper.sources.entries[0].url = "http://example.invalid/"
	if model.restore(JSON.stringify(definition_tamper)) or not _same_normalized(model.snapshot(), before):
		_fail("%s/%s source URL tampered restore atomic rejection" % [mode, case_id])
		return false
	definition_tamper = JSON.parse_string(saved)
	definition_tamper.sources.entries[0].url = "https:///path"
	if model.restore(JSON.stringify(definition_tamper, "", true, true)) or not _same_normalized(model.snapshot(), before):
		_fail("%s/%s empty source authority atomic rejection" % [mode, case_id])
		return false
	definition_tamper = JSON.parse_string(saved)
	definition_tamper.sources.entries[0].url = "https://example.invalid:not-a-port"
	if model.restore(JSON.stringify(definition_tamper, "", true, true)) or not _same_normalized(model.snapshot(), before):
		_fail("%s/%s invalid source port atomic rejection" % [mode, case_id])
		return false
	definition_tamper = JSON.parse_string(saved)
	definition_tamper.sources.entries[0].url = "https://example.com"
	var source_probe := Level2Simulation.new()
	if not source_probe.restore(JSON.stringify(definition_tamper, "", true, true)):
		_fail("%s/%s bare HTTPS host accepts" % [mode, case_id])
		return false
	definition_tamper = JSON.parse_string(saved)
	definition_tamper.sources.entries[0].id = ""
	if model.restore(JSON.stringify(definition_tamper)) or not _same_normalized(model.snapshot(), before):
		_fail("%s/%s source ID tampered restore atomic rejection" % [mode, case_id])
		return false
	definition_tamper = JSON.parse_string(saved)
	definition_tamper.rubric.criteria = {}
	if model.restore(JSON.stringify(definition_tamper)) or not _same_normalized(model.snapshot(), before):
		_fail("%s/%s rubric criteria tampered restore atomic rejection" % [mode, case_id])
		return false
	return true

func _linked_retry_round_trip() -> bool:
	var model := Level2Simulation.new()
	var parent: String = str(model.start("GuidedPractice").attemptId)
	var retry := model.start_linked_retry(parent, "Assessment")
	if retry.retryOfAttemptId != parent or retry.attemptId == parent:
		_fail("linked retry provenance")
		return false
	var restored := Level2Simulation.new()
	if not restored.restore(model.save()) or restored.snapshot().retryOfAttemptId != parent:
		_fail("linked retry restore provenance")
		return false
	return true

func _same_normalized(actual: Variant, expected: Variant) -> bool:
	var left: Variant = _without_generated_ids(actual)
	var right: Variant = _without_generated_ids(expected)
	return _same_tree(left, right)

func _without_generated_ids(value: Variant) -> Variant:
	if typeof(value) == TYPE_DICTIONARY:
		var copy: Dictionary = {}
		for key in value:
			if key != "attemptId" and key != "retryOfAttemptId":
				copy[key] = _without_generated_ids(value[key])
		return copy
	if typeof(value) == TYPE_ARRAY:
		var copy: Array = []
		for item in value:
			copy.append(_without_generated_ids(item))
		return copy
	return value

func _same_tree(left: Variant, right: Variant) -> bool:
	if left == null or right == null:
		return left == right
	if typeof(left) in [TYPE_INT, TYPE_FLOAT] or typeof(right) in [TYPE_INT, TYPE_FLOAT]:
		return is_equal_approx(float(left), float(right))
	if typeof(left) == TYPE_DICTIONARY and typeof(right) == TYPE_DICTIONARY:
		if left.size() != right.size():
			return false
		for key in left:
			if not right.has(key) or not _same_tree(left[key], right[key]):
				return false
		return true
	if typeof(left) == TYPE_ARRAY and typeof(right) == TYPE_ARRAY:
		if left.size() != right.size():
			return false
		for index in left.size():
			if not _same_tree(left[index], right[index]):
				return false
		return true
	return left == right

func _first_difference(left: Variant, right: Variant, path := "report") -> String:
	if left == null or right == null:
		return path + " left=%s right=%s" % [left, right] if left != right else ""
	if typeof(left) == TYPE_DICTIONARY and typeof(right) == TYPE_DICTIONARY:
		for key in left:
			if not right.has(key): return path + "." + str(key) + " missing on right"
			var child := _first_difference(left[key], right[key], path + "." + str(key))
			if not child.is_empty(): return child
		for key in right:
			if not left.has(key): return path + "." + str(key) + " missing on left"
		return ""
	if typeof(left) == TYPE_ARRAY and typeof(right) == TYPE_ARRAY:
		if left.size() != right.size(): return path + " lengths %s/%s" % [left.size(), right.size()]
		for index in left.size():
			var child := _first_difference(left[index], right[index], path + "[%s]" % index)
			if not child.is_empty(): return child
		return ""
	if typeof(left) in [TYPE_INT, TYPE_FLOAT] or typeof(right) in [TYPE_INT, TYPE_FLOAT]:
		return "" if is_equal_approx(float(left), float(right)) else path + " left=%s right=%s" % [left, right]
	return "" if left == right else path + " left=%s right=%s" % [left, right]

func _fail(message: String) -> void:
	push_error("MISMATCH: " + message)
	quit(1)
