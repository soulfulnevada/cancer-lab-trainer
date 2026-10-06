class_name Level1Simulation
extends RefCounted

# Browser implementation of the public LabSimulation seam. All run inputs are
# loaded from the copied canonical definition rather than embedded in code.
const MODEL_VERSION := "1.2.0"
# 1.1.0 saves replay unchanged unless their controls failed after the reader ran.
const RESTORABLE_MODEL_VERSIONS := ["1.1.0", MODEL_VERSION]
const FLAG_LABELS := {"TransferBeforePpe":"transfer before PPE", "WrongVolume":"wrong volume", "ReaderMode":"reader mode", "Carryover":"tip carryover", "FastAspirationRelease":"fast aspiration release", "DuplicateTransfer":"duplicate transfer", "UnsupportedConclusion":"unsupported conclusion", "ReplicateVariation":"replicate spread above the QC threshold"}
const SAVE_FORMAT := 3
const WEB_ENVELOPE := "webEnvelope1"
const LEVEL_ID := "level1"
var scenario: Dictionary = {}
var rules: Dictionary = {}
var sources: Dictionary = {}
var state: Dictionary = {}
var lessons: Array = []

func _init() -> void:
	scenario = _load("res://data/scenario.v1.json")
	rules = _load("res://data/rules.v1.json")
	sources = _load("res://data/sources.v1.json")

func start(mode: String) -> Dictionary:
	if not ["GuidedPractice", "Assessment"].has(mode):
		return {}
	lessons = []
	var wells: Array = []
	for defined in scenario.get("activeWells", []):
		wells.append({"well": defined.well, "role": defined.role, "expectedSignal": float(defined.expectedSignal), "transferVolumeUl": null, "tipId": null, "sourceRole": null, "carryoverFactor": 1.0, "contributionsUl": {}, "rawReading": null, "backgroundAdjusted": null, "relativeToVehicle": null})
	state = {"scenarioId":scenario.id,"scenarioSchemaVersion":scenario.schemaVersion,"attemptId":_id(),"mode":mode,"phase":"Preparation","started":true,"ppeWorn":false,"benchDisinfected":false,"materialsChecked":false,"labelsVerified":false,"plateMapReviewed":false,"tipAttached":false,"tipId":0,"pipetteStage":"ReadyInAir","selectedVolumeUl":null,"selectedSource":null,"loadedSource":null,"boundDestination":null,"aspiratedVolumeUl":0.0,"discardedVolumeUl":0.0,"tipContaminationRole":null,"carryoverRisk":false,"sourceRemainingVolumeUl":{"Blank":250.0,"Vehicle control":250.0,"Fictional treatment":250.0},"simulatedMinutes":0,"attemptNumber":1,"ledger":[],"plateLoaded":false,"correctOrientation":false,"readerConfigured":false,"readerRan":false,"resultsReviewed":false,"escalated":false,"wasteSorted":false,"benchCleanedAtCloseout":false,"handoffRecorded":false,"controlsValid":false,"canSupportConclusion":false,"interpretationDecision":null,"wells":wells,"issues":[],"checkAnswers":[]}
	return snapshot()

func submit(action: String, target: Variant = null, value: Variant = null) -> Dictionary:
	if not state.get("started", false): return _result(false, "Start a scenario before submitting actions.")
	if not _action_names().has(action): return _result(false, "A recognized action is required.")
	if state.phase == "Complete" and action != "AnswerCheck": return _result(false, "This completed attempt is locked. Start a new attempt to preserve its audit record.")
	if state.readerRan and not ["ReviewResults", "DecideSupportedConclusion", "EscalateInvalidRun", "SortWaste", "CleanBench", "RecordHandoff", "AnswerCheck"].has(action): return _result(false, "Acquired measurements are locked. Start a new attempt for a new preparation or reading.")
	if ["SortWaste", "CleanBench", "RecordHandoff"].has(action) and state.phase != "Closeout": return _result(false, "Record the result disposition before completing closeout.")
	var result: Dictionary = _dispatch(action, target, value)
	if result.accepted: state.simulatedMinutes += 2
	var recorded_value: Variant = value if value != null and is_finite(float(value)) else null
	state.ledger.append({"simulatedMinute":state.simulatedMinutes,"attemptNumber":state.attemptNumber,"action":action,"target":target,"value":recorded_value,"outcome":result.message})
	_update_phase()
	var science := _science(action, result.accepted, target, value)
	var lesson_record := {"title":science.get("title", ""), "explanation":science.get("lesson", ""), "whyItMatters":science.get("why", "")}
	if result.accepted and action != "AnswerCheck" and not science.is_empty() and not lessons.has(lesson_record): lessons.append(lesson_record)
	result.scienceTitle = science.get("title", "Check the workflow")
	result.scienceLesson = science.get("lesson", "The simulation keeps actions ordered so that the next observation has context.")
	result.whyItMatters = science.get("why", "In real work, pause and follow the local procedure when the next safe step is unclear.")
	if state.mode == "Assessment":
		result.message = "Action recorded. Details are held for the final debrief." if result.accepted else "Action was not recorded. Review the available workflow controls."
		result.scienceTitle = "Assessment record"
		result.scienceLesson = "Procedural coaching is deferred until the final debrief."
		result.whyItMatters = "The same simulated outcomes and assessment rules apply in both modes."
	result.snapshot = snapshot()
	return result

func _dispatch(action: String, target: Variant, value: Variant) -> Dictionary:
	match action:
		"WearPpe": return _once("ppeWorn", "PPE check recorded.")
		"DisinfectBench": return _once("benchDisinfected", "Workspace preparation recorded.")
		"CheckMaterials": return _once("materialsChecked", "Materials check recorded.")
		"VerifyLabels": return _once("labelsVerified", "Sample identities verified.")
		"ReviewPlateMap": return _once("plateMapReviewed", "Plate map reviewed.")
		"AttachTip": return _attach()
		"EjectTip": return _eject()
		"ChangeTip": return _no("Changing a tip cannot bypass a pipetting cycle. Eject the empty tip in air, then attach a fresh tip.")
		"SetVolume": return _volume(value)
		"SelectSource": return _source(str(target))
		"PressFirstStop": return _first()
		"MoveToSource": return _move_source(str(target))
		"ReleaseSlow": return _release(false)
		"ReleaseFast": return _release(true)
		"MoveToDestination": return _destination(str(target))
		"PressSecondStop": return _second()
		"WithdrawAndRelease": return _withdraw()
		"Aspirate": return _no("Aspirate is no longer a shortcut. Press the first stop in air, move to the selected source, then release while immersed.")
		"Dispense": return _no("Dispense is no longer a shortcut. Position at the destination, press the first stop, clear with the second stop, then withdraw and release.")
		"MislabelSelectedWell": return _mislabel(target)
		"CorrectLabel": return _correct_label(target)
		"RetryTransferCheckpoint": return _retry_transfer_checkpoint()
		"LoadPlate": return _plate(str(target))
		"ConfigureReader": return _reader(str(target))
		"RunReader": return _run()
		"ReviewResults": return _review()
		"DecideSupportedConclusion": return _decide()
		"EscalateInvalidRun": return _escalate()
		"SortWaste": return _once("wasteSorted", "Waste decision recorded.")
		"CleanBench": return _once("benchCleanedAtCloseout", "Closeout cleaning recorded.")
		"RecordHandoff": return _once("handoffRecorded", "Handoff record completed.")
		"AnswerCheck": return _answer_check(target)
		_: return _no("That action is not available in this version of the scenario.")

