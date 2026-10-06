extends SceneTree

# Public parity seam: C# traces test browser state, feedback, and reports.
const MODES := ["GuidedPractice", "Assessment"]
const PipettingQuality := preload("res://scripts/model/pipetting_quality_teaching.gd")

func _init() -> void:
	var oracle: Variant = JSON.parse_string(FileAccess.get_file_as_string("../tests/parity/fixtures/csharp-oracle.json"))
	if typeof(oracle) != TYPE_DICTIONARY:
		_fail("oracle fixture unavailable")
		return
	if Level1Simulation.new().restore(str(oracle.csharpPersistenceSamples.level1)):
		_fail("C# Level 1 save must not cross the web persistence boundary")
		return
	for mode in MODES:
		if not _compare_trace(oracle.level1[mode], mode):
			return
	for edge_id in oracle.level1Edges:
		if not _compare_trace(oracle.level1Edges[edge_id], "edge " + str(edge_id)):
			return
	if not _edge_contracts():
		return
	print("PASS: Level 1 full snapshot/report/feedback parity: 2 normal and 3 edge traces plus recovery and restore contracts")
	quit(0)

func _compare_trace(expected_run: Dictionary, mode: String) -> bool:
	var model := Level1Simulation.new()
	var simulation_mode := str(expected_run.trace[0].start.mode)
	if not _same_normalized(model.start(simulation_mode), expected_run.trace[0].start):
		_fail("%s start snapshot" % mode)
		return false
	if not _same_normalized(model.build_report(), expected_run.trace[0].report):
		_fail("%s start report" % mode)
		return false
	for index in range(1, expected_run.trace.size()):
		var expected: Dictionary = expected_run.trace[index]
		var action: Dictionary = expected.action
		var actual := model.submit(action.type, action.get("target"), action.get("value"))
		for field in ["accepted", "message", "scienceTitle", "scienceLesson", "whyItMatters"]:
			if actual.get(field) != expected.get(field):
				_fail("%s step %s %s feedback %s" % [mode, index, action.type, field])
				return false
		if not _same_normalized(actual.snapshot, expected.snapshot):
			_fail("%s step %s %s snapshot" % [mode, index, action.type])
			return false
		if not _same_normalized(model.build_report(), expected.report):
			_fail("%s step %s %s report %s" % [mode, index, action.type, _first_difference(_without_attempt_ids(model.build_report()), _without_attempt_ids(expected.report))])
			return false
	var saved := model.save()
	var restored := Level1Simulation.new()
	if not restored.restore(saved):
		_fail("%s restore rejected valid save" % mode)
		return false
	if not _same_normalized(restored.snapshot(), model.snapshot()) or not _same_normalized(restored.build_report(), model.build_report()):
		_fail("%s restore replay" % mode)
		return false
	var before := model.snapshot()
	var tampered: Dictionary = JSON.parse_string(saved)
	tampered.snapshot.sourceRemainingVolumeUl.Blank = 249
	if model.restore(JSON.stringify(tampered)) or not _same_normalized(model.snapshot(), before):
		_fail("%s tampered restore atomic rejection" % mode)
		return false
	var mutations := ["missing-assessment-categories", "invalid-active-well", "unsafe-source-url", "empty-source-authority", "bad-source-port", "csharp-shaped-save", "wrong-level-envelope", "well-quantity", "ledger"]
	if model.snapshot().readerRan:
		mutations.append("scenario-seed")
	for mutation in mutations:
		var malformed: Dictionary = JSON.parse_string(saved)
		match mutation:
			"missing-assessment-categories": malformed.rules.assessmentCategories = null
			"invalid-active-well": malformed.scenario.activeWells[0].well = "Z99"
			"unsafe-source-url": malformed.sources.entries[0].url = "http://example.invalid/"
			"empty-source-authority": malformed.sources.entries[0].url = "https:///path"
			"bad-source-port": malformed.sources.entries[0].url = "https://example.invalid:not-a-port"
			"csharp-shaped-save": malformed.erase("webEnvelope")
			"wrong-level-envelope": malformed.levelId = "level2"
			"scenario-seed": malformed.scenario.seed = int(malformed.scenario.seed) + 1
			"well-quantity": malformed.snapshot.wells[0].transferVolumeUl = 100
			"ledger": malformed.snapshot.ledger.pop_at(1)
		if model.restore(JSON.stringify(malformed, "", true, true)) or not _same_normalized(model.snapshot(), before):
			_fail("%s %s restore atomic rejection" % [mode, mutation])
			return false
	var valid_uri: Dictionary = JSON.parse_string(saved)
	valid_uri.sources.entries[0].url = "https://example.com"
	var uri_probe := Level1Simulation.new()
	if not uri_probe.restore(JSON.stringify(valid_uri, "", true, true)):
		_fail("%s bare HTTPS host accepts" % mode)
		return false
	return true

