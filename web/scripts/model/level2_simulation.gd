class_name Level2Simulation
extends RefCounted

# Browser-native port of src/core/TransformationSimulation.cs.
const MODEL_VERSION := "1.0.0"
const SAVE_FORMAT := 1
const WEB_ENVELOPE := "webEnvelope1"
const LEVEL_ID := "level2"
const CASE_IDS := ["missing-arabinose", "swapped-tube-labels", "missing-dna", "failed-antibiotic-negative-control"]
const EXPECTED_ASSIGNMENTS := {"P1":"T|LB+amp", "P2":"NC|LB+amp", "P3":"T|LB+amp+Ara", "P4":"NC|LB"}
const CASE_SIGNATURES := {
	"missing-arabinose":"P1:Present:NoneDetected|P2:Absent:NotAssessable|P3:Present:NoneDetected|P4:Present:NoneDetected",
	"swapped-tube-labels":"P1:Absent:NotAssessable|P2:Present:NoneDetected|P3:Absent:NotAssessable|P4:Present:NoneDetected",
	"missing-dna":"P1:Absent:NotAssessable|P2:Absent:NotAssessable|P3:Absent:NotAssessable|P4:Present:NoneDetected",
	"failed-antibiotic-negative-control":"P1:Present:NoneDetected|P2:Present:NoneDetected|P3:Present:Detected|P4:Present:NoneDetected"
}
const CASE_SETUP_SIGNATURES := {
	"missing-arabinose":"tubes:NC=Buffer|T=DNA;actual:P1=T|LB+amp|P2=NC|LB+amp|P3=T|LB+amp|P4=NC|LB;intended:P1=T|LB+amp|P2=NC|LB+amp|P3=T|LB+amp+Ara|P4=NC|LB",
	"swapped-tube-labels":"tubes:NC=DNA|T=Buffer;actual:P1=T|LB+amp|P2=NC|LB+amp|P3=T|LB+amp+Ara|P4=NC|LB;intended:P1=T|LB+amp|P2=NC|LB+amp|P3=T|LB+amp+Ara|P4=NC|LB",
	"missing-dna":"tubes:NC=Buffer|T=Buffer;actual:P1=T|LB+amp|P2=NC|LB+amp|P3=T|LB+amp+Ara|P4=NC|LB;intended:P1=T|LB+amp|P2=NC|LB+amp|P3=T|LB+amp+Ara|P4=NC|LB",
	"failed-antibiotic-negative-control":"tubes:NC=Buffer|T=DNA;actual:P1=T|LB+amp|P2=NC|LB|P3=T|LB+amp+Ara|P4=NC|LB;intended:P1=T|LB+amp|P2=NC|LB+amp|P3=T|LB+amp+Ara|P4=NC|LB"
}
var scenario: Dictionary = {}
var lessons: Dictionary = {}
var cases: Dictionary = {}
var rubric: Dictionary = {}
var sources: Dictionary = {}
var state: Dictionary = {}

func _init() -> void:
	_load_canonical_definitions()

func _load_canonical_definitions() -> void:
	scenario = _read_json("res://data/transformation-scenario.v1.json")
	lessons = _read_json("res://data/transformation-lessons.v1.json")
	cases = _read_json("res://data/transformation-cases.v1.json")
	rubric = _read_json("res://data/transformation-rubric.v1.json")
	sources = _read_json("res://data/transformation-sources.v1.json")
	assert(_definitions_valid(scenario, lessons, cases, rubric, sources), "Level 2 definitions are invalid or unsupported.")

func start(mode: String) -> Dictionary:
	if not ["GuidedPractice", "Assessment"].has(mode):
		return {}
	state = {
		"attemptId": _id(), "mode": mode, "phase": "Intro", "started": true,
		"scenarioSeed": scenario.seed, "tubeContents": {}, "tubeLabels": {},
		"freshTipReady": false, "selectedTubeSource": null, "selectedTubeDestination": null,
		"tubeSetupCorrections": [], "plateAssignmentCorrections": [], "prelockPredictionCorrections": [],
		"plates": _fresh_plates(), "casePlates": [], "predictions": [], "predictionsLocked": false,
		"hintsUsed": [], "explanationChoices": [], "explanationChoiceHistory": [],
		"diagnosticSetup": null, "diagnosticCaseId": null, "diagnosticDecisions": [],
		"diagnosticDecisionHistory": [], "evidenceViewed": [], "evidenceCardsViewed": [], "ledger": [],
		"cleanupRecorded": false, "wasteDecisionRecorded": false, "debriefDocumented": false,
		"retryOfAttemptId": null
	}
	return snapshot()

func start_linked_retry(prior_attempt_id: String, mode: String) -> Dictionary:
	if not _valid_id(prior_attempt_id):
		return {}
	var result := start(mode)
	if result.is_empty():
		return result
	state.retryOfAttemptId = prior_attempt_id
	return snapshot()

