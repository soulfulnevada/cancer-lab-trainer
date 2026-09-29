class_name PipettingQualityTeaching
extends RefCounted

# General teaching text only. It never records or evaluates a learner action.
const SOP_SOURCE_ID := "eppendorf-liquid-handling-sop"
const TITLE := "Pipetting quality: accuracy and precision"
const BODY := "Accuracy means how close the transferred volume is to the intended target. Precision means how closely repeated transfers agree with one another. For a fictional 50-unit target, 45, 45, 45 is precise because the transfers agree, but inaccurate because they share the same systematic error: a consistent bias. Nearly identical ATP-associated replicates are not proof of volume accuracy.\n\nA controlled gravimetric check is one way to evaluate transfer quality: it weighs liquid and relates mass to volume using density and appropriate conditions and corrections. This introduces the idea; it does not provide calibration instructions or certify equipment performance.\n\nInconsistent transfers can increase ATP replicate spread. A shared bias can shift the transferred volume or amount across affected wells in one direction. The signal or normalized effect then depends on what was transferred and on the assay; it is not guaranteed to be linear or to move every raw reading uniformly. ATP-associated light is a viability-related metabolic signal, not proof of cell death or a mechanism. The simulation’s pipette, quantities, and effects are illustrative, not physically validated or evidence of real competence."

static func has_source(entries: Variant) -> bool:
	if typeof(entries) != TYPE_ARRAY:
		return false
	for entry in entries:
		if typeof(entry) == TYPE_DICTIONARY and entry.get("id") == SOP_SOURCE_ID:
			return true
	return false

static func optional_why(mode: String, entries: Variant) -> Variant:
	return card() if mode == "GuidedPractice" and has_source(entries) else null

static func debrief_card(entries: Variant) -> Variant:
	return card() if has_source(entries) else null

static func html_card(entries: Variant) -> Variant:
	if not has_source(entries):
		return null
	var paragraphs: Array = []
	for paragraph in BODY.split("\n\n"):
		paragraphs.append("<p>%s</p>" % paragraph.xml_escape())
	return "<h2>%s</h2>%s" % [TITLE.xml_escape(), "".join(paragraphs)]

static func card() -> Dictionary:
	return {"title": TITLE, "body": BODY}
