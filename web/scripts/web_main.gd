extends Node

# Browser controller: the GDScript simulations remain the only science/state authority.
const WEB_VERSION := "1.2.1-web.3"
const LabWorld := preload("res://scripts/ui/lab_world.gd")

var level := ""
var level1: Level1Simulation
var level2: Level2Simulation
var selected_well := "B1"
var selected_source := "Blank"
var last_feedback := "Choose a level and learning mode."
var saved_attempts := {"level1": {}, "level2": {}}
var js_callback: Variant
var world: Node3D

func _ready() -> void:
	world = LabWorld.new()
	add_child(world)
	if OS.has_feature("web"):
		js_callback = JavaScriptBridge.create_callback(_on_browser_message)
		# The custom shell owns DOM rendering; this callback is its sole route into the model.
		JavaScriptBridge.get_interface("window").__cancerLabGodotCallback = js_callback
		var browser_shell: Variant = JavaScriptBridge.get_interface("window").CancerLabWeb
		if browser_shell != null:
			browser_shell.ready()
		_request_saved("level1")
		_request_saved("level2")
	_render_menu()

func _on_browser_message(arguments: Array) -> void:
	if arguments.is_empty():
		return
	var message: Variant = JSON.parse_string(str(arguments[0]))
	if typeof(message) != TYPE_DICTIONARY:
		return
	match str(message.get("kind", "")):
		"start": _start(str(message.get("level", "")), str(message.get("mode", "")))
		"submit": _submit(str(message.get("action", "")), message.get("target"), message.get("value"))
		"select-well":
			selected_well = str(message.get("well", "B1"))
			_render_run()
		"menu": _render_menu()
		"repeat-cycle": _repeat_cycle(str(message.get("well", "")))
		"resume": _resume(str(message.get("level", "")), str(message.get("id", "")))
		"delete": _delete_attempt(str(message.get("level", "")), str(message.get("id", "")))
		"export": _export_report(str(message.get("format", "csv")))
		"linked-retry": _start_linked_retry()
		"storage-list": _receive_saved(message)
		"storage-status": _storage_status(message)
		"camera":
			if world.has_method("toggle_closeup"): world.toggle_closeup()

func _start(next_level: String, mode: String) -> void:
	if not ["level1", "level2"].has(next_level) or not ["GuidedPractice", "Assessment"].has(mode):
		return
	level = next_level
	last_feedback = "New %s attempt started." % ("Level 1" if level == "level1" else "Level 2")
	if level == "level1":
		level1 = Level1Simulation.new()
		level1.start(mode)
	else:
		level2 = Level2Simulation.new()
		level2.start(mode)
	_save_current()
	_render_run()

func _start_linked_retry() -> void:
	if level != "level2" or str(_snapshot().get("phase", "")) != "Complete":
		return
	var prior_attempt_id := str(_snapshot().get("attemptId", ""))
	var mode := str(_snapshot().get("mode", ""))
	var retry := Level2Simulation.new()
	if retry.start_linked_retry(prior_attempt_id, mode).is_empty():
		return
	level2 = retry
	last_feedback = "Started a linked Level 2 retry. The completed attempt remains saved separately."
	_save_current()
	_render_run()

func _submit(action: String, target: Variant, value: Variant) -> void:
	if level.is_empty():
		return
	if target == "": target = null
	if value == "": value = null
	if level == "level1":
		if action == "SetVolume" and value != null:
			value = str(value).to_float()
		if action == "MoveToDestination" and target == null:
			target = selected_well
		if action == "SelectSource" and target == null:
			target = selected_source
		var response := level1.submit(action, target, value)
		last_feedback = str(response.message)
		if _snapshot().mode == "GuidedPractice":
			last_feedback += "\n\n%s\n%s\n%s" % [response.get("scienceTitle", ""), response.get("scienceLesson", ""), response.get("whyItMatters", "")]
		elif not bool(response.get("accepted", false)):
			last_feedback += "\n\n" + _assessment_action_context(_snapshot())
		if target in ["Blank", "Vehicle control", "Fictional treatment"]:
			selected_source = str(target)
	else:
		var response := level2.submit(action, target, value)
		last_feedback = str(response.message)
		if _snapshot().mode == "GuidedPractice" and not str(response.get("coaching", "")).is_empty():
			last_feedback += "\n\n" + str(response.coaching)
		elif _snapshot().mode == "Assessment" and not bool(response.get("accepted", false)):
			last_feedback += "\n\n" + _assessment_action_context(_snapshot())
	_save_current() # Includes rejected actions because the model ledger records them.
	_render_run()