func submit(action_type: String, target: Variant = null, value: Variant = null) -> Dictionary:
	if not state.get("started", false):
		return _result(false, "Start an attempt first.", "")
	if not _action_names().has(action_type):
		return _result(false, "A recognized action is required.", "")
	if state.phase == "Complete":
		return _record(false, "This completed attempt is locked. Start a new linked attempt for another run.", "", action_type, target, value)
	var answer: Array = []
	match action_type:
		"AcknowledgeIntro": answer = _acknowledge_intro()
		"LabelTube": answer = _label_tube(target, value)
		"UseFreshTip": answer = _use_fresh_tip()
		"SelectTubeSource": answer = _select_tube_source(target)
		"SelectTubeDestination": answer = _select_tube_destination(target)
		"LoadTube": answer = _load_tube()
		"CorrectTubeContent": answer = _correct_tube_content(target, value)
		"RecoverTubePreparation": answer = _recover_tube_preparation()
		"AssignPlate": answer = _assign_plate(target, value)
		"CorrectPlateAssignment": answer = _correct_plate_assignment(target, value)
		"CommitPrediction": answer = _commit_prediction(target, value)
		"AdvanceConceptualTime": answer = _advance_time()
		"ViewPlate": answer = _view_plate(target, value, false)
		"RecordExplanation": answer = _explain(target, value)
		"StartDiagnosticCase": answer = _start_case(target)
		"ViewCasePlate": answer = _view_plate(target, value, true)
		"ViewEvidenceCard": answer = _view_evidence_card(target)
		"RecordDiagnosticDecision": answer = _record_decision(target, value)
		"RequestHint": answer = _hint(target)
		"RevisePrediction": answer = _revise_prediction(target, value)
		"RecordCleanup": answer = _record_closeout("cleanup")
		"RecordWasteDecision": answer = _record_closeout("waste")
		"RecordDebrief": answer = _record_closeout("document")
		"CompleteDebrief": answer = _complete_debrief()
		_: answer = [false, "This action is not available in this version.", ""]
	return _record(answer[0], answer[1], answer[2], action_type, target, value)

func snapshot() -> Dictionary:
	var copy: Dictionary = state.duplicate(true)
	if copy.get("diagnosticCaseId") != null and not ["Complete", "Debrief"].has(copy.phase):
		copy.diagnosticCaseId = "case-in-progress"
		if not copy.evidenceCardsViewed.has("setup-record"):
			copy.diagnosticSetup = null
		for record in copy.ledger:
			if record.type == "StartDiagnosticCase":
				record.target = "case-in-progress"
	return copy

func save() -> String:
	# Full precision is required because restore validates an exact replayed state.
	return JSON.stringify({"webEnvelope":WEB_ENVELOPE, "levelId":LEVEL_ID, "formatVersion":SAVE_FORMAT, "modelVersion":MODEL_VERSION, "scenario":scenario, "lessons":lessons, "cases":cases, "rubric":rubric, "sources":sources, "snapshot":state}, "", true, true)

func restore(text: String) -> bool:
	var parsed: Variant = JSON.parse_string(text)
	if typeof(parsed) != TYPE_DICTIONARY:
		return false
	if parsed.get("webEnvelope") != WEB_ENVELOPE or parsed.get("levelId") != LEVEL_ID or parsed.get("formatVersion") != SAVE_FORMAT or parsed.get("modelVersion") != MODEL_VERSION:
		return false
	if typeof(parsed.get("snapshot")) != TYPE_DICTIONARY:
		return false
	var saved: Dictionary = parsed
	if not _definitions_valid(saved.get("scenario", {}), saved.get("lessons", {}), saved.get("cases", {}), saved.get("rubric", {}), saved.get("sources", {})):
		return false
	var saved_state: Dictionary = saved.snapshot
	if not _valid_id(str(saved_state.get("attemptId", ""))) or not ["GuidedPractice", "Assessment"].has(saved_state.get("mode")):
		return false
	if saved_state.get("retryOfAttemptId") != null and not _valid_id(str(saved_state.retryOfAttemptId)):
		return false
	var candidate := Level2Simulation.new()
	candidate.scenario = saved.scenario.duplicate(true)
	candidate.lessons = saved.lessons.duplicate(true)
	candidate.cases = saved.cases.duplicate(true)
	candidate.rubric = saved.rubric.duplicate(true)
	candidate.sources = saved.sources.duplicate(true)
	candidate.start(saved_state.mode)
	candidate.state.attemptId = saved_state.attemptId
	for record in saved_state.get("ledger", []):
		if typeof(record) != TYPE_DICTIONARY or not candidate._action_names().has(record.get("type")):
			return false
		candidate.submit(record.type, record.get("target"), record.get("value"))
	candidate.state.retryOfAttemptId = saved_state.get("retryOfAttemptId")
	if not _same_tree(candidate.state, saved_state):
		return false
	scenario = candidate.scenario
	lessons = candidate.lessons
	cases = candidate.cases
	rubric = candidate.rubric
	sources = candidate.sources
	state = candidate.state
	return true