func _edge_contracts() -> bool:
	var model := Level1Simulation.new()
	model.start("GuidedPractice")
	for action in ["WearPpe", "DisinfectBench", "CheckMaterials", "VerifyLabels", "ReviewPlateMap", "AttachTip"]:
		model.submit(action)
	model.submit("SetVolume", null, 50)
	var prior_volume: Variant = model.snapshot().selectedVolumeUl
	if model.submit("SetVolume", null, NAN).accepted or model.snapshot().selectedVolumeUl != prior_volume:
		_fail("nonfinite volume rejects atomically")
		return false
	model.submit("SelectSource", "Blank")
	model.submit("PressFirstStop")
	model.submit("MoveToSource", "Blank")
	if not model.submit("ReleaseFast").accepted:
		_fail("fast release records")
		return false
	var partial_save := model.save()
	var partial_restored := Level1Simulation.new()
	if not partial_restored.restore(partial_save) or partial_restored.snapshot().pipetteStage != "LoadedAtSource":
		_fail("partial-cycle restore")
		return false
	if not model.submit("RetryTransferCheckpoint").accepted or not model.snapshot().issues.any(func(issue): return issue.code == "FastAspirationRelease" and issue.resolved):
		_fail("guided recovery retains resolved fast-release issue")
		return false
	model.submit("MislabelSelectedWell", "B1")
	if not model.submit("CorrectLabel", "B1").accepted:
		_fail("scoped label correction")
		return false
	var assessment := Level1Simulation.new()
	assessment.start("Assessment")
	if assessment.submit("RetryTransferCheckpoint").accepted:
		_fail("assessment retry rejection")
		return false
	var teaching_sources: Array = model.sources.entries
	var guided_card: Variant = PipettingQuality.optional_why("GuidedPractice", teaching_sources)
	if guided_card == null or PipettingQuality.optional_why("Assessment", teaching_sources) != null or PipettingQuality.debrief_card(teaching_sources) == null:
		_fail("source-gated pipetting quality mode boundary")
		return false
	var guided_html: Variant = PipettingQuality.html_card(teaching_sources)
	if not str(guided_card.body).contains("not guaranteed to be linear") or guided_html == null or not str(guided_html).begins_with("<h2>Pipetting quality: accuracy and precision</h2><p>") or PipettingQuality.debrief_card([]) != null or PipettingQuality.html_card([]) != null:
		_fail("source-gated pipetting quality scientific boundary")
		return false
	return true

func _same_normalized(actual: Variant, expected: Variant) -> bool:
	return _same_tree(_without_attempt_ids(actual), _without_attempt_ids(expected))

func _without_attempt_ids(value: Variant) -> Variant:
	if typeof(value) == TYPE_DICTIONARY:
		var copy: Dictionary = {}
		for key in value:
			if key != "attemptId": copy[key] = _without_attempt_ids(value[key])
		return copy
	if typeof(value) == TYPE_ARRAY:
		var copy: Array = []
		for item in value: copy.append(_without_attempt_ids(item))
		return copy
	return value

func _same_tree(left: Variant, right: Variant) -> bool:
	if left == null or right == null: return left == right
	if typeof(left) == TYPE_INT and typeof(right) == TYPE_INT: return left == right
	if typeof(left) in [TYPE_INT, TYPE_FLOAT] or typeof(right) in [TYPE_INT, TYPE_FLOAT]: return is_equal_approx(float(left), float(right))
	if typeof(left) == TYPE_DICTIONARY and typeof(right) == TYPE_DICTIONARY:
		if left.size() != right.size(): return false
		for key in left:
			if not right.has(key) or not _same_tree(left[key], right[key]): return false
		return true
	if typeof(left) == TYPE_ARRAY and typeof(right) == TYPE_ARRAY:
		if left.size() != right.size(): return false
		for index in left.size():
			if not _same_tree(left[index], right[index]): return false
		return true
	return left == right

func _first_difference(left: Variant, right: Variant, path := "report") -> String:
	if left == null or right == null: return path if left != right else ""
	if typeof(left) == TYPE_DICTIONARY and typeof(right) == TYPE_DICTIONARY:
		for key in left:
			if not right.has(key): return path + "." + str(key) + " missing right"
			var child: String = _first_difference(left[key], right[key], path + "." + str(key))
			if not child.is_empty(): return child
		for key in right:
			if not left.has(key): return path + "." + str(key) + " missing left"
		return ""
	if typeof(left) == TYPE_ARRAY and typeof(right) == TYPE_ARRAY:
		if left.size() != right.size(): return path + " length"
		for index in left.size():
			var child: String = _first_difference(left[index], right[index], path + "[%s]" % index)
			if not child.is_empty(): return child
		return ""
	if typeof(left) in [TYPE_INT, TYPE_FLOAT] or typeof(right) in [TYPE_INT, TYPE_FLOAT]: return "" if is_equal_approx(float(left),float(right)) else path
	return "" if left == right else path

func _fail(message: String) -> void:
	push_error("MISMATCH: " + message)
	quit(1)