# Guided-only shortcut: replays the seven forward-cycle actions through the model in order,
# so the ledger, validity checks, and saves are identical to pressing each control.
func _repeat_cycle(well: String) -> void:
	if level != "level1" or str(_snapshot().get("mode", "")) != "GuidedPractice" or not ["B1", "B2", "B3", "B4", "B5", "B6"].has(well):
		return
	var source: Variant = _snapshot().get("selectedSource")
	var steps := [["PressFirstStop", null], ["MoveToSource", source], ["ReleaseSlow", null], ["MoveToDestination", well], ["PressFirstStop", null], ["PressSecondStop", null], ["WithdrawAndRelease", null]]
	var completed := 0
	for step in steps:
		var response := level1.submit(step[0], step[1], null)
		if not bool(response.get("accepted", false)):
			last_feedback = "Repeat cycle stopped after %s of 7 steps: %s" % [completed, str(response.message)]
			_save_current()
			_render_run()
			return
		completed += 1
	selected_well = well
	last_feedback = "Repeated the forward cycle into %s with %s: first stop in air, into the source, slow release, position at %s, first stop, second stop, withdraw. All 7 steps were recorded in order." % [well, source, well]
	_save_current()
	_render_run()

func _snapshot() -> Dictionary:
	return level1.snapshot() if level == "level1" else level2.snapshot()

func _model_save() -> String:
	return level1.save() if level == "level1" else level2.save()

func _save_current() -> void:
	if level.is_empty() or not OS.has_feature("web"):
		return
	var snapshot := _snapshot()
	var payload := {"level": level, "id": snapshot.attemptId, "save": _model_save(), "description": "%s · %s · %s" % [snapshot.mode, snapshot.phase, str(snapshot.attemptId).left(8)]}
	_js_call("saveAttempt", payload)

func _request_saved(which_level: String) -> void:
	if OS.has_feature("web"):
		_js_call("listAttempts", {"level": which_level})

func _receive_saved(message: Dictionary) -> void:
	var which_level := str(message.get("level", ""))
	if not saved_attempts.has(which_level) or typeof(message.get("entries")) != TYPE_ARRAY:
		return
	saved_attempts[which_level] = {}
	for entry in message.entries:
		if typeof(entry) == TYPE_DICTIONARY and not str(entry.get("id", "")).is_empty():
			saved_attempts[which_level][entry.id] = entry
	if level.is_empty():
		_render_menu()

func _storage_status(message: Dictionary) -> void:
	var which_level := str(message.get("level", ""))
	_request_saved(which_level)
	if str(message.get("status", "")) == "failed":
		last_feedback = "This browser could not commit the local save. The current in-memory attempt remains open."
	elif str(message.get("status", "")) == "committed" and not level.is_empty() and which_level == level:
		last_feedback = last_feedback + "\n\nSaved locally in this browser."
		_render_run()

func _resume(which_level: String, id: String) -> void:
	var entry: Variant = saved_attempts.get(which_level, {}).get(id)
	if typeof(entry) != TYPE_DICTIONARY:
		return
	level = which_level
	if level == "level1":
		level1 = Level1Simulation.new()
		if not level1.restore(str(entry.save)):
			last_feedback = "This local Level 1 save could not be restored; it remains preserved in this browser."
			_render_menu()
			return
	else:
		level2 = Level2Simulation.new()
		if not level2.restore(str(entry.save)):
			last_feedback = "This local Level 2 save could not be restored; it remains preserved in this browser."
			_render_menu()
			return
	last_feedback = "Resumed local %s attempt %s." % [level, id.left(8)]
	_render_run()

func _delete_attempt(which_level: String, id: String) -> void:
	if OS.has_feature("web"):
		_js_call("deleteAttempt", {"level": which_level, "id": id})
	saved_attempts.get(which_level, {}).erase(id)
	_render_menu()

func _render_menu() -> void:
	level = ""
	last_feedback = "Choose Level 1 or Level 2. Saved attempts stay separate on this browser only."
	_render({"screen":"menu", "version":WEB_VERSION, "saved":saved_attempts, "feedback":last_feedback})
	if world.has_method("show_level"): world.show_level("")

func _render_run() -> void:
	if level.is_empty():
		_render_menu()
		return
	var state := _snapshot()
	var complete := str(state.phase) == "Complete"
	var report: Dictionary = {}
	if complete:
		report = level1.build_report() if level == "level1" else level2.build_report()
	var quality_card: Variant = null
	if level == "level1":
		quality_card = PipettingQualityTeaching.debrief_card(level1.sources.entries) if complete else PipettingQualityTeaching.optional_why(str(state.mode), level1.sources.entries)
	var payload := {
		"screen":"run", "version":WEB_VERSION, "level":level, "mode":state.mode, "phase":state.phase,
		"attemptId":str(state.attemptId).left(8), "feedback":last_feedback, "selectedWell":selected_well,
		"selectedSource":selected_source, "actions":_actions(), "complete":complete, "report":report, "qualityCard":quality_card,
		"status":_status_text(state), "wells":state.get("wells", state.get("plates", [])), "state":_display_state(state)
	}
	_render(payload)
	if world.has_method("refresh"): world.refresh(level, state, selected_well)