func build_report() -> Dictionary:
	var diagnostic: Dictionary = _case_by_id(state.get("diagnosticCaseId"))
	var reveal := ["Debrief", "Complete"].has(state.get("phase"))
	var case_setup: Variant = null
	if state.get("evidenceCardsViewed", []).has("setup-record") and state.get("diagnosticSetup") != null:
		case_setup = state.diagnosticSetup.duplicate(true)
	var report := {
		"applicationVersion":"1.2.1", "modelVersion":MODEL_VERSION, "saveFormatVersion":SAVE_FORMAT,
		"attemptId":state.get("attemptId", ""), "mode":state.get("mode", ""), "scenarioSeed":state.get("scenarioSeed", 0), "complete":state.get("phase") == "Complete",
		"plates":state.get("plates", []).duplicate(true), "tubeContents":state.get("tubeContents", {}).duplicate(true), "tubeLabels":state.get("tubeLabels", {}).duplicate(true),
		"tubeSetupCorrections":state.get("tubeSetupCorrections", []).duplicate(), "plateAssignmentCorrections":state.get("plateAssignmentCorrections", []).duplicate(),
		"caseObservations":state.get("casePlates", []).duplicate(true), "predictions":state.get("predictions", []).duplicate(true),
		"prelockPredictionCorrections":state.get("prelockPredictionCorrections", []).duplicate(), "hintsUsed":state.get("hintsUsed", []).duplicate(),
		"explanationChoices":state.get("explanationChoices", []).duplicate(), "explanationChoiceHistory":state.get("explanationChoiceHistory", []).duplicate(),
		"caseSetup":case_setup, "diagnosticDecisions":state.get("diagnosticDecisions", []).duplicate(), "diagnosticDecisionHistory":state.get("diagnosticDecisionHistory", []).duplicate(),
		"evidenceViewed":state.get("evidenceViewed", []).duplicate(), "evidenceCardsViewed":state.get("evidenceCardsViewed", []).duplicate(), "retryOfAttemptId":state.get("retryOfAttemptId"),
		"ledger":_learner_facing_ledger(reveal), "sources":sources.get("entries", []).duplicate(true),
		"mainSelectionClaimSupported":_main_selection_supported(), "mainExpressionClaimSupported":_main_expression_supported(),
		"rubricStatus":_rubric_status(), "claimReasons":{"main-selection":_selection_reason_main(), "main-expression":_expression_reason_main()}, "diagnosticCase":null,
		"limits":"This fictional model does not demonstrate wet-lab competence. Growth on antibiotic media does not show that every cell was transformed; nonfluorescence does not establish absence of GFP DNA."
	}
	if reveal and not diagnostic.is_empty():
		report["diagnosticCase"] = _diagnostic_report(diagnostic)
	return report

func _acknowledge_intro() -> Array:
	if state.phase != "Intro":
		return [false, "Intro has already been acknowledged.", ""]
	return _move("TubeSetup", "Intro concepts recorded. Label the transformation reaction (T) and negative control (NC).", "DNA can be expressed through RNA to protein; GFP fluorescence requires excitation in this fictional model.")

func _label_tube(tube: Variant, label: Variant) -> Array:
	if state.phase != "TubeSetup" or not ["T", "NC"].has(tube) or str(label).strip_edges().is_empty():
		return [false, "Record each tube label before its conceptual transfer.", ""]
	if state.tubeLabels.has(tube):
		return [false, "That tube label is already recorded.", ""]
	state.tubeLabels[tube] = label
	return [true, "Label recorded for tube %s." % tube, "Labels identify a planned role; they do not replace actual contents."]

func _use_fresh_tip() -> Array:
	if state.phase != "TubeSetup" or state.tubeLabels.size() != 2:
		return [false, "Record T and NC labels before a conceptual fresh-tip transfer.", ""]
	state.freshTipReady = true
	state.selectedTubeSource = null
	state.selectedTubeDestination = null
	return [true, "Fresh conceptual tip recorded for one source-to-destination transfer.", "The model tracks source and destination as traceability decisions, not physical technique."]

func _select_tube_source(source: Variant) -> Array:
	if not state.freshTipReady or not ["DNA", "Buffer"].has(source):
		return [false, "Use a fresh conceptual tip, then choose DNA or Buffer as source.", ""]
	state.selectedTubeSource = source
	return [true, "Source %s selected." % source, ""]

func _select_tube_destination(tube: Variant) -> Array:
	if not state.freshTipReady or state.selectedTubeSource == null or not ["T", "NC"].has(tube) or state.tubeContents.has(tube):
		return [false, "Choose an unused T or NC destination after selecting a source.", ""]
	state.selectedTubeDestination = tube
	return [true, "Destination %s selected." % tube, ""]

func _load_tube() -> Array:
	var tube = state.selectedTubeDestination
	var content = state.selectedTubeSource
	if state.phase != "TubeSetup" or not state.freshTipReady or tube == null or content == null:
		return [false, "Use fresh tip, source, and destination controls before recording an actual tube content.", ""]
	state.tubeContents[tube] = content
	state.freshTipReady = false
	state.selectedTubeSource = null
	state.selectedTubeDestination = null
	if state.tubeContents.size() == 2:
		state.phase = "PlateSetup"
	return [true, "Actual content recorded for tube %s. Labels stay separate from contents." % tube, "Adding DNA does not mean every cell will receive it; the label alone cannot change the recorded content."]

func _correct_tube_content(tube: Variant, content: Variant) -> Array:
	if not ["TubeSetup", "PlateSetup", "Predictions"].has(state.phase) or state.predictionsLocked or not ["T", "NC"].has(tube) or not ["DNA", "Buffer"].has(content) or not state.tubeContents.has(tube):
		return [false, "Committed tube contents can be corrected before prediction lock; provisional predictions affected by the correction are retained in history and must be re-recorded.", ""]
	state.tubeSetupCorrections.append("%s:%s->%s" % [tube, state.tubeContents[tube], content])
	state.tubeContents[tube] = content
	_clear_provisional_predictions(_plates_for_tube(tube), "tube-content-correction")
	return [true, "Committed actual tube content corrected before prediction lock; affected provisional predictions were retained in history and must be recorded again.", ""]

func _recover_tube_preparation() -> Array:
	if state.phase != "TubeSetup":
		return [false, "Tube preparation recovery is only available before plate setup.", ""]
	state.tubeSetupCorrections.append("reset-preparation")
	state.freshTipReady = false
	state.selectedTubeSource = null
	state.selectedTubeDestination = null
	return [true, "Conceptual tube preparation reset; prior selections remain in correction history.", ""]