func _once(field: String, message: String) -> Dictionary:
	if state[field]: return _no("That item is already recorded.")
	state[field] = true
	return _yes(message)
func _attach() -> Dictionary:
	if state.tipAttached: return _no("Eject the current empty tip in air before attaching another tip.")
	if state.pipetteStage != "ReadyInAir": return _no("Finish the active pipetting cycle before attaching a tip.")
	state.tipAttached=true; state.tipId+=1; state.tipContaminationRole=null; state.carryoverRisk=false
	return _yes("Tip attached in air.")
func _eject() -> Dictionary:
	if not state.tipAttached: return _no("Attach a tip before ejecting it.")
	if state.pipetteStage != "ReadyInAir": return _no("A tip can be ejected only in air after the cycle has been completed.")
	state.tipAttached=false; state.tipContaminationRole=null; state.carryoverRisk=false; state.selectedSource=null
	return _yes("Empty tip ejected in air.")
func _volume(value: Variant) -> Dictionary:
	if not state.tipAttached: return _no("Attach a tip before setting up the illustrative transfer.")
	if state.pipetteStage != "ReadyInAir": return _no("Finish the active pipetting cycle before changing the volume setting.")
	if value == null or not is_finite(float(value)) or float(value) <= 0.0 or float(value) > 250.0: return _no("Choose an illustrative volume between 0 and 250 µL.")
	state.selectedVolumeUl=float(value); return _yes("Volume setting recorded.")
func _source(source: String) -> Dictionary:
	if state.pipetteStage != "ReadyInAir": return _no("The active cycle retains its source identity. Complete it before selecting another source.")
	if not state.sourceRemainingVolumeUl.has(source): return _no("Choose one of the visible fictional sample sources.")
	state.selectedSource=source; return _yes("Selected fictional source: %s." % source)
func _first() -> Dictionary:
	if state.pipetteStage == "ReadyInAir":
		if not state.tipAttached or state.selectedVolumeUl == null or state.selectedSource == null: return _no("Attach a tip, select a source, and set a nominal volume before pressing the first stop in air.")
		if state.sourceRemainingVolumeUl[state.selectedSource] < state.selectedVolumeUl: return _no("That fictional source does not have enough nominal volume remaining; adjust the setting before starting the cycle.")
		state.pipetteStage="FirstStopInAir"; return _yes("First stop pressed in air. Move the tip to the selected source before releasing.")
	if state.pipetteStage == "LoadedAtDestination":
		var transfer: Dictionary = _apply_transfer(str(state.boundDestination), str(state.loadedSource), float(state.aspiratedVolumeUl), bool(state.carryoverRisk))
		if not transfer.accepted: return transfer
		state.pipetteStage="FirstStopAtDestination"; return _yes("Nominal %s µL transfer recorded at %s. Keep the plunger pressed for the second stop." % [_format_quantity(state.selectedVolumeUl),state.boundDestination])
	return _no("Press the first stop only in air before aspiration or at the bound destination after loading.")
func _move_source(source: String) -> Dictionary:
	if state.pipetteStage != "FirstStopInAir": return _no("Press the first stop in air before moving to a source.")
	if source != "" and source != state.selectedSource: return _no("Move to the selected source; source identity is fixed for this cycle.")
	state.pipetteStage="FirstStopInSource"; return _yes("Tip positioned in %s. Release slowly while immersed to record the nominal load." % state.selectedSource)
func _release(fast: bool) -> Dictionary:
	if state.pipetteStage != "FirstStopInSource" or not state.tipAttached or state.selectedVolumeUl == null or state.selectedSource == null: return _no("Press the first stop in air and move to the selected source before releasing.")
	if state.sourceRemainingVolumeUl[state.selectedSource] < state.selectedVolumeUl: return _no("That fictional source does not have enough modeled volume remaining.")
	if state.tipContaminationRole != null and state.tipContaminationRole != state.selectedSource: state.carryoverRisk=true; _issue("Carryover", "A tip was reused across distinct fictional sources. The model applies an illustrative carryover factor to the next dispense.", true, true)
	state.sourceRemainingVolumeUl[state.selectedSource]-=state.selectedVolumeUl; state.aspiratedVolumeUl=state.selectedVolumeUl; state.loadedSource=state.selectedSource; state.tipContaminationRole=state.selectedSource; state.pipetteStage="LoadedAtSource"
	if fast: _issue("FastAspirationRelease", "The source release was recorded as fast. This is a qualitative, recoverable handling condition; the model does not quantify a physical volume error, so nominal inventory and fictional readings remain unchanged.", true, true)
	return _yes("Nominal %s µL loaded from %s; the toy source inventory is conserved." % [_format_quantity(state.aspiratedVolumeUl),state.selectedSource])
func _destination(target: String) -> Dictionary:
	if state.pipetteStage != "LoadedAtSource" or state.aspiratedVolumeUl <= 0.0: return _no("Load a nominal quantity from a source before positioning at a destination.")
	var well: Variant = _well(target)
	if well == null: return _no("Choose one of the active training wells before positioning at a destination.")
	state.boundDestination=well.well; state.pipetteStage="LoadedAtDestination"; return _yes("Withdrew from %s and positioned at %s. This well is now bound for the cycle." % [state.loadedSource,well.well])
func _second() -> Dictionary:
	if state.pipetteStage != "FirstStopAtDestination": return _no("Press the destination first stop before clearing the modeled residue with the second stop.")
	state.pipetteStage="SecondStopAtDestination"; return _yes("Second stop recorded. No additional nominal microlitres were added.")
func _withdraw() -> Dictionary:
	if state.pipetteStage != "SecondStopAtDestination": return _no("Complete the destination second stop before withdrawing while held and releasing in air.")
	state.pipetteStage="ReadyInAir"; state.loadedSource=null; state.boundDestination=null; state.carryoverRisk=false; return _yes("Tip withdrawn while held, then released in air. The pipette is ready for the next explicit cycle.")