func _display_state(state: Dictionary) -> Dictionary:
	if level == "level1":
		return {"pipetteStage":state.get("pipetteStage"), "tipAttached":state.get("tipAttached"), "tipId":state.get("tipId"), "selectedVolumeUl":state.get("selectedVolumeUl"), "targetVolumeUl":level1.scenario.get("toyTransferVolumeUl"), "sourceRemainingVolumeUl":state.get("sourceRemainingVolumeUl"), "selectedSource":state.get("selectedSource"), "boundDestination":state.get("boundDestination"), "readerRan":state.get("readerRan"), "plateLoaded":state.get("plateLoaded"), "correctOrientation":state.get("correctOrientation"), "readerConfigured":state.get("readerConfigured"), "resultsReviewed":state.get("resultsReviewed"), "attempt":state.get("attemptNumber"), "ppeWorn":state.get("ppeWorn"), "benchDisinfected":state.get("benchDisinfected"), "materialsChecked":state.get("materialsChecked"), "labelsVerified":state.get("labelsVerified"), "plateMapReviewed":state.get("plateMapReviewed"), "tipContaminationRole":state.get("tipContaminationRole"), "controlsValid":state.get("controlsValid"), "checks":level1.rules.get("comprehensionChecks", []), "checkAnswers":state.get("checkAnswers", [])}
	var case_choices: Array = []
	for definition in level2.cases.get("cases", []):
		case_choices.append({"id":definition.get("id", ""), "title":definition.get("title", "Lab Detective case")})
	return {"tubeLabels":state.get("tubeLabels"), "tubeContents":state.get("tubeContents"), "plates":state.get("plates"), "casePlates":state.get("casePlates"), "diagnosticSetup":state.get("diagnosticSetup"), "caseChoices":case_choices, "predictions":state.get("predictions"), "predictionsLocked":state.get("predictionsLocked"), "evidenceViewed":state.get("evidenceCardsViewed"), "attempt":state.get("attemptNumber", 1), "explanationChoices":state.get("explanationChoices", []), "diagnosticDecisions":state.get("diagnosticDecisions", []), "cleanupRecorded":state.get("cleanupRecorded", false), "wasteDecisionRecorded":state.get("wasteDecisionRecorded", false), "debriefDocumented":state.get("debriefDocumented", false), "freshTipReady":state.get("freshTipReady", false), "selectedTubeSource":state.get("selectedTubeSource"), "selectedTubeDestination":state.get("selectedTubeDestination")}

func _status_text(state: Dictionary) -> String:
	if level == "level1":
		return "Pipette: %s · selected source: %s · bound destination: %s" % [_stage_label(str(state.get("pipetteStage", "ReadyInAir"))), _or_none(state.get("selectedSource")), _or_none(state.get("boundDestination"))]
	return "Tube setup and plate predictions are separate conceptual records. Outcomes unlock only after all eight prediction dimensions are locked."

func _or_none(value: Variant) -> String:
	return "none" if value == null or str(value).is_empty() else str(value)

func _stage_label(stage: String) -> String:
	return {"ReadyInAir":"ready in air", "FirstStopInAir":"first stop in air", "FirstStopInSource":"at selected source", "LoadedAtSource":"loaded at source", "LoadedAtDestination":"positioned at bound destination", "FirstStopAtDestination":"nominal delivery recorded", "SecondStopAtDestination":"second stop recorded"}.get(stage, stage)

func _assessment_action_context(state: Dictionary) -> String:
	if level == "level1":
		if str(state.get("phase", "")) == "Transfers":
			return "No record changed. Review the displayed pipette stage, tip state, nominal setting, selected source, and bound destination; then use the next forward task control."
		if not bool(state.get("readerRan", false)):
			return "No record changed. Review the displayed preparation, orientation, reader, and observation status before choosing the next task control."
		return "No record changed. Review the displayed phase and available task controls before continuing."
	return "No record changed. Review the displayed phase, tube or plate record, prediction/view status, and available task controls before continuing."