func _assign_plate(id: Variant, assignment: Variant) -> Array:
	if state.phase != "PlateSetup" or id == null or assignment == null:
		return [false, "Set each planned plate label before making predictions.", ""]
	var parsed := _assignment(str(assignment))
	var plate := _plate(id)
	if parsed.is_empty() or plate.is_empty():
		return [false, "Use a named plate, T or NC, and one listed conceptual medium.", ""]
	if not str(plate.tubeLabel).is_empty():
		return [false, "That plate assignment is already recorded.", ""]
	plate.tubeLabel = parsed[0]
	plate.medium = parsed[1]
	if _all_assigned():
		state.phase = "Predictions"
	return [true, "Plate %s assignment recorded." % id, "The media labels represent selection and induction concepts, not a protocol."]

func _correct_plate_assignment(id: Variant, assignment: Variant) -> Array:
	var parsed := _assignment(str(assignment))
	var plate := _plate(id)
	if not ["PlateSetup", "Predictions"].has(state.phase) or state.predictionsLocked or parsed.is_empty():
		return [false, "Plate assignments can be corrected only before all original predictions lock.", ""]
	if plate.is_empty() or str(plate.tubeLabel).is_empty():
		return [false, "Choose an already assigned plate.", ""]
	state.plateAssignmentCorrections.append("%s:%s|%s->%s" % [id, plate.tubeLabel, plate.medium, assignment])
	plate.tubeLabel = parsed[0]
	plate.medium = parsed[1]
	_clear_provisional_predictions([plate], "plate-assignment-correction")
	state.phase = "Predictions"
	return [true, "Plate assignment correction appended before outcomes; affected provisional predictions were retained in history and must be recorded again.", "A correction updates the conceptual setup but does not erase traceability history."]

func _clear_provisional_predictions(plates: Array, reason: String) -> void:
	for plate in plates:
		var prediction := _prediction(plate.id)
		if not prediction.is_empty():
			state.prelockPredictionCorrections.append("%s:%s:growth=%s;fluorescence=%s" % [reason, plate.id, prediction.originalGrowthPrediction, prediction.originalFluorescencePrediction])
			state.predictions.erase(prediction)

func _commit_prediction(id: Variant, prediction_text: Variant) -> Array:
	var parsed := _prediction_value(str(prediction_text))
	if state.phase != "Predictions" or id == null or parsed.is_empty():
		return [false, "Record independent growth and fluorescence predictions, or NotSure, for every plate before outcomes are available.", ""]
	if _plate(id).is_empty():
		return [false, "Choose a listed plate.", ""]
	var prediction := _prediction(id)
	if prediction.is_empty():
		prediction = {"plateId":id, "originalGrowthPrediction":"", "originalFluorescencePrediction":"", "revisions":[]}
		state.predictions.append(prediction)
	if parsed[0] == "growth":
		if not str(prediction.originalGrowthPrediction).is_empty():
			return [false, "That original growth prediction is already locked.", ""]
		prediction.originalGrowthPrediction = parsed[1]
	else:
		if not str(prediction.originalFluorescencePrediction).is_empty():
			return [false, "That original fluorescence prediction is already locked.", ""]
		prediction.originalFluorescencePrediction = parsed[1]
	if _predictions_complete():
		state.predictionsLocked = true
		state.phase = "TimeJump"
		return [true, "All original predictions are locked. Use the conceptual time jump when ready; no outcomes are shown yet.", "Predictions remain intact; later corrections append without replacing the original record."]
	return [true, "Prediction dimension recorded. Complete both dimensions for every plate before any observation.", ""]

func _advance_time() -> Array:
	if state.phase != "TimeJump":
		return [false, "Lock all predictions before the conceptual time jump.", ""]
	return _move("Observe", "Conceptual time jump complete. Choose normal or excitation view for each plate.", "This animation represents time passing conceptually and does not give a laboratory duration.")

func _view_plate(id: Variant, view: Variant, is_case: bool) -> Array:
	if (not is_case and not ["Observe", "Explain"].has(state.phase)) or (is_case and state.phase != "DiagnosticCase") or id == null or not ["normal", "excitation"].has(view):
		return [false, "This view is not available in the current stage.", ""]
	var plate := _case_plate(id) if is_case else _plate(id)
	if plate.is_empty():
		return [false, "Choose a listed plate.", ""]
	if view == "excitation" and not plate.normalViewSeen:
		return [false, "Record the normal view before judging whether fluorescence is assessable.", ""]
	var outcome := _case_outcome(state.diagnosticCaseId, id) if is_case else _outcome_for(plate)
	plate.activeView = view
	if view == "normal":
		plate.normalViewSeen = true
		plate.growth = outcome.growth
	else:
		plate.excitationViewSeen = true
		plate.fluorescence = "NotAssessable" if plate.growth == "Absent" else outcome.fluorescence
	var evidence := "%s%s:%s" % ["case:" if is_case else "main:", id, view]
	if not state.evidenceViewed.has(evidence):
		state.evidenceViewed.append(evidence)
	if not is_case and _main_all_seen():
		state.phase = "Explain"
	return [true, "%s view recorded for %s. The observed phenotype is available for explanation." % [view, id], _view_coaching(plate, view)]