func _apply_transfer(target: String, source: String, volume: float, carryover_risk: bool) -> Dictionary:
	if not state.ppeWorn: _issue("TransferBeforePpe", "A transfer was attempted before the preparation check was recorded.", true, true)
	if not state.labelsVerified or not state.plateMapReviewed: _issue("TransferBeforeTraceability", "A transfer was attempted before sample identity and plate-map checks.", true, true)
	var well: Variant = _well(target)
	if well == null: _issue("WrongWell", "%s is outside the active training layout." % target, true, true); state.aspiratedVolumeUl=0.0; return _yes("The selected destination is not in the active layout; the error is preserved for debrief.")
	if well.transferVolumeUl != null: _issue("DuplicateTransfer", "%s received an additional modeled transfer." % well.well, true, true)
	if source != well.role: _issue("SampleIdentityMismatch", "%s was dispensed into %s, which is planned as %s." % [source,well.well,well.role], true, true)
	if absf(volume - float(scenario.toyTransferVolumeUl)) > .01: _issue("WrongVolume", "%s received %0.1f µL instead of the scenario's %0.1f µL." % [well.well,volume,float(scenario.toyTransferVolumeUl)], true, true)
	well.transferVolumeUl=(0.0 if well.transferVolumeUl == null else float(well.transferVolumeUl))+volume; well.contributionsUl[source]=float(well.contributionsUl.get(source,0.0))+volume; well.tipId=state.tipId; well.sourceRole=source; well.carryoverFactor=1.08 if carryover_risk else 1.0; state.aspiratedVolumeUl=0.0
	return _yes("Illustrative dispense recorded for %s (%s)." % [well.well,well.role])

func _mislabel(target: Variant) -> Dictionary:
	var well: Variant = _well(str(target))
	if well == null:
		return _no("Select an active well before recording a label mismatch.")
	_issue("Mislabel", "A fictional label mismatch was recorded for %s. Correct it before acquisition or escalate it." % target, true, true, str(target).to_upper())
	return _yes("Fictional label mismatch recorded.")

func _correct_label(target: Variant) -> Dictionary:
	var found := false
	for issue in state.issues:
		if issue.code == "Mislabel" and not issue.resolved and issue.target == str(target).to_upper():
			found = true
	if not found:
		return _no("No active label mismatch is recorded for that well.")
	_resolve_issue("Mislabel", str(target).to_upper())
	return _yes("Label check for %s recorded as corrected before acquisition." % (str(target) if target != null else "selected well"))

func _retry_transfer_checkpoint() -> Dictionary:
	if state.mode != "GuidedPractice":
		return _no("Checkpoint retry is available only in Guided Practice; Assessment preserves the original attempt.")
	if state.readerRan:
		return _no("Start a new attempt after acquisition; this checkpoint only resets the pre-reader transfer stage.")
	for well in state.wells:
		well.transferVolumeUl = null
		well.tipId = null
		well.sourceRole = null
		well.contributionsUl.clear()
		well.carryoverFactor = 1.0
		well.rawReading = null
		well.backgroundAdjusted = null
		well.relativeToVehicle = null
	state.sourceRemainingVolumeUl = {"Blank":250.0, "Vehicle control":250.0, "Fictional treatment":250.0}
	state.tipAttached = false
	state.tipContaminationRole = null
	state.selectedSource = null
	state.loadedSource = null
	state.boundDestination = null
	state.pipetteStage = "ReadyInAir"
	state.aspiratedVolumeUl = 0.0
	state.carryoverRisk = false
	state.selectedVolumeUl = null
	state.plateLoaded = false
	state.readerConfigured = false
	state.correctOrientation = false
	state.discardedVolumeUl = 0.0
	for issue in state.issues:
		if issue.recoverable and not issue.resolved:
			issue.resolved = true
			issue.message += " Resolved by Guided Practice transfer-checkpoint retry; retained in attempt lineage."
	state.attemptNumber += 1
	return _yes("Started a new Guided transfer checkpoint while preserving the earlier error ledger.")
func _plate(orientation: String) -> Dictionary:
	if state.pipetteStage != "ReadyInAir": return _no("Complete the active pipette cycle before loading the plate.")
	state.plateLoaded=true; state.correctOrientation=orientation.to_lower()=="correct"
	if not state.correctOrientation: _issue("PlateOrientation", "Plate was loaded with an incorrect orientation in the training reader.", true, true)
	else: _resolve_issue("PlateOrientation")
	return _yes("Plate loaded with the A1 corner in the illustrated correct position." if state.correctOrientation else "Incorrect orientation recorded for debrief.")
func _reader(mode: String) -> Dictionary:
	state.readerConfigured=mode.to_lower()==str(scenario.readerMode).to_lower()
	if not state.readerConfigured: _issue("ReaderMode", "Reader was configured as %s, not %s." % [mode if not mode.is_empty() else "unspecified",scenario.readerMode], true, true)
	else: _resolve_issue("ReaderMode")
	return _yes("Reader configured for the scenario’s luminescence mode." if state.readerConfigured else "Reader configuration mismatch recorded for debrief.")
func _run() -> Dictionary:
	if state.pipetteStage != "ReadyInAir": return _no("Complete the active pipette cycle before running the reader.")
	if not state.plateLoaded or not state.readerConfigured: return _no("Load the plate and configure the reader before running it.")
	for well in state.wells: well.rawReading=_decimal(maxf(0.0,_source_signal(well)*_factor(well)+_noise(int(scenario.seed)+_well_index(well.well)*7919)), 1)
	var blanks:Array=[]; for well in state.wells: if well.role=="Blank": blanks.append(well.rawReading)
	var background:float=_mean(blanks); var vehicles:Array=[]; for well in state.wells: well.backgroundAdjusted=_decimal(float(well.rawReading)-background,12); if well.role=="Vehicle control": vehicles.append(well.backgroundAdjusted)
	var vehicle:float=_mean(vehicles); for well in state.wells: well.relativeToVehicle=_decimal(float(well.backgroundAdjusted)/vehicle*100.0,12) if vehicle>0.0 else null
	state.readerRan=true; if state.wells.any(func(w): return w.transferVolumeUl==null): _issue("MissingControl", "Required samples or controls were not prepared. The partial plate cannot support a treatment comparison.", true, false)
	if _replicates().any(func(r): return r.role != "Blank" and r.cvPercent != null and float(r.cvPercent) > float(rules.maxReplicateCvPercent)): _issue("ReplicateVariation", "Technical replicate spread exceeds this scenario's illustrative QC threshold.", true, false)
	state.controlsValid=_valid(); state.canSupportConclusion=state.controlsValid and _ready() and not _critical()
	# Without a valid blank and vehicle reference the percentage has no sound basis, so it is withheld.
	if not state.controlsValid:
		for well in state.wells: well.relativeToVehicle = null
	return _yes("Fictional readings generated from the versioned scenario seed." if state.controlsValid else "Fictional readings generated from the versioned scenario seed. The blank or vehicle reference is not valid, so no percentage of vehicle is calculated.")