func _actions() -> Array:
	if level == "level1":
		return ["WearPpe","DisinfectBench","CheckMaterials","VerifyLabels","ReviewPlateMap","AttachTip","EjectTip","SetVolume","SelectSource","PressFirstStop","MoveToSource","ReleaseSlow","ReleaseFast","MoveToDestination","PressSecondStop","WithdrawAndRelease","MislabelSelectedWell","CorrectLabel","RetryTransferCheckpoint","LoadPlate","ConfigureReader","RunReader","ReviewResults","DecideSupportedConclusion","EscalateInvalidRun","SortWaste","CleanBench","RecordHandoff"]
	return ["AcknowledgeIntro","LabelTube","UseFreshTip","SelectTubeSource","SelectTubeDestination","LoadTube","CorrectTubeContent","RecoverTubePreparation","AssignPlate","CorrectPlateAssignment","CommitPrediction","AdvanceConceptualTime","ViewPlate","RecordExplanation","StartDiagnosticCase","ViewCasePlate","ViewEvidenceCard","RecordDiagnosticDecision","RequestHint","RevisePrediction","RecordCleanup","RecordWasteDecision","RecordDebrief","CompleteDebrief"]

func _export_report(format: String) -> void:
	if level.is_empty() or str(_snapshot().phase) != "Complete":
		return
	var report: Dictionary = level1.build_report() if level == "level1" else level2.build_report()
	var body := _json_export(report) if format == "json" else (_csv(report) if format == "csv" else _html(report))
	var content_type := "application/json;charset=utf-8" if format == "json" else "text/%s;charset=utf-8" % ("csv" if format == "csv" else "html")
	_js_call("download", {"name":"cancer-lab-%s-%s.%s" % [level, str(_snapshot().attemptId).left(8), format], "type":content_type, "body":body})

func _json_export(report: Dictionary) -> String:
	return JSON.stringify({"exportEnvelope":"webExportEnvelope1", "webProvenance":{"webAppVersion":WEB_VERSION, "webEnvelope":"webEnvelope1", "level":level, "modelVersion":report.get("modelVersion", ""), "saveFormatVersion":_report_save_format(report)}, "report":report}, "", true, true)