func _view_coaching(plate: Dictionary, view: String) -> String:
	if plate.growth == "Absent":
		return "No colonies grew, so fluorescence cannot be assessed on this plate; that does not mean GFP is absent."
	if view == "normal":
		return "Colonies grew. Growth is an observation; it does not show that every cell took up the plasmid."
	if plate.fluorescence == "Detected":
		return "Green under excitation is consistent with GFP expression in these colonies; it does not show why expression occurred."
	return "These colonies are not green under excitation. That does not prove the GFP DNA is missing; expression may not have been switched on."

func _explain(id: Variant, choice: Variant) -> Array:
	if state.phase != "Explain" or id == null or not ["no-colonies-not-assessable", "nongreen-not-no-dna", "green-consistent-gfp", "growth-proves-all", "nonfluorescence-proves-no-dna"].has(choice) or _plate(id).is_empty():
		return [false, "Choose a plate-specific observation explanation after observations.", ""]
	var prefix := "%s:" % id
	for prior in state.explanationChoices.duplicate():
		if str(prior).begins_with(prefix):
			state.explanationChoiceHistory.append(prior)
			state.explanationChoices.erase(prior)
	state.explanationChoices.append(prefix + str(choice))
	return [true, "Explanation choice recorded. The debrief distinguishes observations from what the controls can support.", "Phenotypes can fit several causes, so the model scores evidence limits rather than guessing a hidden cause."]

func _start_case(id: Variant) -> Array:
	if state.phase != "Explain" or not _main_all_seen() or not _all_main_explained():
		return [false, "Record one plate-specific explanation for every main observation before choosing one replayable Lab Detective case.", ""]
	var chosen := _case_by_id("missing-arabinose" if id == null else id)
	if chosen.is_empty():
		return [false, "Choose one listed fictional diagnostic case.", ""]
	state.diagnosticCaseId = chosen.id
	state.diagnosticSetup = chosen.setup.duplicate(true)
	state.phase = "DiagnosticCase"
	state.casePlates = _fresh_plates()
	return [true, "Lab Detective case selected. Observe evidence before the fictional cause is disclosed in debrief.", "The case is curated separately from the normal run; a pattern does not prove a unique real-world cause."]

func _record_decision(decision: Variant, rationale: Variant) -> Array:
	if state.phase != "DiagnosticCase" or not _case_all_seen() or not state.evidenceCardsViewed.has("setup-record") or not state.evidenceCardsViewed.has("control-comparison") or not ["selection-supported", "selection-withheld", "expression-supported", "expression-withheld", "uncertainty-cannot-prove", "uncertainty-proves-cause", "uncertainty-not-sure"].has(decision) or not ["phenotype-not-cause", "control-pattern", "uncertain"].has(rationale):
		return [false, "View case evidence cards and choose a structured claim plus uncertainty statement.", ""]
	var dimension := "selection" if str(decision).begins_with("selection") else "expression" if str(decision).begins_with("expression") else "uncertainty"
	for prior in state.diagnosticDecisions.duplicate():
		if str(prior).begins_with(dimension + "-"):
			state.diagnosticDecisionHistory.append(prior)
			state.diagnosticDecisions.erase(prior)
	state.diagnosticDecisions.append("%s:%s" % [decision, rationale])
	return [true, "Diagnostic decision recorded. It will be assessed for observation, plausibility, and evidence limits.", ""]

func _view_evidence_card(card: Variant) -> Array:
	if state.phase != "DiagnosticCase" or not ["setup-record", "control-comparison", "observation-limit"].has(card):
		return [false, "This evidence card is available during the Lab Detective case.", ""]
	if not state.evidenceCardsViewed.has(card):
		state.evidenceCardsViewed.append(card)
	return [true, "Evidence card viewed: %s." % card, "This is an inspectable record, not a hidden-cause answer."]

func _hint(id: Variant) -> Array:
	var hint := "controls" if id == null else str(id)
	if not state.hintsUsed.has(hint):
		state.hintsUsed.append(hint)
	return [true, "Hint recorded in this attempt history.", "Use controls to distinguish selection from expression. A planned claim is withheld when its control is not interpretable."]

func _revise_prediction(id: Variant, revised: Variant) -> Array:
	if not state.predictionsLocked or not ["Explain", "DiagnosticCase", "Complete"].has(state.phase) or id == null or _prediction_value(str(revised)).is_empty():
		return [false, "A later growth or fluorescence revision must use a valid dimension and value after original predictions lock.", ""]
	var prediction := _prediction(id)
	if prediction.is_empty() or str(prediction.originalGrowthPrediction).is_empty() or str(prediction.originalFluorescencePrediction).is_empty():
		return [false, "Choose a fully predicted plate.", ""]
	prediction.revisions.append(revised)
	return [true, "Revision appended; the original prediction remains unchanged.", ""]

func _record_closeout(item: String) -> Array:
	if state.phase != "DiagnosticCase" or not _case_all_seen():
		return [false, "View all case evidence before simulated cleanup and documentation.", ""]
	if item == "cleanup":
		state.cleanupRecorded = true
	elif item == "waste":
		state.wasteDecisionRecorded = true
	else:
		state.debriefDocumented = true
	return [true, "Simulated %s recorded." % item, "This is a documentation and closeout concept, not a local disposal procedure."]

func _complete_debrief() -> Array:
	if state.phase != "DiagnosticCase" or state.diagnosticCaseId == null or not _case_all_seen() or state.evidenceCardsViewed.size() < 3 or not _all_main_explained() or state.diagnosticDecisions.size() != 3 or not _has_decision_dimension("selection") or not _has_decision_dimension("expression") or not _has_decision_dimension("uncertainty") or not state.cleanupRecorded or not state.wasteDecisionRecorded or not state.debriefDocumented:
		return [false, "View all case evidence cards, record two structured decisions, and record simulated cleanup, waste decision, and documentation before debrief.", ""]
	state.phase = "Complete"
	return [true, "Debrief complete. The fictional case cause is now displayed with evidence limits.", ""]

