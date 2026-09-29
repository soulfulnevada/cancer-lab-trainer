extends SceneTree

const BrowserMain := preload("res://scripts/web_main.gd")

func _init() -> void:
	var app := BrowserMain.new()
	app.level = "level2"
	var report := {
		"modelVersion":"1.0.0", "saveFormatVersion":1, "mode":"GuidedPractice", "attemptId":"a", "scenarioSeed":1,
		"retryOfAttemptId":"parent", "plates":[{"id":"P1","label":"planned","tubeLabel":"T","medium":"LB","growth":"Present","fluorescence":"Detected"}],
		"caseObservations":[{"id":"P1","label":"case","growth":"Present","fluorescence":"NoneDetected"}],
		"tubeLabels":{"T":"T"}, "tubeContents":{"T":"DNA"}, "caseSetup":{"plateAssignments":{"P1":"T|LB"}},
		"predictions":[{"plateId":"P1","originalGrowthPrediction":"Present","originalFluorescencePrediction":"Detected","revisions":["growth:NotSure"]}],
		"mainSelectionClaimSupported":true, "mainExpressionClaimSupported":false,
		"claimReasons":{"main-selection":"limited","main-expression":"withheld"}, "hintsUsed":["pbad-arac"],
		"explanationChoices":["P1:green-consistent-gfp"], "ledger":[{"type":"ViewPlate","target":"P1","value":"normal"}],
		"diagnosticDecisions":["selection-supported:control-pattern"], "evidenceCardsViewed":["setup-record"],
		"tubeSetupCorrections":["T:Buffer->DNA"], "plateAssignmentCorrections":[], "prelockPredictionCorrections":[],
		"diagnosticCase":{"title":"Case A","finalFictionalCause":"fictional cause","selectionClaimSupported":true,"expressionClaimSupported":false,"selectionClaimReason":"limited","expressionClaimReason":"withheld"},
		"limits":"limit", "sources":[{"id":"unsafe","label":"unsafe","url":"https://example.test/?q=\" onmouseover=\"bad"}]
	}
	var csv := app._csv(report)
	_assert(csv.contains("tube-content") and csv.contains("case-setup") and csv.contains("main-claim") and csv.contains("ledger") and csv.contains("retry_parent=parent"), "CSV must include complete Level 2 record sections")
	_assert(app._csv_cell(-3.8) == "\"-3.8\"", "numeric negatives stay numeric")
	_assert(app._csv_cell("\t=1+1").begins_with("\"'"), "tab-prefixed formulas are protected")
	var html := app._html(report)
	_assert(html.contains("save format 1"), "Level 2 HTML must include its web save format")
	_assert(html.contains("Case observations") and html.contains("Viewed case setup") and html.contains("label T; actual content DNA") and html.contains("pbad-arac") and html.contains("Linked retry parent: parent") and html.contains("Action ledger"), "HTML must include complete case, tube, assistance, retry, and ledger sections")
	_assert(html.contains("&quot;") and not html.contains("href=\"https://example.test/?q=\" onmouseover"), "HTML attributes escape quotes")
	var exported_json: Dictionary = JSON.parse_string(app._json_export(report))
	_assert(exported_json.get("exportEnvelope") == "webExportEnvelope1" and exported_json.get("webProvenance", {}).get("saveFormatVersion") == 1 and exported_json.get("report", {}).get("attemptId") == "a", "JSON download adds outer web provenance without mutating the report")
	app.level = "level1"
	_assert(app._report_save_format({}) == 3, "Level 1 HTML falls back to save format 3 when reports omit persistence metadata")
	print("PASS: production export contract")
	app.free()
	quit()

func _assert(condition: bool, message: String) -> void:
	if not condition:
		push_error(message)
		quit(1)