func _csv(report: Dictionary) -> String:
	var metadata := ["web_app=" + WEB_VERSION, "web_envelope=webEnvelope1", "level=" + level, "model=" + str(report.get("modelVersion", "")), "save_format=" + str(_save_format_version()), "mode=" + str(report.get("mode", "")), "attempt=" + str(report.get("attemptId", "")), "scenario_seed=" + (str(int(report.scenarioSeed)) if typeof(report.get("scenarioSeed")) in [TYPE_INT, TYPE_FLOAT] else str(report.get("scenarioSeed", ""))), "retry_parent=" + str(report.get("retryOfAttemptId", ""))]
	if level == "level2":
		var level2_lines := ["# " + ",".join(metadata), "# " + FICTIONAL_NOTICE, "section,plate,label,tube,medium,growth,fluorescence,original_growth_prediction,original_fluorescence_prediction,revisions"]
		var predictions: Dictionary = {}
		for prediction in report.get("predictions", []): predictions[prediction.get("plateId", "")] = prediction
		for plate in report.get("plates", []):
			var prediction: Dictionary = predictions.get(plate.get("id", ""), {})
			level2_lines.append(_csv_row(["main-plate", plate.get("id", ""), plate.get("label", ""), plate.get("tubeLabel", ""), plate.get("medium", ""), plate.get("growth", ""), plate.get("fluorescence", ""), prediction.get("originalGrowthPrediction", ""), prediction.get("originalFluorescencePrediction", ""), ";".join(prediction.get("revisions", []))]))
		for plate in report.get("caseObservations", []):
			level2_lines.append(_csv_row(["diagnostic-plate", plate.get("id", ""), plate.get("label", ""), plate.get("tubeLabel", ""), plate.get("medium", ""), plate.get("growth", ""), plate.get("fluorescence", ""), "", "", ""]))
		level2_lines.append("section,field,value")
		for tube in report.get("tubeLabels", {}): level2_lines.append(_csv_row(["tube-label", tube, report.tubeLabels[tube]]))
		for tube in report.get("tubeContents", {}): level2_lines.append(_csv_row(["tube-content", tube, report.tubeContents[tube]]))
		for key in report.get("caseSetup", {}): level2_lines.append(_csv_row(["case-setup", key, JSON.stringify(report.caseSetup[key], "", false, true)]))
		level2_lines.append(_csv_row(["main-claim", "selection-supported", report.get("mainSelectionClaimSupported", false)]))
		level2_lines.append(_csv_row(["main-claim", "expression-supported", report.get("mainExpressionClaimSupported", false)]))
		for correction in report.get("tubeSetupCorrections", []): level2_lines.append(_csv_row(["tube-correction", "record", correction]))
		for correction in report.get("plateAssignmentCorrections", []): level2_lines.append(_csv_row(["plate-correction", "record", correction]))
		for correction in report.get("prelockPredictionCorrections", []): level2_lines.append(_csv_row(["prediction-correction", "record", correction]))
		for decision in report.get("diagnosticDecisions", []): level2_lines.append(_csv_row(["diagnostic-decision", "record", decision]))
		for hint in report.get("hintsUsed", []): level2_lines.append(_csv_row(["hint", "record", hint]))
		for explanation in report.get("explanationChoices", []): level2_lines.append(_csv_row(["explanation", "record", explanation]))
		for record in report.get("ledger", []): level2_lines.append(_csv_row(["ledger", str(record.get("type", "")), JSON.stringify(record, "", false, true)]))
		for evidence in report.get("evidenceCardsViewed", []): level2_lines.append(_csv_row(["evidence-card", "viewed", evidence]))
		for key in report.get("rubricStatus", {}): level2_lines.append(_csv_row(["claim-status", key, report.rubricStatus[key]]))
		for key in report.get("claimReasons", {}): level2_lines.append(_csv_row(["claim-reason", key, report.claimReasons[key]]))
		level2_lines.append(_csv_row(["scientific-limit", "limits", report.get("limits", "")]))
		for source in report.get("sources", []): level2_lines.append(_csv_row(["source", source.get("id", ""), source.get("label", source.get("title", "")), source.get("url", "")]))
		return "\n".join(level2_lines)
	var lines := ["# " + ",".join(metadata), "# " + FICTIONAL_NOTICE, "section,well,role,transfer_volume_ul,fictional_raw_signal,fictional_background_adjusted,fictional_percent_of_vehicle"]
	for well in report.get("wells", []):
		lines.append(_csv_row(["well", well.get("well", ""), well.get("role", ""), well.get("transferVolumeUl", ""), well.get("rawReading", ""), well.get("backgroundAdjusted", ""), _relative_text(well)]))
	lines.append("section,field,value")
	for replicate in report.get("replicates", []): lines.append(_csv_row(["replicate", replicate.get("role", ""), "n=%s;mean=%s;cv_percent=%s" % [replicate.get("count", ""), replicate.get("mean", ""), replicate.get("cvPercent", "")]]))
	for issue in report.get("issues", []): lines.append(_csv_row(["issue", issue.get("code", ""), JSON.stringify(issue, "", false, true)]))
	for key in report.get("categoryStatus", {}): lines.append(_csv_row(["category", key, report.categoryStatus[key]]))
	for row in _check_rows(report): lines.append(_csv_row(["check-answer", row.id, "%s | correct=%s" % [row.answer, row.correct]]))
	lines.append(_csv_row(["conclusion", "interpretation", report.get("conclusion", "")]))
	lines.append(_csv_row(["scientific-limit", "limits", report.get("scientificLimit", "")]))
	for source in report.get("sources", []): lines.append(_csv_row(["source", source.get("id", ""), source.get("label", source.get("title", "")), source.get("url", "")]))
	return "\n".join(lines)

const FICTIONAL_NOTICE := "FICTIONAL TRAINING DATA: illustrative values from a teaching model, not laboratory results."
const NOT_CALCULATED := "not calculated: invalid blank/vehicle reference"
const OBSERVATION_LABELS := {"NotViewed":"Not viewed", "NoneDetected":"Not detected", "NoColoniesToAssess":"No colonies to assess", "NotAssessable":"Not assessable", "NotSure":"Not sure"}

func _obs(value: Variant) -> String:
	return str(OBSERVATION_LABELS.get(str(value), value))

func _relative_text(well: Dictionary) -> String:
	if well.get("relativeToVehicle") != null: return str(well.relativeToVehicle)
	return "" if well.get("rawReading") == null else NOT_CALCULATED

func _check_rows(report: Dictionary) -> Array:
	# Pairs each recorded answer with its question so exports read without the app.
	var rows: Array = []
	var checks: Dictionary = {}
	var rules: Dictionary = level1.rules if level1 != null else {}
	for check in rules.get("comprehensionChecks", []): checks[check.id] = check
	for answer in report.get("checkAnswers", []):
		var check: Dictionary = checks.get(answer.get("questionId", ""), {})
		var chosen := str(answer.get("optionId", ""))
		for option in check.get("options", []):
			if option.id == chosen: chosen = str(option.text)
		rows.append({"id":answer.get("questionId", ""), "prompt":check.get("prompt", answer.get("questionId", "")), "answer":chosen, "correct":bool(answer.get("correct", false))})
	return rows

func _save_format_version() -> int:
	return 3 if level == "level1" else 1

func _report_save_format(report: Dictionary) -> int:
	return int(report.get("saveFormatVersion", _save_format_version()))