func _record(accepted: bool, message: String, coaching: String, action_type: Variant = null, target: Variant = null, value: Variant = null) -> Dictionary:
	if action_type != null:
		state.ledger.append({"type":action_type, "target":target, "value":value, "accepted":accepted, "outcome":message})
	if state.mode == "Assessment":
		return {"accepted":accepted, "message":"Action recorded. Coaching is held for debrief." if accepted else "Action was not recorded. Review the available controls.", "coaching":"Procedural coaching is deferred to debrief.", "snapshot":snapshot()}
	return {"accepted":accepted, "message":message, "coaching":coaching, "snapshot":snapshot()}

func _result(accepted: bool, message: String, coaching: String) -> Dictionary:
	return _record(accepted, message, coaching)

func _move(next: String, message: String, coaching: String) -> Array:
	state.phase = next
	return [true, message, coaching]

func _fresh_plates() -> Array:
	var result: Array = []
	for p in scenario.get("plates", []):
		result.append({"id":p.id, "label":p.label, "tubeLabel":"", "medium":"", "growth":"NotViewed", "fluorescence":"NotViewed", "activeView":"", "normalViewSeen":false, "excitationViewSeen":false})
	return result

func _plate(id: Variant) -> Dictionary:
	for plate in state.get("plates", []):
		if plate.id == id:
			return plate
	return {}

func _case_plate(id: Variant) -> Dictionary:
	for plate in state.get("casePlates", []):
		if plate.id == id:
			return plate
	return {}

func _case_by_id(id: Variant) -> Dictionary:
	for item in cases.get("cases", []):
		if item.id == id:
			return item
	return {}

func _case_outcome(case_id: Variant, plate_id: Variant) -> Dictionary:
	return _case_by_id(case_id).get("outcomes", {}).get(plate_id, {})

func _prediction(id: Variant) -> Dictionary:
	for item in state.get("predictions", []):
		if item.plateId == id:
			return item
	return {}

func _plates_for_tube(tube: String) -> Array:
	var answer: Array = []
	for plate in state.plates:
		if plate.tubeLabel == tube:
			answer.append(plate)
	return answer

func _assignment(value: String) -> Array:
	var fields := value.split("|")
	if fields.size() == 2 and ["T", "NC"].has(fields[0]) and ["LB", "LB+amp", "LB+amp+Ara"].has(fields[1]):
		return [fields[0], fields[1]]
	return []

func _prediction_value(value: String) -> Array:
	var fields := value.split(":")
	if fields.size() != 2:
		return []
	if fields[0] == "growth" and ["Present", "Absent", "NotSure"].has(fields[1]):
		return fields
	if fields[0] == "fluorescence" and ["Detected", "NoneDetected", "NoColoniesToAssess", "NotSure"].has(fields[1]):
		return fields
	return []

func _all_assigned() -> bool:
	for plate in state.plates:
		if str(plate.tubeLabel).is_empty():
			return false
	return true

func _predictions_complete() -> bool:
	if state.predictions.size() != 4:
		return false
	for item in state.predictions:
		if str(item.originalGrowthPrediction).is_empty() or str(item.originalFluorescencePrediction).is_empty():
			return false
	return true

func _main_all_seen() -> bool:
	for plate in state.plates:
		if not plate.normalViewSeen or not plate.excitationViewSeen:
			return false
	return true

func _case_all_seen() -> bool:
	if state.casePlates.size() != 4:
		return false
	for plate in state.casePlates:
		if not plate.normalViewSeen or not plate.excitationViewSeen:
			return false
	return true

func _all_main_explained() -> bool:
	for plate in state.plates:
		if not _has_main_explanation(plate):
			return false
	return true

func _has_main_explanation(plate: Dictionary) -> bool:
	for choice in state.explanationChoices:
		if str(choice).begins_with(str(plate.id) + ":"):
			return true
	return false

func _has_decision_dimension(dimension: String) -> bool:
	var count := 0
	for decision in state.diagnosticDecisions:
		if str(decision).begins_with(dimension + "-"):
			count += 1
	return count == 1

func _outcome_for(plate: Dictionary) -> Dictionary:
	var has_dna: bool = state.tubeContents.get(plate.tubeLabel) == "DNA"
	var amp: bool = str(plate.medium).contains("amp")
	var ara: bool = str(plate.medium).contains("Ara")
	var growth := "Absent" if amp and not has_dna else "Present"
	return {"growth":growth, "fluorescence":"NotAssessable" if growth == "Absent" else "Detected" if has_dna and ara else "NoneDetected"}

func _has_assignment(id: String) -> bool:
	var plate := _plate(id)
	return not plate.is_empty() and EXPECTED_ASSIGNMENTS[id] == str(plate.tubeLabel) + "|" + str(plate.medium)

func _main_selection_supported() -> bool:
	return state.plates.size() == 4 and _has_assignment("P1") and _has_assignment("P2") and _has_assignment("P4") and _plate("P1").normalViewSeen and _plate("P2").normalViewSeen and _plate("P4").normalViewSeen and state.tubeContents.get("T") == "DNA" and state.tubeContents.get("NC") == "Buffer" and _plate("P1").growth == "Present" and _plate("P2").growth == "Absent" and _plate("P4").growth == "Present"