func _review() -> Dictionary:
	if not state.readerRan: return _no("Run the reader before reviewing results.")
	state.resultsReviewed=true
	return _yes("Controls pass the fictional model’s validity checks." if state.canSupportConclusion else "The model marks this run as unsuitable for a supported conclusion: " + "; ".join(PackedStringArray(_support_problems())) + ".")
func _decide() -> Dictionary:
	if not state.resultsReviewed: return _no("Review the results before recording a conclusion.")
	if not state.canSupportConclusion and state.mode == "GuidedPractice":
		return _no("This run cannot support a conclusion: " + "; ".join(PackedStringArray(_support_problems())) + ". Escalate it for review instead.")
	if not state.canSupportConclusion:
		state.interpretationDecision = "unsupported-claim"
		_issue("UnsupportedConclusion", "A supported conclusion was selected despite invalid controls or workflow conditions.", true, true)
		return _yes("Unsupported conclusion recorded for debrief. Escalation remains available.")
	state.interpretationDecision = "supported"
	return _yes("Supported fictional observation recorded.")
func _escalate() -> Dictionary:
	if not state.readerRan: return _no("Escalation is available after a measurement or documented invalid condition.")
	state.escalated=true
	return _yes("Run escalated for review despite passing the fictional QC checks; no conclusion will be claimed." if state.canSupportConclusion else "Invalid run escalated and preserved in the handoff record.")
func _answer_check(target: Variant) -> Dictionary:
	if not state.resultsReviewed: return _no("Review the results before answering the check questions.")
	var parts := str(target if target != null else "").split(":", true, 1)
	var check := _check_by_id(parts[0])
	if check.is_empty() or parts.size() != 2: return _no("Choose one of the listed check questions.")
	if check.stage == "debrief" and state.phase != "Complete": return _no("These questions open after the debrief.")
	for answer in state.checkAnswers:
		if answer.questionId == check.id: return _no("That question already has a recorded answer.")
	var chosen: Variant = null
	var best := ""
	for option in check.options:
		if option.id == parts[1]: chosen = option
		if option.id == check.correctOptionId: best = str(option.text)
	if chosen == null: return _no("Choose one of the listed answers.")
	var correct: bool = chosen.id == check.correctOptionId
	state.checkAnswers.append({"questionId":check.id,"optionId":chosen.id,"correct":correct})
	return _yes("Correct. Your answer is recorded." if correct else "Not quite. Your answer is recorded. The best answer: " + best + ".")
func _check_by_id(id: String) -> Dictionary:
	for check in rules.get("comprehensionChecks", []):
		if check.id == id: return check
	return {}
# Plain-language reasons a run cannot support a conclusion, in a fixed order.
func _support_problems() -> Array:
	var problems: Array = []
	var controls: Array = []
	var treatments: Array = []
	for well in state.wells:
		if well.role in ["Blank", "Vehicle control"]: controls.append(well)
		elif well.role == "Fictional treatment": treatments.append(well)
	var wrong_volume := func(w): return absf((0.0 if w.transferVolumeUl == null else float(w.transferVolumeUl)) - float(scenario.toyTransferVolumeUl)) >= .01
	var wrong_liquid := func(w): return w.sourceRole != null and w.sourceRole != w.role
	if not state.correctOrientation: problems.append("the plate orientation is incorrect")
	if controls.any(wrong_volume): problems.append("a blank or vehicle well is missing or has the wrong volume")
	if controls.any(wrong_liquid): problems.append("a blank or vehicle well received the wrong liquid")
	if state.readerRan:
		var vehicles: Array = []
		for well in controls:
			if well.role == "Vehicle control": vehicles.append(0.0 if well.backgroundAdjusted == null else float(well.backgroundAdjusted))
		if _mean(vehicles) < float(rules.minimumReferenceAdjustedSignal): problems.append("the vehicle reference signal is too low to compare against")
	if state.issues.any(func(i): return not i.resolved and ["WrongWell", "Mislabel", "SampleIdentityMismatch", "TransferBeforeTraceability"].has(i.code)): problems.append("a traceability issue is unresolved")
	if treatments.any(wrong_volume): problems.append("a treatment well is missing or has the wrong volume")
	if treatments.any(wrong_liquid): problems.append("a treatment well received the wrong liquid")
	var flags: Array = []
	for issue in state.issues:
		if issue.critical and not issue.resolved and not ["WrongWell", "Mislabel", "SampleIdentityMismatch", "TransferBeforeTraceability", "PlateOrientation", "MissingControl"].has(issue.code) and not flags.has(FLAG_LABELS.get(issue.code, issue.code)): flags.append(FLAG_LABELS.get(issue.code, issue.code))
	if not flags.is_empty(): problems.append("unresolved handling flags: " + ", ".join(PackedStringArray(flags)))
	if problems.is_empty(): problems.append("the controls or workflow conditions do not pass the model's checks")
	return problems
func snapshot() -> Dictionary: return state.duplicate(true)
func save() -> String: return JSON.stringify({"webEnvelope":WEB_ENVELOPE,"levelId":LEVEL_ID,"formatVersion":SAVE_FORMAT,"modelVersion":MODEL_VERSION,"scenario":scenario,"rules":rules,"sources":sources,"snapshot":state}, "", true, true)
func restore(text: String) -> bool:
	var save: Variant=JSON.parse_string(text)
	if typeof(save)!=TYPE_DICTIONARY or save.get("webEnvelope")!=WEB_ENVELOPE or save.get("levelId")!=LEVEL_ID or save.get("formatVersion")!=SAVE_FORMAT or not RESTORABLE_MODEL_VERSIONS.has(save.get("modelVersion")) or typeof(save.get("scenario"))!=TYPE_DICTIONARY or typeof(save.get("rules"))!=TYPE_DICTIONARY or typeof(save.get("sources"))!=TYPE_DICTIONARY or typeof(save.get("snapshot"))!=TYPE_DICTIONARY: return false
	var saved:Dictionary=save
	var snapshot_saved:Dictionary=saved.snapshot
	if not snapshot_saved.has("checkAnswers"): snapshot_saved.checkAnswers = [] # 1.1.0 saves predate check answers
	if not _valid_snapshot_shape(snapshot_saved, saved.scenario): return false
	var candidate:=Level1Simulation.new()
	candidate.scenario=saved.scenario.duplicate(true); candidate.rules=saved.rules.duplicate(true); candidate.sources=saved.sources.duplicate(true)
	if not candidate._definitions_valid(): return false
	candidate.start(snapshot_saved.mode); candidate.state.attemptId=snapshot_saved.attemptId
	for entry in snapshot_saved.ledger:
		if typeof(entry)!=TYPE_DICTIONARY or not candidate._action_names().has(entry.get("action")): return false
		candidate.submit(entry.action, entry.get("target"), entry.get("value"))
	if not _same_tree(candidate.state,snapshot_saved): return false
	scenario=candidate.scenario; rules=candidate.rules; sources=candidate.sources; state=candidate.state; lessons=candidate.lessons
	return true