func _csv_row(values: Array) -> String:
	var cells: Array[String] = []
	for value in values:
		cells.append(_csv_cell(value))
	return ",".join(cells)

func _csv_cell(value: Variant) -> String:
	var text := "" if value == null else str(value)
	if typeof(value) == TYPE_STRING and (text.lstrip(" \t").begins_with("=") or text.lstrip(" \t").begins_with("+") or text.lstrip(" \t").begins_with("-") or text.lstrip(" \t").begins_with("@")):
		text = "'" + text
	return "\"%s\"" % text.replace("\"", "\"\"")

func _html(report: Dictionary) -> String:
	var title := "Cancer Lab Trainer browser debrief"
	if level == "level2":
		return _level2_html(report, title)
	var heading := "Fictional readings (training data, not laboratory results)" if level == "level1" else "Conceptual plate observations"
	var rows: Array = []
	if level == "level1":
		for well in report.get("wells", []): rows.append("<tr><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td><td>%s</td></tr>" % [str(well.get("well", "")).xml_escape(), str(well.get("role", "")).xml_escape(), str(well.get("transferVolumeUl", "")).xml_escape(), str(well.get("rawReading", "")).xml_escape(), str(well.get("backgroundAdjusted", "")).xml_escape(), _relative_text(well).xml_escape()])
	else:
		for plate in report.get("plates", []): rows.append("<tr><td>%s</td><td>%s</td><td>%s / %s</td><td>%s</td><td>%s</td></tr>" % [str(plate.get("id", "")).xml_escape(), str(plate.get("label", "")).xml_escape(), str(plate.get("tubeLabel", "")).xml_escape(), str(plate.get("medium", "")).xml_escape(), str(plate.get("growth", "")).xml_escape(), str(plate.get("fluorescence", "")).xml_escape()])
	var categories := ""
	for key in report.get("categoryStatus", report.get("rubricStatus", {})): categories += "<li>%s: %s</li>" % [str(key).xml_escape(), str(report.get("categoryStatus", report.get("rubricStatus", {}))[key]).xml_escape()]
	var lessons := ""
	for lesson in report.get("lessons", []): lessons += "<h3>%s</h3><p>%s</p><p>%s</p>" % [str(lesson.get("title", "")).xml_escape(), str(lesson.get("explanation", "")).xml_escape(), str(lesson.get("whyItMatters", "")).xml_escape()]
	var sources := ""
	for source in report.get("sources", []):
		var url := str(source.get("url", ""))
		sources += "<li>%s%s</li>" % [str(source.get("label", source.get("title", source.get("id", "Source")))).xml_escape(), (" — <a href=\"%s\">source</a>" % url.xml_escape(true)) if url.begins_with("https://") else ""]
	var evidence := ""
	for item in report.get("evidenceCardsViewed", report.get("evidenceViewed", [])): evidence += "<li>%s</li>" % str(item).xml_escape()
	var decisions := ""
	for item in report.get("diagnosticDecisions", []): decisions += "<li>%s</li>" % str(item).xml_escape()
	var corrections := ""
	for item in report.get("tubeSetupCorrections", []) + report.get("plateAssignmentCorrections", []) + report.get("prelockPredictionCorrections", []): corrections += "<li>%s</li>" % str(item).xml_escape()
	var replicate_text := ""
	for replicate in report.get("replicates", []): replicate_text += "<li>%s: n=%s; mean=%s; CV=%s%%</li>" % [str(replicate.get("role", "")).xml_escape(), str(replicate.get("count", "")).xml_escape(), str(replicate.get("mean", "")).xml_escape(), str(replicate.get("cvPercent", "")).xml_escape()]
	var checks_html := ""
	for row in _check_rows(report): checks_html += "<li><strong>%s</strong> %s (%s)</li>" % [str(row.prompt).xml_escape(), str(row.answer).xml_escape(), "correct" if row.correct else "not the best answer"]
	if not checks_html.is_empty(): checks_html = "<h2>Check questions</h2><ul>%s</ul>" % checks_html
	var quality: Variant = ""
	if level == "level1": quality = PipettingQualityTeaching.html_card(level1.sources.entries)
	var table_header := "<th>Well</th><th>Role</th><th>Transfer</th><th>Raw (fictional)</th><th>Background-adjusted (fictional)</th><th>% of vehicle (fictional)</th>" if level == "level1" else "<th>Plate</th><th>Label</th><th>Tube / medium</th><th>Growth</th><th>Fluorescence</th>"
	return "<!doctype html><meta charset=\"utf-8\"><title>%s</title><style>body{font-family:system-ui;max-width:900px;margin:2rem auto;padding:0 1rem}table{border-collapse:collapse;width:100%%}th,td{border:1px solid #789;padding:.4rem;text-align:left}</style><h1>%s</h1><p>Web app %s · web envelope webEnvelope1 · level %s · model %s · save format %s · mode %s · attempt %s · seed %s</p><p><strong>%s</strong></p><h2>Outcome and limits</h2><p>%s</p><p>%s</p><h2>Assessment record</h2><ul>%s</ul><h2>%s</h2><p>ATP-associated light is an indirect, viability-related signal. A lower value can reflect fewer cells, less ATP per cell, or assay interference; it is not proof that cells died.</p><table><tr>%s</tr>%s</table><h2>Replicate variation</h2><ul>%s</ul><p>Paired wells are technical replicates from one preparation; only independent biological preparations could show that a difference holds up.</p>%s<h2>Evidence and decisions</h2><ul>%s%s</ul><h2>Corrections and retained history</h2><ul>%s</ul><h2>Science review</h2>%s%s<h2>Sources</h2><ul>%s</ul>" % [title, title, WEB_VERSION, level, str(report.get("modelVersion", "")).xml_escape(), str(_report_save_format(report)).xml_escape(), str(report.get("mode", "")).xml_escape(), str(report.get("attemptId", "")).xml_escape(), str(report.get("scenarioSeed", "")).xml_escape(), FICTIONAL_NOTICE, str(report.get("conclusion", "Conceptual debrief complete.")).xml_escape(), str(report.get("scientificLimit", report.get("limits", "Illustrative training only."))).xml_escape(), categories, heading, table_header, "".join(rows), replicate_text, checks_html, evidence, decisions, corrections, lessons, quality, sources]