func _main_expression_supported() -> bool:
	return _main_selection_supported() and _has_assignment("P3") and _plate("P3").normalViewSeen and _plate("P1").excitationViewSeen and _plate("P2").excitationViewSeen and _plate("P3").excitationViewSeen and _plate("P4").excitationViewSeen and _plate("P1").fluorescence == "NoneDetected" and _plate("P2").fluorescence == "NotAssessable" and _plate("P3").growth == "Present" and _plate("P3").fluorescence == "Detected" and _plate("P4").fluorescence == "NoneDetected"

func _selection_reason_main() -> String:
	for plate in state.get("plates", []):
		if not plate.normalViewSeen:
			return "Selection claim is withheld until all planned normal-view observations are recorded."
	if _main_selection_supported():
		return "The planned fictional controls support only a limited selection interpretation; observed growth does not mean every cell transformed."
	return "The planned selection claim is withheld because the actual contents, control pattern, assignments, or observations are not interpretable as the intended four-plate comparison."

func _expression_reason_main() -> String:
	for plate in state.get("plates", []):
		if not plate.excitationViewSeen:
			return "Expression claim is withheld until all planned excitation observations are recorded."
	if _main_expression_supported():
		return "The planned fictional controls support only a limited induced-expression interpretation; nonfluorescence does not establish absence of GFP DNA."
	return "The planned expression claim is withheld because the selection claim or excitation-control pattern is not interpretable; observations remain reportable."

func _diagnostic_report(diagnostic: Dictionary) -> Dictionary:
	var selection := _derived_case_selection_supported(diagnostic)
	var expression := _derived_case_expression_supported(diagnostic)
	return {"id":"case-debrief", "title":diagnostic.title, "finalFictionalCause":diagnostic.fictionalCause, "selectionClaimSupported":selection, "expressionClaimSupported":expression, "selectionClaimReason":"The curated controls support a limited selection claim in this fictional case." if selection else "The curated control pattern withholds the planned selection claim; observations remain reportable.", "expressionClaimReason":"The curated controls support a limited expression claim in this fictional case." if expression else "The curated control pattern withholds the planned expression claim; phenotype alone does not prove one cause."}

func _learner_facing_ledger(reveal: bool) -> Array:
	var copy: Array = state.get("ledger", []).duplicate(true)
	if not reveal:
		for record in copy:
			if record.type == "StartDiagnosticCase":
				record.target = "case-in-progress"
	return copy

func _decision_status(dimension: String, expected_support: bool) -> String:
	var current := ""
	for decision in state.get("diagnosticDecisions", []):
		if str(decision).begins_with(dimension + "-"):
			current = decision
	if current.is_empty():
		return "not recorded"
	var expected := "uncertainty-cannot-prove" if dimension == "uncertainty" else dimension + ("-supported" if expected_support else "-withheld")
	return "aligned" if current.begins_with(expected) else "mismatch"

func _rubric_status() -> Dictionary:
	var diagnostic := _case_by_id(state.get("diagnosticCaseId"))
	var selection := _decision_status("selection", not diagnostic.is_empty() and _derived_case_selection_supported(diagnostic))
	var expression := _decision_status("expression", not diagnostic.is_empty() and _derived_case_expression_supported(diagnostic))
	var uncertainty := _decision_status("uncertainty", true)
	var all_explained := _all_main_explained()
	var aligned := true
	for plate in state.get("plates", []):
		var expected := "no-colonies-not-assessable" if plate.id == "P2" else "green-consistent-gfp" if plate.id == "P3" else "nongreen-not-no-dna"
		if not state.explanationChoices.has("%s:%s" % [plate.id, expected]):
			aligned = false
	var evidence: String
	if not all_explained:
		evidence = "main explanation pending"
	elif not state.evidenceCardsViewed.has("observation-limit") or not _has_decision_dimension("uncertainty"):
		evidence = "main explanations aligned; case uncertainty pending" if aligned else "main explanation mismatch recorded; case uncertainty pending"
	else:
		evidence = "main explanations aligned; case uncertainty %s" % uncertainty if aligned else "main explanation mismatch recorded; case uncertainty %s" % uncertainty
	return {"observations":"main and case views recorded" if state.evidenceViewed.size() >= 16 else "incomplete", "plausibility":"selection %s; expression %s" % [selection, expression] if state.evidenceCardsViewed.has("control-comparison") and state.diagnosticDecisions.size() == 3 else "not yet recorded", "evidence-limits":evidence, "support-status":"debriefed from control comparisons: selection %s; expression %s; uncertainty %s" % [selection, expression, uncertainty] if state.phase == "Complete" else "awaiting debrief"}

func _derived_case_selection_supported(definition: Dictionary) -> bool:
	return definition.get("id") == "missing-arabinose" and _case_signature(definition) == CASE_SIGNATURES.get(definition.get("id"))

func _derived_case_expression_supported(definition: Dictionary) -> bool:
	return _derived_case_selection_supported(definition) and definition.outcomes.P3.fluorescence == "Detected"

func _case_signature(definition: Dictionary) -> String:
	var parts: Array = []
	for id in ["P1", "P2", "P3", "P4"]:
		parts.append("%s:%s:%s" % [id, definition.outcomes[id].growth, definition.outcomes[id].fluorescence])
	return "|".join(parts)

func _case_setup_signature(setup: Dictionary) -> String:
	return "tubes:%s;actual:%s;intended:%s" % [_format_dictionary(setup.tubeContents), _format_dictionary(setup.plateAssignments), _format_dictionary(setup.intendedPlateLabels)]