func build_report() -> Dictionary:
	var valid := _valid()
	var support: bool = valid and _ready() and not _critical() and state.readerRan
	var conclusion := "No conclusion was recorded. A valid measurement still needs an explicit, appropriately limited interpretation."
	if state.escalated: conclusion = "Run was escalated for review. No biological conclusion is claimed by this attempt."
	elif state.interpretationDecision == "supported" and support: conclusion = "In this fictional training model, the treatment wells have a lower normalized luminescence signal than the vehicle controls. This supports an observed viability-associated difference only."
	elif state.interpretationDecision == "unsupported-claim": conclusion = "An unsupported conclusion was attempted; no biological conclusion is claimed by this attempt."
	return {"modelVersion":MODEL_VERSION,"scenarioId":scenario.id,"scenarioSchemaVersion":scenario.schemaVersion,"mode":state.mode,"complete":state.phase=="Complete","controlsValid":valid,"canSupportConclusion":support,"escalated":state.escalated,"attemptId":state.attemptId,"attemptNumber":state.attemptNumber,"scenarioSeed":scenario.seed,"rulesSchemaVersion":rules.schemaVersion,"sourcesSchemaVersion":sources.schemaVersion,"interpretationDecision":state.interpretationDecision,"conclusion":conclusion,"categoryStatus":_categories(valid,support),"wells":state.wells.duplicate(true),"issues":state.issues.duplicate(true),"ledger":state.ledger.duplicate(true),"sources":sources.entries.duplicate(true),"replicates":_replicates(),"lessons":lessons.duplicate(true) if state.phase=="Complete" else [],"checkAnswers":state.checkAnswers.duplicate(true),"scientificLimit":"ATP-associated luminescence is a viability proxy and does not establish a cell-death mechanism. Stored source and well quantities are nominal training amounts, not physical volume measurements or calibration results."}
func _categories(valid:bool,support:bool)->Dictionary:
	return {"Preparation":"Complete" if state.ppeWorn and state.benchDisinfected and state.materialsChecked else "Incomplete","Traceability":"Complete" if state.labelsVerified and state.plateMapReviewed and state.correctOrientation and not state.issues.any(func(i):return not i.resolved and ["Mislabel","SampleIdentityMismatch","WrongWell"].has(i.code)) else "Needs review","Handling":"Complete" if _ready() and not _critical() else "Needs review","Interpretation":"Complete" if state.resultsReviewed and ((state.interpretationDecision=="supported" and support) or state.escalated) else "Needs review","Closeout":"Complete" if state.wasteSorted and state.benchCleanedAtCloseout and state.handoffRecorded else "Incomplete"}
func _replicates()->Array:
	var items:Array=[]
	for role in ["Blank","Vehicle control","Fictional treatment"]:
		var values:Array=[]
		for well in state.wells:
			if well.role==role and well.rawReading!=null: values.append(float(well.rawReading))
		if values.size()==0: continue
		var mean:=_mean(values); var variance:=0.0
		if values.size()>1: for value in values: variance+=pow(float(value)-mean,2); variance/=values.size()-1
		var sd:=sqrt(variance)
		items.append({"role":role,"count":values.size(),"mean":mean,"standardDeviation":sd,"cvPercent":sd/mean*100.0 if mean>0.0 else null})
	return items
func _valid() -> bool:
	var controls:Array=[]
	for well in state.wells:
		if well.role in ["Blank", "Vehicle control"]: controls.append(well)
	if controls.size() < 4 or not state.readerRan or not state.correctOrientation or not state.readerConfigured: return false
	for well in controls:
		if absf((0.0 if well.transferVolumeUl == null else float(well.transferVolumeUl))-float(scenario.toyTransferVolumeUl)) >= .01 or well.sourceRole != well.role: return false
	var vehicles:Array=[]
	for well in controls:
		if well.role == "Vehicle control": vehicles.append(0.0 if well.backgroundAdjusted == null else float(well.backgroundAdjusted))
	if _mean(vehicles) < float(rules.minimumReferenceAdjustedSignal): return false
	return not state.issues.any(func(i): return not i.resolved and ["WrongWell", "Mislabel", "SampleIdentityMismatch", "TransferBeforeTraceability", "PlateOrientation", "ReaderMode"].has(i.code))
func _ready() -> bool:return state.wells.size() == scenario.activeWells.size() and state.wells.all(func(w):return absf((0.0 if w.transferVolumeUl == null else float(w.transferVolumeUl))-float(scenario.toyTransferVolumeUl))<.01 and w.rawReading!=null and w.sourceRole==w.role)
func _critical() -> bool:return state.issues.any(func(i):return i.critical and not i.resolved)
func _update_phase() -> void:
	if not (state.ppeWorn and state.benchDisinfected and state.materialsChecked):
		state.phase = "Preparation"
	elif not (state.labelsVerified and state.plateMapReviewed):
		state.phase = "Organization"
	elif not state.readerRan:
		state.phase = "Transfers"
	elif not (state.resultsReviewed and (state.interpretationDecision == "supported" or state.escalated)):
		state.phase = "Interpretation"
	else:
		state.phase = "Complete" if state.wasteSorted and state.benchCleanedAtCloseout and state.handoffRecorded else "Closeout"
func _issue(code:String,message:String,critical:bool,recoverable:bool,target:Variant=null)->void:
	if not state.issues.any(func(i):return i.code==code and i.target==target and not i.resolved):state.issues.append({"code":code,"message":message,"critical":critical and (rules.criticalErrors.has(code) or code=="ReplicateVariation"),"recoverable":recoverable,"resolved":false,"target":target})
func _resolve_issue(code:String,target:Variant=null)->void:
	for issue in state.issues:
		if issue.code==code and issue.target==target and issue.recoverable and not issue.resolved:
			issue.resolved=true
			issue.message += " Corrected before acquisition; retained as an audit note."
func _factor(w:Dictionary)->float:return .02 if w.transferVolumeUl==null else float(w.transferVolumeUl)/float(scenario.toyTransferVolumeUl)*float(w.carryoverFactor)
func _source_signal(well:Dictionary)->float:
	if well.contributionsUl.is_empty() or (well.contributionsUl.size()==1 and well.contributionsUl.has(well.role)): return float(well.expectedSignal)
	var total:=0.0; var weighted:=0.0
	for role in well.contributionsUl:
		var role_values:Array=[]
		for defined in scenario.activeWells: if defined.role==role: role_values.append(float(defined.expectedSignal))
		var contribution:=float(well.contributionsUl[role]); total+=contribution; weighted+=contribution*_mean(role_values)
	return weighted/total if total>0.0 else float(well.expectedSignal)