func _level2_html(report: Dictionary, title: String) -> String:
	var rows := ""
	var prediction_by_plate: Dictionary = {}
	for prediction in report.get("predictions", []): prediction_by_plate[prediction.get("plateId", "")] = prediction
	for plate in report.get("plates", []):
		var prediction: Dictionary = prediction_by_plate.get(plate.get("id", ""), {})
		rows += "<tr><td>%s</td><td>%s</td><td>%s / %s</td><td>%s</td><td>%s</td><td>%s / %s</td></tr>" % [str(plate.get("id", "")).xml_escape(), str(plate.get("label", "")).xml_escape(), str(plate.get("tubeLabel", "")).xml_escape(), str(plate.get("medium", "")).xml_escape(), _obs(prediction.get("originalGrowthPrediction", "")).xml_escape(), _obs(prediction.get("originalFluorescencePrediction", "")).xml_escape(), _obs(plate.get("growth", "")).xml_escape(), _obs(plate.get("fluorescence", "")).xml_escape()]
	var claims := "<li>Main selection: %s — %s</li><li>Main expression: %s — %s</li>" % [str(report.get("mainSelectionClaimSupported", false)).xml_escape(), str(report.get("claimReasons", {}).get("main-selection", "")).xml_escape(), str(report.get("mainExpressionClaimSupported", false)).xml_escape(), str(report.get("claimReasons", {}).get("main-expression", "")).xml_escape()]
	var case_text := ""
	var diagnostic: Dictionary = report.get("diagnosticCase", {})
	if not diagnostic.is_empty(): case_text = "<h2>%s</h2><p>%s</p><p>Selection: %s — %s</p><p>Expression: %s — %s</p>" % [str(diagnostic.get("title", "Lab Detective debrief")).xml_escape(), str(diagnostic.get("finalFictionalCause", "")).xml_escape(), str(diagnostic.get("selectionClaimSupported", false)).xml_escape(), str(diagnostic.get("selectionClaimReason", "")).xml_escape(), str(diagnostic.get("expressionClaimSupported", false)).xml_escape(), str(diagnostic.get("expressionClaimReason", "")).xml_escape()]
	var case_rows := ""
	for plate in report.get("caseObservations", []): case_rows += "<tr><td>%s</td><td>%s</td><td>%s / %s</td></tr>" % [str(plate.get("id", "")).xml_escape(), str(plate.get("label", "")).xml_escape(), _obs(plate.get("growth", "")).xml_escape(), _obs(plate.get("fluorescence", "")).xml_escape()]
	if not case_rows.is_empty(): case_text += "<h2>Case observations</h2><table><tr><th>Plate</th><th>Label</th><th>Observed state</th></tr>%s</table>" % case_rows
	var setup_text := ""
	for key in report.get("caseSetup", {}): setup_text += "<li>%s: %s</li>" % [str(key).xml_escape(), JSON.stringify(report.caseSetup[key], "", false, true).xml_escape()]
	if not setup_text.is_empty(): case_text += "<h2>Viewed case setup</h2><ul>%s</ul>" % setup_text
	var history := ""
	for prediction in report.get("predictions", []): history += "<li>%s revisions: %s</li>" % [str(prediction.get("plateId", "")).xml_escape(), "; ".join(prediction.get("revisions", [])).xml_escape()]
	for value in report.get("tubeSetupCorrections", []) + report.get("plateAssignmentCorrections", []) + report.get("prelockPredictionCorrections", []) + report.get("evidenceCardsViewed", []) + report.get("diagnosticDecisions", []) + report.get("hintsUsed", []) + report.get("explanationChoices", []): history += "<li>%s</li>" % str(value).xml_escape()
	if report.get("retryOfAttemptId") != null: history += "<li>Linked retry parent: %s</li>" % str(report.retryOfAttemptId).xml_escape()
	else: history += "<li>Linked retry parent: none</li>"
	for record in report.get("ledger", []): history += "<li>Action ledger: %s</li>" % JSON.stringify(record, "", false, true).xml_escape()
	var tube_records := ""
	for tube in report.get("tubeLabels", {}): tube_records += "<li>%s: label %s; actual content %s</li>" % [str(tube).xml_escape(), str(report.tubeLabels[tube]).xml_escape(), str(report.get("tubeContents", {}).get(tube, "not recorded")).xml_escape()]
	if tube_records.is_empty(): tube_records = "<li>No main tube record was available.</li>"
	var assistance := ""
	var hints: Array = report.get("hintsUsed", [])
	var explanations: Array = report.get("explanationChoices", [])
	assistance += "<p>Hints: %s</p>" % ("none" if hints.is_empty() else "; ".join(hints).xml_escape())
	assistance += "<p>Recorded explanations: %s</p>" % ("none" if explanations.is_empty() else "; ".join(explanations).xml_escape())
	var rubric := ""
	for key in report.get("rubricStatus", {}): rubric += "<li>%s: %s</li>" % [str(key).xml_escape(), str(report.rubricStatus[key]).xml_escape()]
	if rubric.is_empty(): rubric = "<li>No rubric record was available.</li>"
	var sources := ""
	for source in report.get("sources", []):
		var url := str(source.get("url", ""))
		sources += "<li>%s%s</li>" % [str(source.get("label", source.get("id", "Source"))).xml_escape(), (" — <a href=\"%s\">source</a>" % url.xml_escape(true)) if url.begins_with("https://") else ""]
	return "<!doctype html><meta charset=\"utf-8\"><title>%s</title><style>body{font-family:system-ui;max-width:1000px;margin:2rem auto;padding:0 1rem}table{border-collapse:collapse;width:100%%}th,td{border:1px solid #789;padding:.4rem;text-align:left}</style><h1>%s</h1><p>Web app %s · web envelope webEnvelope1 · model %s · save format %s · mode %s · attempt %s · seed %s</p><p><strong>%s</strong></p><h2>Main tube records</h2><ul>%s</ul><h2>Main four-plate record</h2><table><tr><th>Plate</th><th>Planned label</th><th>Actual assignment</th><th>Growth prediction</th><th>Fluorescence prediction</th><th>Observed state</th></tr>%s</table><h2>Claim support and limits</h2><ul>%s</ul><h2>Rubric record</h2><ul>%s</ul>%s<h2>Assistance and explanations</h2>%s<h2>Retained revisions, corrections, evidence, decisions, and ledger</h2><ul>%s</ul><h2>Scientific limit</h2><p>%s</p><h2>Sources</h2><ul>%s</ul>" % [title, title, WEB_VERSION, str(report.get("modelVersion", "")).xml_escape(), str(_report_save_format(report)).xml_escape(), str(report.get("mode", "")).xml_escape(), str(report.get("attemptId", "")).xml_escape(), str(report.get("scenarioSeed", "")).xml_escape(), FICTIONAL_NOTICE, tube_records, rows, claims, rubric, case_text, assistance, history, str(report.get("limits", "Illustrative training only.")).xml_escape(), sources]

func _render(payload: Dictionary) -> void:
	if OS.has_feature("web"):
		_js_call("render", payload)

func _js_call(method: String, payload: Dictionary) -> void:
	var encoded := JSON.stringify(payload, "", false, true)
	# Pass the serialized payload as an argument instead of interpolating it into JavaScript.
	# Report HTML and CSV can contain quotes and line breaks, which made eval-generated calls invalid.
	var browser_shell: Variant = JavaScriptBridge.get_interface("window").CancerLabWeb
	if browser_shell == null:
		return
	match method:
		"render": browser_shell.render(encoded)
		"saveAttempt": browser_shell.saveAttempt(encoded)
		"listAttempts": browser_shell.listAttempts(encoded)
		"deleteAttempt": browser_shell.deleteAttempt(encoded)
		"download": browser_shell.download(encoded)