func _format_dictionary(values: Dictionary) -> String:
	var keys := values.keys()
	keys.sort()
	var items: Array = []
	for key in keys:
		items.append("%s=%s" % [key, values[key]])
	return "|".join(items)

func _definitions_valid(candidate_scenario: Variant, candidate_lessons: Variant, candidate_cases: Variant, candidate_rubric: Variant, candidate_sources: Variant) -> bool:
	if typeof(candidate_scenario) != TYPE_DICTIONARY or typeof(candidate_lessons) != TYPE_DICTIONARY or typeof(candidate_cases) != TYPE_DICTIONARY or typeof(candidate_rubric) != TYPE_DICTIONARY or typeof(candidate_sources) != TYPE_DICTIONARY:
		return false
	if candidate_scenario.get("schemaVersion") != 1 or candidate_lessons.get("schemaVersion") != 1 or candidate_cases.get("schemaVersion") != 1 or candidate_rubric.get("schemaVersion") != 1 or candidate_sources.get("schemaVersion") != 1 or str(candidate_scenario.get("id", "")).is_empty() or candidate_scenario.get("plates", []).size() != 4 or candidate_lessons.get("lessons", []).size() < 4 or candidate_sources.get("entries", []).is_empty():
		return false
	var seen: Array = []
	for plate in candidate_scenario.plates:
		if not EXPECTED_ASSIGNMENTS.has(plate.get("id")) or plate.get("expectedAssignment") != EXPECTED_ASSIGNMENTS[plate.id] or seen.has(plate.id):
			return false
		seen.append(plate.id)
	var ids: Array = []
	for definition in candidate_cases.get("cases", []):
		ids.append(definition.get("id"))
	ids.sort()
	var expected := CASE_IDS.duplicate()
	expected.sort()
	if ids != expected:
		return false
	var dimensions: Array = candidate_rubric.get("dimensions", []).duplicate()
	dimensions.sort()
	if dimensions != ["evidence-limits", "observations", "plausibility", "support-status"]:
		return false
	var criteria: Variant = candidate_rubric.get("criteria")
	if typeof(criteria) != TYPE_DICTIONARY:
		return false
	var criterion_keys: Array = criteria.keys()
	criterion_keys.sort()
	if criterion_keys != ["evidence-limits", "observations", "plausibility", "support-status"]:
		return false
	for key in criterion_keys:
		if str(criteria[key]).strip_edges().is_empty():
			return false
	for source in candidate_sources.get("entries", []):
		if typeof(source) != TYPE_DICTIONARY or str(source.get("id", "")).is_empty():
			return false
		var url := str(source.get("url", ""))
		if not url.is_empty() and not _valid_https_url(url):
			return false
	for definition in candidate_cases.cases:
		if str(definition.get("title", "")).to_lower().begins_with("missing") or not CASE_SIGNATURES.has(definition.get("id")) or _case_signature(definition) != CASE_SIGNATURES[definition.id] or _case_setup_signature(definition.get("setup", {})) != CASE_SETUP_SIGNATURES[definition.id] or definition.get("selectionClaimSupported") != _derived_case_selection_supported(definition) or definition.get("expressionClaimSupported") != _derived_case_expression_supported(definition):
			return false
	return true

func _read_json(path: String) -> Dictionary:
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	return parsed if typeof(parsed) == TYPE_DICTIONARY else {}

func _valid_https_url(value: String) -> bool:
	if not value.begins_with("https://") or value.length() <= 8:
		return false
	var authority := value.substr(8).split("/", true, 1)[0]
	if authority.is_empty() or authority.find(" ") != -1 or authority.find("\t") != -1 or authority.contains("@") or authority.contains("?") or authority.contains("#"):
		return false
	if authority.begins_with("["):
		var bracket := authority.find("]")
		if bracket <= 1:
			return false
		return bracket == authority.length() - 1 or _valid_port(authority.substr(bracket + 1))
	var host_port := authority.split(":", true)
	if host_port.size() > 2 or host_port[0].is_empty():
		return false
	return host_port.size() == 1 or _valid_port(":" + host_port[1])

func _valid_port(value: String) -> bool:
	if not value.begins_with(":") or not value.substr(1).is_valid_int():
		return false
	var port := value.substr(1).to_int()
	return port >= 1 and port <= 65535

func _action_names() -> Array:
	return ["AcknowledgeIntro", "LabelTube", "UseFreshTip", "SelectTubeSource", "SelectTubeDestination", "LoadTube", "CorrectTubeContent", "RecoverTubePreparation", "AssignPlate", "CorrectPlateAssignment", "CommitPrediction", "AdvanceConceptualTime", "ViewPlate", "RecordExplanation", "StartDiagnosticCase", "ViewCasePlate", "ViewEvidenceCard", "RecordDiagnosticDecision", "RequestHint", "RevisePrediction", "RecordCleanup", "RecordWasteDecision", "RecordDebrief", "CompleteDebrief"]

func _valid_id(value: String) -> bool:
	return value.length() == 32 and value.is_valid_hex_number()

func _id() -> String:
	return "%08x%08x%08x%08x" % [randi(), randi(), randi(), randi()]

func _same_tree(left: Variant, right: Variant) -> bool:
	if left == null or right == null:
		return left == right
	if typeof(left) in [TYPE_INT, TYPE_FLOAT] or typeof(right) in [TYPE_INT, TYPE_FLOAT]:
		return float(left) == float(right)
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