func _round_to_even(value:float,digits:int)->float:
	var scale:float=pow(10.0,digits); var scaled:float=value*scale; var lower:float=floor(scaled); var fraction:float=scaled-lower
	if is_equal_approx(fraction,0.5): return (lower if int(lower)%2==0 else lower+1.0)/scale
	return floor(scaled+0.5)/scale
func _decimal(value:float,digits:int)->float:return ("%0.*f" % [digits,value]).to_float()
func _noise(seed: int) -> float:
	# Port of .NET 8.0.31 System.Random.Net5CompatImpl seeding/sample behavior; MIT attribution:
	# https://github.com/dotnet/runtime/blob/v8.0.31/src/libraries/System.Private.CoreLib/src/System/Random.Net5CompatImpl.cs
	# This preserves the reference model's deterministic fictional readings without using Godot RNG.
	const MBIG := 2147483647
	var subtraction := 2147483647 if seed == -2147483648 else absi(seed)
	var mj := 161803398 - subtraction
	if mj < 0: mj += MBIG
	var seed_array: Array = []; seed_array.resize(56); seed_array[55] = mj
	var mk := 1
	for index in range(1, 55):
		var ii := (21 * index) % 55
		seed_array[ii] = mk
		mk = mj - mk
		if mk < 0: mk += MBIG
		mj = seed_array[ii]
	for pass_index in range(1, 5):
		for index in range(1, 56):
			seed_array[index] -= seed_array[1 + (index + 30) % 55]
			if seed_array[index] < 0: seed_array[index] += MBIG
	var inext := 1
	var inextp := 22
	var sample: int = int(seed_array[inext]) - int(seed_array[inextp])
	if sample == MBIG: sample -= 1
	if sample < 0: sample += MBIG
	return (float(sample) / float(MBIG) - .5) * 18.0
func _well(id:String)->Variant:
	for well in state.wells:
		if well.well.to_upper()==id.to_upper(): return well
	return null
func _well_index(id:String)->int:return (id.unicode_at(0)-65)*12+id.substr(1).to_int()
func _mean(values:Array)->float:
	var total:=0.0
	for value in values: total+=float(value)
	return total/values.size() if values.size()>0 else 0.0
func _format_quantity(value: Variant) -> String:
	var number := float(value)
	return str(int(number)) if is_equal_approx(number, floor(number)) else "%0.1f" % number
func _yes(message:String)->Dictionary:return {"accepted":true,"message":message}
func _no(message:String)->Dictionary:return {"accepted":false,"message":message}
func _result(ok:bool,message:String)->Dictionary:return {"accepted":ok,"message":message,"snapshot":snapshot()}
func _load(path:String)->Dictionary:var parsed:Variant=JSON.parse_string(FileAccess.get_file_as_string(path));return parsed if typeof(parsed)==TYPE_DICTIONARY else {}
func _id()->String:return "%08x%08x%08x%08x" % [randi(),randi(),randi(),randi()]

func _action_names() -> Array:
	return ["WearPpe","DisinfectBench","CheckMaterials","VerifyLabels","ReviewPlateMap","AttachTip","EjectTip","ChangeTip","SetVolume","SelectSource","PressFirstStop","MoveToSource","ReleaseSlow","ReleaseFast","MoveToDestination","PressSecondStop","WithdrawAndRelease","Aspirate","Dispense","MislabelSelectedWell","CorrectLabel","RetryTransferCheckpoint","LoadPlate","ConfigureReader","RunReader","ReviewResults","DecideSupportedConclusion","EscalateInvalidRun","SortWaste","CleanBench","RecordHandoff","AnswerCheck"]

func _definitions_valid() -> bool:
	if scenario.get("schemaVersion") != 1 or str(scenario.get("id", "")).strip_edges().is_empty() or str(scenario.get("readerMode", "")).strip_edges().is_empty():
		return false
	if not is_finite(float(scenario.get("toyTransferVolumeUl", 0.0))) or float(scenario.get("toyTransferVolumeUl", 0.0)) <= 0.0:
		return false
	if typeof(scenario.get("activeWells")) != TYPE_ARRAY or scenario.activeWells.size() < 6:
		return false
	var wells: Dictionary = {}
	var roles: Dictionary = {"Blank": 0, "Vehicle control": 0, "Fictional treatment": 0}
	for well in scenario.activeWells:
		if typeof(well) != TYPE_DICTIONARY or not _valid_well_id(str(well.get("well", ""))) or str(well.get("role", "")).strip_edges().is_empty():
			return false
		if not is_finite(float(well.get("expectedSignal", 0.0))) or float(well.get("expectedSignal", 0.0)) < 0.0:
			return false
		var canonical_well := str(well.well).to_upper()
		if wells.has(canonical_well):
			return false
		wells[canonical_well] = true
		if roles.has(well.role):
			roles[well.role] += 1
	if int(roles.Blank) < 2 or int(roles["Vehicle control"]) < 2 or int(roles["Fictional treatment"]) < 2:
		return false
	if rules.get("schemaVersion") != 1 or rules.get("illustrativeOnly") != true:
		return false
	if typeof(rules.get("criticalErrors")) != TYPE_ARRAY or typeof(rules.get("assessmentCategories")) != TYPE_ARRAY:
		return false
	if not is_finite(float(rules.get("maxReplicateCvPercent", 0.0))) or float(rules.get("maxReplicateCvPercent", 0.0)) <= 0.0:
		return false
	if not is_finite(float(rules.get("minimumReferenceAdjustedSignal", 0.0))) or float(rules.get("minimumReferenceAdjustedSignal", 0.0)) <= 0.0:
		return false
	if not _checks_valid(rules.get("comprehensionChecks", [])):
		return false
	if sources.get("schemaVersion") != 1 or typeof(sources.get("entries")) != TYPE_ARRAY:
		return false
	for source in sources.entries:
		if typeof(source) != TYPE_DICTIONARY or not _valid_https_url(str(source.get("url", ""))):
			return false
	return true

func _checks_valid(checks: Variant) -> bool:
	if typeof(checks) != TYPE_ARRAY: return false
	var ids: Dictionary = {}
	for check in checks:
		if typeof(check) != TYPE_DICTIONARY or str(check.get("id", "")).is_empty() or ids.has(check.id) or not ["follow-up", "debrief"].has(check.get("stage")): return false
		if str(check.get("prompt", "")).is_empty() or str(check.get("explanation", "")).is_empty() or typeof(check.get("options")) != TYPE_ARRAY or check.options.size() < 2: return false
		ids[check.id] = true
		var option_ids: Dictionary = {}
		for option in check.options:
			if typeof(option) != TYPE_DICTIONARY or str(option.get("id", "")).is_empty() or str(option.get("text", "")).is_empty() or option_ids.has(option.id): return false
			option_ids[option.id] = true
		if not option_ids.has(check.get("correctOptionId")): return false
	return true

func _valid_well_id(value: String) -> bool:
	if value.length() < 2 or value.length() > 3:
		return false
	var row := value.unicode_at(0)
	if row < 65 or row > 72:
		return false
	var column_text := value.substr(1)
	if not column_text.is_valid_int():
		return false
	var column := column_text.to_int()
	return column >= 1 and column <= 12

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

func _valid_snapshot_shape(candidate:Variant,candidate_scenario:Variant)->bool:
	if typeof(candidate)!=TYPE_DICTIONARY or typeof(candidate_scenario)!=TYPE_DICTIONARY: return false
	if not candidate.get("started",false) or not _valid_id(str(candidate.get("attemptId",""))) or not ["GuidedPractice","Assessment"].has(candidate.get("mode")) or not ["Welcome","Preparation","Organization","Transfers","Measurement","Interpretation","Closeout","Complete"].has(candidate.get("phase")): return false
	if typeof(candidate.get("checkAnswers"))!=TYPE_ARRAY or typeof(candidate.get("wells"))!=TYPE_ARRAY or typeof(candidate.get("issues"))!=TYPE_ARRAY or typeof(candidate.get("ledger"))!=TYPE_ARRAY or typeof(candidate.get("sourceRemainingVolumeUl"))!=TYPE_DICTIONARY: return false
	for role in ["Blank","Vehicle control","Fictional treatment"]:
		if not candidate.sourceRemainingVolumeUl.has(role) or not is_finite(float(candidate.sourceRemainingVolumeUl[role])) or float(candidate.sourceRemainingVolumeUl[role])<0.0 or float(candidate.sourceRemainingVolumeUl[role])>250.0: return false
	for well in candidate.wells:
		if typeof(well)!=TYPE_DICTIONARY or typeof(well.get("contributionsUl"))!=TYPE_DICTIONARY or (well.get("transferVolumeUl")!=null and (not is_finite(float(well.transferVolumeUl)) or float(well.transferVolumeUl)<0.0)): return false
	if not is_finite(float(candidate.get("aspiratedVolumeUl",0.0))) or float(candidate.get("aspiratedVolumeUl",0.0))<0.0 or float(candidate.get("aspiratedVolumeUl",0.0))>250.0: return false
	if candidate.get("selectedVolumeUl")!=null and (not is_finite(float(candidate.selectedVolumeUl)) or float(candidate.selectedVolumeUl)<=0.0 or float(candidate.selectedVolumeUl)>250.0): return false
	if candidate.get("selectedSource")!=null and not candidate.sourceRemainingVolumeUl.has(candidate.selectedSource): return false
	if candidate.get("loadedSource")!=null and not candidate.sourceRemainingVolumeUl.has(candidate.loadedSource): return false
	if candidate.get("boundDestination")!=null and _well_from_scenario(candidate_scenario,candidate.boundDestination).is_empty(): return false
	if candidate.pipetteStage=="ReadyInAir" and (candidate.loadedSource!=null or candidate.boundDestination!=null or float(candidate.aspiratedVolumeUl)!=0.0): return false
	if candidate.pipetteStage in ["LoadedAtSource","LoadedAtDestination"] and (candidate.loadedSource==null or float(candidate.aspiratedVolumeUl)<=0.0): return false
	if candidate.pipetteStage in ["FirstStopAtDestination","SecondStopAtDestination"] and (candidate.loadedSource==null or candidate.boundDestination==null or float(candidate.aspiratedVolumeUl)!=0.0): return false
	return true

func _well_from_scenario(candidate_scenario:Dictionary,well_id:Variant)->Dictionary:
	for well in candidate_scenario.get("activeWells",[]):
		if str(well.get("well","")).to_upper()==str(well_id).to_upper(): return well
	return {}

func _valid_id(value:String)->bool:return value.length()==32 and value.is_valid_hex_number()

func _same_tree(left:Variant,right:Variant)->bool:
	if left==null or right==null:return left==right
	if typeof(left) in [TYPE_INT,TYPE_FLOAT] or typeof(right) in [TYPE_INT,TYPE_FLOAT]:return float(left)==float(right)
	if typeof(left)==TYPE_DICTIONARY and typeof(right)==TYPE_DICTIONARY:
		if left.size()!=right.size():return false
		for key in left:
			if not right.has(key) or not _same_tree(left[key],right[key]):return false
		return true
	if typeof(left)==TYPE_ARRAY and typeof(right)==TYPE_ARRAY:
		if left.size()!=right.size():return false
		for index in left.size():
			if not _same_tree(left[index],right[index]):return false
		return true
	return left==right

func _first_difference(left:Variant,right:Variant,path := "state")->String:
	if left==null or right==null:return path if left!=right else ""
	if typeof(left)==TYPE_DICTIONARY and typeof(right)==TYPE_DICTIONARY:
		for key in left:
			if not right.has(key):return path+"."+str(key)+" missing right"
			var child:String=_first_difference(left[key],right[key],path+"."+str(key))
			if not child.is_empty():return child
		for key in right:
			if not left.has(key):return path+"."+str(key)+" missing left"
		return ""
	if typeof(left)==TYPE_ARRAY and typeof(right)==TYPE_ARRAY:
		if left.size()!=right.size():return path+" length"
		for index in left.size():
			var child:String=_first_difference(left[index],right[index],path+"[%s]"%index)
			if not child.is_empty():return child
		return ""
	if typeof(left) in [TYPE_INT,TYPE_FLOAT] or typeof(right) in [TYPE_INT,TYPE_FLOAT]:return "" if float(left)==float(right) else path+" left=%0.17f right=%0.17f" % [float(left),float(right)]
	return "" if left==right else path

func _science(action:String,accepted:bool,_target:Variant,_value:Variant)->Dictionary:
	if not accepted:return {"title":"Check the workflow","lesson":"The simulation keeps actions ordered so that the next observation has context.","why":"In real work, pause and follow the local procedure when the next safe step is unclear."}
	if action == "PressFirstStop" and state.pipetteStage == "FirstStopAtDestination":
		return {"title":"Nominal delivery at first stop","lesson":"In this training model, the first stop at the destination records the nominal set amount immediately.","why":"The second stop clears the modeled residue state; it does not add a fabricated extra volume."}
	if action == "AnswerCheck":
		return {"title":"Check your understanding","lesson":str(_check_by_id(str(_target if _target != null else "").split(":", true, 1)[0]).get("explanation", "")),"why":"Your first answer to each question is kept in the attempt record."}
	if action == "DecideSupportedConclusion" and not state.canSupportConclusion:
		return {"title":"Know when not to conclude","lesson":"An experiment can produce numbers yet still be invalid for the question being asked.","why":"Recognizing that limit protects later decisions from an attractive but unsupported result."}
	var entries:Dictionary={
		"WearPpe":{"title":"Preparation protects people and data","lesson":"Personal protective equipment is selected before handling materials. It reduces exposure risk and helps keep the workspace orderly.","why":"Skipping preparation can put people at risk and leaves no clear safety record."},
		"DisinfectBench":{"title":"Clean workspace","lesson":"A prepared bench separates the training task from residues or clutter left by earlier work.","why":"Contamination and mix-ups can change a measurement before the instrument ever sees the plate."},
		"CheckMaterials":{"title":"Instrument readiness","lesson":"Checking materials and reader readiness before a run is a traceability step.","why":"Discovering a missing material midway can create an undocumented deviation."},
		"VerifyLabels":{"title":"Traceability","lesson":"A sample label links a physical item to the plate map and later result.","why":"A label error can make a perfectly measured signal belong to the wrong sample."},
		"ReviewPlateMap":{"title":"Controls and replicates","lesson":"Blank wells estimate background; vehicle control wells provide a reference; replicate wells reveal variation.","why":"Without valid controls, a treatment-looking signal has no supported comparison."},
		"AttachTip":{"title":"Pipette tip history","lesson":"A tip is the disposable part that contacts liquid. This demo records it to teach traceability, not a real contamination policy.","why":"A fresh tip can help prevent material carryover between distinct samples. Local SOPs define the actual rule."},
		"EjectTip":{"title":"Separate tip disposal","lesson":"The model separates an empty-tip ejection from liquid movement so a replacement cannot erase an unfinished cycle.","why":"This is an illustrative interaction guard. Local procedures define real disposal and tip-use rules."},
		"SetVolume":{"title":"Accuracy and precision","lesson":"Accuracy means closeness to the intended amount; precision means repeated amounts are close to each other. The app uses a toy 50 uL target only to make error propagation visible.","why":"A volume mismatch changes the modeled signal. Real effects depend on the assay, liquids, equipment, and local procedure."},
		"SelectSource":{"title":"Source identity","lesson":"Each source in this model has a finite toy inventory and planned destination wells.","why":"Keeping source identity attached to each transfer makes a sample swap visible rather than treating every liquid as interchangeable."},
		"PressFirstStop":{"title":"Forward aspiration sequence","lesson":"The illustrated forward sequence begins with the first stop in air before the tip enters the source.","why":"The order creates a clear learning record; this simplified scene does not model immersion depth, timing, or calibration."},
		"MoveToSource":{"title":"Source position","lesson":"The source vial position is illustrative; the cap is visibly open so the scene does not imply a blocked immersion.","why":"The app records the documented order, not a physical depth, dwell time, or calibration claim."},
		"ReleaseSlow":{"title":"Nominal source quantity","lesson":"A slow release is recorded while the illustrated tip is in the selected source. The quantity is nominal bookkeeping, not a physical calibration claim.","why":"Nominal conservation makes an impossible sequence visible without claiming a measured real-world volume."},
		"ReleaseFast":{"title":"Nominal source quantity","lesson":"A fast release is recorded as a qualitative training condition, not a percent-error or calibration calculation.","why":"Nominal conservation makes an impossible sequence visible without claiming a measured real-world volume."},
		"MoveToDestination":{"title":"Destination binding","lesson":"The selected well is frozen when the loaded tip is positioned at the plate.","why":"Changing the visible well selection later cannot retarget liquid already positioned for this modeled transfer."},
		"PressSecondStop":{"title":"Clearing without inventing quantity","lesson":"The second stop records a completed clearing step in the forward sequence.","why":"The model does not claim a separate measured blow-out volume or calibration result."},
		"WithdrawAndRelease":{"title":"Finish the forward cycle","lesson":"The illustrated sequence keeps the plunger pressed during withdrawal and releases only after the tip is back in air.","why":"This is a documented sequence model, not a substitute for a local SOP or hands-on assessment."},
		"MislabelSelectedWell":{"title":"Labels create provenance","lesson":"The same number can tell a different story if its identity link is wrong.","why":"A label correction before acquisition is auditable; an unresolved label mismatch blocks interpretation."},
		"CorrectLabel":{"title":"Correcting without erasing","lesson":"The training ledger keeps the earlier mismatch and marks it resolved before measurement.","why":"Auditable correction preserves what happened while allowing a correctly identified pre-acquisition run to proceed."},
		"RetryTransferCheckpoint":{"title":"Recovery with an audit trail","lesson":"Practice can reset the pre-reader transfer work without pretending the first attempt never happened.","why":"This separates learning recovery from silently overwriting an error record."},
		"LoadPlate":{"title":"Plate orientation","lesson":"A plate position connects a physical grid to the software’s grid. Orientation is a traceability check.","why":"A rotated plate can assign a valid reading to the wrong planned sample."},
		"ConfigureReader":{"title":"What the reader observes","lesson":"A luminescence reader measures light from the assay reaction. It does not directly watch cells dying.","why":"Instrument configuration determines what the numbers mean. A number without its measurement context is hard to interpret."},
		"RunReader":{"title":"Signal, background, and comparison","lesson":"Raw light includes background. The app subtracts the average blank signal, then expresses each active well relative to the vehicle-control average.","why":"These calculations organize a comparison; they do not tell you why a signal changed or establish a real effect."},
		"ReviewResults":{"title":"Interpretation starts with controls","lesson":"Replicate spread gives context for a mean, and controls tell whether a comparison is interpretable.","why":"A lower treatment signal is an observation. It becomes a supported comparison only when the necessary controls and traceability conditions hold."},
		"DecideSupportedConclusion":{"title":"Claim only what the data support","lesson":"The valid run supports a lower viability-associated signal in fictional treatment wells relative to fictional vehicle controls.","why":"The model does not identify a mechanism, predict a real drug response, or prove that cells died."},
		"EscalateInvalidRun":{"title":"Escalation is scientific judgment","lesson":"Escalation documents that a control or setup condition prevents a supported conclusion.","why":"Stopping an invalid run is often better practice than forcing an interpretation from unreliable evidence."},
		"SortWaste":{"title":"Closeout is part of the experiment","lesson":"Waste is sorted according to the local procedure; the simulation does not teach a real waste classification.","why":"A correct measurement still creates risk if materials are left for the next person without a clear disposition."},
		"CleanBench":{"title":"Leave a usable workspace","lesson":"Cleanup ends the task and prepares the shared area for the next user.","why":"Closeout makes deviations visible instead of silently passing them forward."},
		"RecordHandoff":{"title":"Scientific memory","lesson":"A handoff records what happened, including deviations and any escalation.","why":"Without documentation, a later reviewer cannot tell whether an unexpected result was experimental biology or a process problem."}
	}
	return entries.get(action,{})
