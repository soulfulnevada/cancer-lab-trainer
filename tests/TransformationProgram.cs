using CancerLabTrainer.Transformation;
using CancerLabTrainer.Ui;
using System.Text.Json;
using System.Text.Json.Serialization;

var tests = new (string Name, Action Run)[]
{
    ("normal plan stays hidden until all predictions lock", PredictionLocking),
    ("normal transformation run reports the four planned qualitative observations", NormalOutcomes),
    ("normal planned controls support both limited claim dimensions", NormalClaims),
    ("incorrect actual tube contents change observations without changing labels", ActualTubeContentsMatter),
    ("assessment defers coaching but preserves action errors", AssessmentFeedbackTiming),
    ("four curated diagnostic cases preserve observation and claim limits", CuratedCases),
    ("saved attempts replay and corrupted attempts remain rejected", SaveRestoreAndCorruption),
    ("prediction dimensions and prelock corrections protect final setup", PredictionDimensionsAndCorrections),
    ("actual control assignments derive separate selection and expression support", ClaimSupportUsesActualControls),
    ("curated case reports retain observations without causal leakage", PrededriefReportRedaction),
    ("linked retries retain their completed predecessor", LinkedRetry),
    ("linked retry provenance restores without its parent file", LinkedRetryRestore),
    ("diagnostic decisions retain history and grade current dimensions", DiagnosticDecisionRubric),
    ("main explanations grade current choice and retain replacements", MainExplanationRubric),
    ("main explanations accept meaningful plate-specific statements", MeaningfulMainExplanations),
    ("detective replay waits for all main explanations", DiagnosticCasePrerequisite),
    ("case D setup distinguishes actual and intended negative-control plates", CaseDSetup),
    ("canonical definitions reject changed outcomes and setup records", CanonicalDefinitionValidation),
    ("exports and deletion remain isolated to level two", StoreAndExports),
    ("case claim display scopes main claims and defers case answers", CaseClaimDisplayScopesAndDefers),
    ("completed Case A HTML scopes main and case claim support", CompletedCaseHtmlScopesClaims)
};

try
{
    foreach (var test in tests) test.Run();
    Console.WriteLine("PASS: all transformation contract tests");
}
catch (Exception error)
{
    Console.Error.WriteLine("FAIL: " + error.Message);
    Environment.ExitCode = 1;
}

static TransformationSimulation NewSimulation() => TransformationSimulation.FromJsonDocuments(
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "transformation-scenario.v1.json")),
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "transformation-lessons.v1.json")),
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "transformation-cases.v1.json")),
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "transformation-rubric.v1.json")),
    File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "transformation-sources.v1.json")));

static void PredictionLocking()
{
    var sim = NewSimulation(); sim.Start(TransformationRunMode.GuidedPractice);
    SetupNormalPlan(sim);
    Assert(sim.Snapshot().Plates.All(p => p.Growth == TransformationGrowth.NotViewed), "actual observations must stay hidden before predictions lock");
    ExpectReject(sim, new(TransformationActionType.ViewPlate, "P1", "normal"));
    LockPredictions(sim);
    Assert(sim.Snapshot().PredictionsLocked, "all four predictions must lock together");
    Assert(sim.Snapshot().Plates.All(p => p.Growth == TransformationGrowth.NotViewed), "locking predictions must not reveal outcomes");
}

static void NormalOutcomes()
{
    var sim = NewSimulation(); sim.Start(TransformationRunMode.GuidedPractice); SetupNormalPlan(sim); LockPredictions(sim); ObserveAll(sim);
    var plates = sim.Snapshot().Plates.ToDictionary(p => p.Id);
    Assert(plates["P1"].Growth == TransformationGrowth.Present && plates["P1"].Fluorescence == TransformationFluorescence.NoneDetected, "P1 should grow without detected fluorescence");
    Assert(plates["P2"].Growth == TransformationGrowth.Absent && plates["P2"].Fluorescence == TransformationFluorescence.NotAssessable, "P2 should be absent and not assessable");
    Assert(plates["P3"].Growth == TransformationGrowth.Present && plates["P3"].Fluorescence == TransformationFluorescence.Detected, "P3 should grow and fluoresce under excitation");
    Assert(plates["P4"].Growth == TransformationGrowth.Present && plates["P4"].Fluorescence == TransformationFluorescence.NoneDetected, "P4 should grow without detected fluorescence");
}

static void ActualTubeContentsMatter()
{
    var sim = NewSimulation(); sim.Start(TransformationRunMode.GuidedPractice); sim.Submit(new(TransformationActionType.AcknowledgeIntro));
    LabelTubes(sim); TransferTube(sim, "T", "Buffer"); TransferTube(sim, "NC", "DNA");
    CompletePlateSetup(sim); LockPredictions(sim); ObserveAll(sim);
    var plates = sim.Snapshot().Plates.ToDictionary(p => p.Id);
    Assert(plates["P1"].Growth == TransformationGrowth.Absent && plates["P2"].Growth == TransformationGrowth.Present, "tube labels cannot overwrite the actual simulated contents");
}

static void AssessmentFeedbackTiming()
{
    var sim = NewSimulation(); sim.Start(TransformationRunMode.Assessment);
    var response = sim.Submit(new(TransformationActionType.LoadTube));
    Assert(!response.Accepted && !response.Message.Contains("intro", StringComparison.OrdinalIgnoreCase), "assessment should record an immediate action error without procedural explanation");
    sim.Submit(new(TransformationActionType.AcknowledgeIntro));
    sim.Submit(new(TransformationActionType.LabelTube, "T", "Transformation reaction")); sim.Submit(new(TransformationActionType.LabelTube, "NC", "Negative control")); sim.Submit(new(TransformationActionType.UseFreshTip)); sim.Submit(new(TransformationActionType.SelectTubeSource, "DNA")); sim.Submit(new(TransformationActionType.SelectTubeDestination, "T")); response = sim.Submit(new(TransformationActionType.LoadTube));
    Assert(response.Accepted && response.Message == "Action recorded. Coaching is held for debrief.", "assessment accepted feedback must be deferred");
}

static void CuratedCases()
{
    foreach (var id in new[] { "missing-arabinose", "swapped-tube-labels", "missing-dna", "failed-antibiotic-negative-control" })
    {
        var sim = NewSimulation(); sim.Start(TransformationRunMode.GuidedPractice); SetupNormalPlan(sim); LockPredictions(sim); ObserveAll(sim); sim.Submit(new(TransformationActionType.StartDiagnosticCase, id));
        foreach (var plate in new[] { "P1", "P2", "P3", "P4" }) { sim.Submit(new(TransformationActionType.ViewCasePlate, plate, "normal")); sim.Submit(new(TransformationActionType.ViewCasePlate, plate, "excitation")); }
        var report = sim.BuildReport();
        Assert(report.DiagnosticCase is null && !report.ClaimReasons.ContainsKey("case-selection") && sim.Snapshot().DiagnosticCaseId == "case-in-progress", "case answer and support metadata must remain hidden until debrief");
        sim.Submit(new(TransformationActionType.ViewEvidenceCard, "setup-record")); sim.Submit(new(TransformationActionType.ViewEvidenceCard, "control-comparison")); sim.Submit(new(TransformationActionType.ViewEvidenceCard, "observation-limit")); sim.Submit(new(TransformationActionType.RecordDiagnosticDecision, "selection-withheld", "control-pattern")); sim.Submit(new(TransformationActionType.RecordDiagnosticDecision, "expression-withheld", "phenotype-not-cause")); sim.Submit(new(TransformationActionType.RecordDiagnosticDecision, "uncertainty-cannot-prove", "phenotype-not-cause")); sim.Submit(new(TransformationActionType.RecordCleanup)); sim.Submit(new(TransformationActionType.RecordWasteDecision)); sim.Submit(new(TransformationActionType.RecordDebrief)); sim.Submit(new(TransformationActionType.CompleteDebrief));
        Assert(sim.BuildReport().DiagnosticCase?.FinalFictionalCause is not null, "debrief should disclose the selected fictional case cause");
    }
}

static void NormalClaims()
{
    var sim = NewSimulation(); sim.Start(TransformationRunMode.GuidedPractice); SetupNormalPlan(sim); LockPredictions(sim); ObserveAll(sim); var report = sim.BuildReport(); Assert(report.MainSelectionClaimSupported && report.MainExpressionClaimSupported, "normal controls including P2 NotAssessable should support both limited claim dimensions");
}

static void PredictionDimensionsAndCorrections()
{
    var sim = NewSimulation(); sim.Start(TransformationRunMode.GuidedPractice); sim.Submit(new(TransformationActionType.AcknowledgeIntro)); LabelTubes(sim); TransferTube(sim, "T", "Buffer"); TransferTube(sim, "NC", "Buffer");
    Assert(sim.Submit(new(TransformationActionType.CorrectTubeContent, "T", "DNA")).Accepted, "committed tube content must be correctable after plate setup begins and before original predictions"); Assert(sim.Snapshot().TubeSetupCorrections.Contains("T:Buffer->DNA"), "tube correction history must be retained"); CompletePlateSetup(sim); ExpectReject(sim, new(TransformationActionType.CommitPrediction, "P1", "growth:Detected")); ExpectReject(sim, new(TransformationActionType.CommitPrediction, "P1", "fluorescence:Absent")); sim.Submit(new(TransformationActionType.CommitPrediction, "P1", "growth:NotSure")); Assert(sim.Submit(new(TransformationActionType.CorrectTubeContent, "T", "Buffer")).Accepted, "a pre-lock tube correction must retain and clear affected provisional predictions"); Assert(sim.Snapshot().Predictions.All(p => p.PlateId != "P1"), "tube correction must require fresh predictions for affected plates"); sim.Submit(new(TransformationActionType.CommitPrediction, "P1", "growth:NotSure")); sim.Submit(new(TransformationActionType.CommitPrediction, "P1", "fluorescence:NotSure")); Assert(sim.Submit(new(TransformationActionType.CorrectPlateAssignment, "P1", "T|LB+amp")).Accepted, "a pre-lock plate correction must retain and clear the affected provisional prediction"); Assert(sim.Snapshot().PrelockPredictionCorrections.Any(), "pre-lock prediction history must retain replaced values"); sim.Submit(new(TransformationActionType.CommitPrediction, "P1", "growth:NotSure")); sim.Submit(new(TransformationActionType.CommitPrediction, "P1", "fluorescence:NotSure")); LockPredictions(sim); sim.Submit(new(TransformationActionType.AdvanceConceptualTime)); foreach (var plate in new[] { "P1", "P2", "P3", "P4" }) { sim.Submit(new(TransformationActionType.ViewPlate, plate, "normal")); sim.Submit(new(TransformationActionType.ViewPlate, plate, "excitation")); } ExpectReject(sim, new(TransformationActionType.RevisePrediction, "P1", "after-observation")); Assert(sim.Submit(new(TransformationActionType.RevisePrediction, "P1", "growth:NotSure")).Accepted, "a valid later prediction revision must append without overwriting its original");
}

static void ClaimSupportUsesActualControls()
{
    var malformed = NewSimulation(); malformed.Start(TransformationRunMode.GuidedPractice); malformed.Submit(new(TransformationActionType.AcknowledgeIntro)); LabelTubes(malformed); TransferTube(malformed, "T", "DNA"); TransferTube(malformed, "NC", "Buffer");
    malformed.Submit(new(TransformationActionType.AssignPlate, "P1", "T|LB+amp")); malformed.Submit(new(TransformationActionType.AssignPlate, "P2", "NC|LB+amp")); malformed.Submit(new(TransformationActionType.AssignPlate, "P3", "T|LB+amp+Ara")); malformed.Submit(new(TransformationActionType.AssignPlate, "P4", "T|LB")); LockPredictions(malformed); ObserveAll(malformed);
    Assert(!malformed.BuildReport().MainSelectionClaimSupported, "a T/LB P4 cannot stand in for the NC/LB viability control");

    var missingAra = NewSimulation(); missingAra.Start(TransformationRunMode.GuidedPractice); SetupNormalPlan(missingAra);
    Assert(missingAra.Submit(new(TransformationActionType.CorrectPlateAssignment, "P3", "T|LB+amp")).Accepted, "P3 arabinose may be corrected before predictions"); LockPredictions(missingAra); ObserveAll(missingAra);
    var report = missingAra.BuildReport(); Assert(report.MainSelectionClaimSupported && !report.MainExpressionClaimSupported, "selection controls remain interpretable when P3 lacks arabinose, while the expression comparison is withheld");
}

static void PrededriefReportRedaction()
{
    var sim = NewSimulation(); sim.Start(TransformationRunMode.GuidedPractice); SetupNormalPlan(sim); LockPredictions(sim); ObserveAll(sim); sim.Submit(new(TransformationActionType.StartDiagnosticCase, "missing-arabinose"));
    foreach (var plate in new[] { "P1", "P2", "P3", "P4" }) { sim.Submit(new(TransformationActionType.ViewCasePlate, plate, "normal")); sim.Submit(new(TransformationActionType.ViewCasePlate, plate, "excitation")); }
    var report = sim.BuildReport(); var csv = TransformationSimulation.BuildCsv(report); var html = TransformationSimulation.BuildHtml(report);
    Assert(report.CaseObservations.Count == 4 && report.DiagnosticCase is null, "pre-debrief report retains visible case observations without a causal record");
    Assert(!csv.Contains("missing-arabinose", StringComparison.Ordinal) && !html.Contains("missing-arabinose", StringComparison.Ordinal), "pre-debrief exports must not disclose causal case identifiers");
    Assert(!report.ClaimReasons.Keys.Any(k => k.StartsWith("case-", StringComparison.Ordinal)) && report.Ledger.Where(record => record.Type == TransformationActionType.StartDiagnosticCase).All(record => record.Target == "case-in-progress") && sim.Snapshot().DiagnosticSetup is null && report.CaseSetup is null && !csv.Contains("P3=T|LB+amp") && !html.Contains("P3=T|LB+amp"), "pre-card learner-facing records must withhold diagnostic setup and causal metadata");
}

static void LinkedRetry()
{
    var sim = NewSimulation(); var prior = sim.Start(TransformationRunMode.GuidedPractice).AttemptId; var retry = sim.StartLinkedRetry(prior, TransformationRunMode.Assessment);
    Assert(retry.AttemptId != prior && retry.RetryOfAttemptId == prior && retry.Mode == TransformationRunMode.Assessment, "a retry must create a separate linked attempt record");
}

static void LinkedRetryRestore()
{
    var sim = NewSimulation(); var parent = sim.Start(TransformationRunMode.GuidedPractice).AttemptId; sim.StartLinkedRetry(parent, TransformationRunMode.Assessment); var restored = TransformationSimulation.Restore(sim.Save());
    Assert(restored.Snapshot().RetryOfAttemptId == parent, "linked retry provenance must survive restore even when its parent file is unavailable");
}

static void DiagnosticDecisionRubric()
{
    var sim = NewSimulation(); sim.Start(TransformationRunMode.Assessment); SetupNormalPlan(sim); LockPredictions(sim); ObserveAll(sim); sim.Submit(new(TransformationActionType.StartDiagnosticCase, "missing-arabinose"));
    foreach (var plate in new[] { "P1", "P2", "P3", "P4" }) { sim.Submit(new(TransformationActionType.ViewCasePlate, plate, "normal")); sim.Submit(new(TransformationActionType.ViewCasePlate, plate, "excitation")); }
    ExpectReject(sim, new(TransformationActionType.RecordDiagnosticDecision, "selection-supported", "control-pattern"));
    foreach (var card in new[] { "setup-record", "control-comparison", "observation-limit" }) sim.Submit(new(TransformationActionType.ViewEvidenceCard, card));
    sim.Submit(new(TransformationActionType.RecordDiagnosticDecision, "selection-withheld", "control-pattern")); sim.Submit(new(TransformationActionType.RecordDiagnosticDecision, "selection-supported", "control-pattern")); sim.Submit(new(TransformationActionType.RecordDiagnosticDecision, "expression-supported", "uncertain")); sim.Submit(new(TransformationActionType.RecordDiagnosticDecision, "uncertainty-not-sure", "uncertain"));
    sim.Submit(new(TransformationActionType.RecordCleanup)); sim.Submit(new(TransformationActionType.RecordWasteDecision)); sim.Submit(new(TransformationActionType.RecordDebrief)); sim.Submit(new(TransformationActionType.CompleteDebrief));
    var report = sim.BuildReport(); Assert(report.DiagnosticDecisions.Count == 3 && report.DiagnosticDecisionHistory.Any(item => item.StartsWith("selection-withheld", StringComparison.Ordinal)), "one current decision per claim dimension must preserve replacements in history");
    Assert(report.RubricStatus["plausibility"].Contains("selection aligned") && report.RubricStatus["plausibility"].Contains("expression mismatch"), "rubric must compare current claims with control-derived support instead of a hidden cause");
}

static void MainExplanationRubric()
{
    var sim = NewSimulation(); sim.Start(TransformationRunMode.Assessment); SetupNormalPlan(sim); LockPredictions(sim); ObserveAll(sim);
    sim.Submit(new(TransformationActionType.RecordExplanation, "P1", "growth-proves-all"));
    Assert(sim.BuildReport().RubricStatus["evidence-limits"].Contains("mismatch"), "a wrong current main explanation must be recorded as a mismatch");
    sim.Submit(new(TransformationActionType.RecordExplanation, "P1", "nongreen-not-no-dna"));
    var report = sim.BuildReport(); Assert(report.RubricStatus["evidence-limits"].Contains("aligned") && report.ExplanationChoiceHistory.Contains("P1:growth-proves-all"), "a corrected current explanation must align while retaining the prior answer");
}

static void MeaningfulMainExplanations()
{
    var sim = NewSimulation(); sim.Start(TransformationRunMode.Assessment); SetupNormalPlan(sim); LockPredictions(sim); ObserveAllWithoutExplanations(sim);
    Assert(sim.Submit(new(TransformationActionType.RecordExplanation, "P1", "nongreen-not-no-dna")).Accepted, "a non-green plate must accept the evidence-limited DNA statement");
    Assert(sim.Submit(new(TransformationActionType.RecordExplanation, "P2", "no-colonies-not-assessable")).Accepted, "a no-colonies plate must accept the not-assessable statement");
    Assert(sim.Submit(new(TransformationActionType.RecordExplanation, "P3", "green-consistent-gfp")).Accepted, "a green excitation phenotype must accept the GFP-consistent statement");
    ExpectReject(sim, new(TransformationActionType.RecordExplanation, "P4", "evidence-limited"));
}

static void DiagnosticCasePrerequisite()
{
    var sim = NewSimulation(); sim.Start(TransformationRunMode.Assessment); SetupNormalPlan(sim); LockPredictions(sim); ObserveAllWithoutExplanations(sim);
    ExpectReject(sim, new(TransformationActionType.StartDiagnosticCase, "missing-arabinose"));
    foreach (var plate in new[] { "P1", "P2", "P3", "P4" }) sim.Submit(new(TransformationActionType.RecordExplanation, plate, plate == "P2" ? "no-colonies-not-assessable" : plate == "P3" ? "green-consistent-gfp" : "nongreen-not-no-dna"));
    Assert(sim.Submit(new(TransformationActionType.StartDiagnosticCase, "missing-arabinose")).Accepted, "the learner must recover into a case after all observation explanations are recorded");
}

static void CaseDSetup()
{
    var sim = NewSimulation(); sim.Start(TransformationRunMode.GuidedPractice); SetupNormalPlan(sim); LockPredictions(sim); ObserveAll(sim); sim.Submit(new(TransformationActionType.StartDiagnosticCase, "failed-antibiotic-negative-control")); sim.Submit(new(TransformationActionType.ViewEvidenceCard, "setup-record"));
    var state = sim.Snapshot(); var report = sim.BuildReport(); var csv = TransformationSimulation.BuildCsv(report); var html = TransformationSimulation.BuildHtml(report);
    Assert(state.DiagnosticSetup?.TubeContents["NC"] == "Buffer" && state.DiagnosticSetup.PlateAssignments["P2"] == "NC|LB" && state.DiagnosticSetup.IntendedPlateLabels["P2"] == "NC|LB+amp", "Case D must preserve the intended P2 label while modeling absent fictional selection condition");
    Assert(csv.Contains("intended plate labels: P1=T|LB+amp | P2=NC|LB+amp") && html.Contains("intended plate labels: P1=T|LB+amp | P2=NC|LB+amp"), "case exports must render intended labels separately from actual assignments");
}

static void CanonicalDefinitionValidation()
{
    var scenario = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "transformation-scenario.v1.json"));
    var lessons = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "transformation-lessons.v1.json"));
    var casePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "transformation-cases.v1.json");
    var cases = File.ReadAllText(casePath).Replace("\"fluorescence\": \"NoneDetected\"", "\"fluorescence\": \"Detected\"", StringComparison.Ordinal);
    var rubric = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "transformation-rubric.v1.json"));
    var sources = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "transformation-sources.v1.json"));
    ExpectThrows(() => TransformationSimulation.FromJsonDocuments(scenario, lessons, cases, rubric, sources), "a changed canonical case phenotype must be rejected");
    var definitions = JsonSerializer.Deserialize<TransformationCasesDefinition>(File.ReadAllText(casePath), DefinitionJsonOptions()) ?? throw new InvalidOperationException("fixture must deserialize");
    definitions.Cases.Single(item => item.Id == "failed-antibiotic-negative-control").Setup.TubeContents["NC"] = "DNA";
    ExpectThrows(() => TransformationSimulation.FromJsonDocuments(scenario, lessons, JsonSerializer.Serialize(definitions, DefinitionJsonOptions()), rubric, sources), "a changed canonical Case D tube content must be rejected");
    definitions = JsonSerializer.Deserialize<TransformationCasesDefinition>(File.ReadAllText(casePath), DefinitionJsonOptions()) ?? throw new InvalidOperationException("fixture must deserialize");
    definitions.Cases.Single(item => item.Id == "failed-antibiotic-negative-control").Setup.PlateAssignments["P2"] = "NC|LB+amp";
    ExpectThrows(() => TransformationSimulation.FromJsonDocuments(scenario, lessons, JsonSerializer.Serialize(definitions, DefinitionJsonOptions()), rubric, sources), "a changed canonical Case D actual assignment must be rejected");
    definitions = JsonSerializer.Deserialize<TransformationCasesDefinition>(File.ReadAllText(casePath), DefinitionJsonOptions()) ?? throw new InvalidOperationException("fixture must deserialize");
    definitions.Cases.Single(item => item.Id == "failed-antibiotic-negative-control").Setup.IntendedPlateLabels["P2"] = "NC|LB";
    ExpectThrows(() => TransformationSimulation.FromJsonDocuments(scenario, lessons, JsonSerializer.Serialize(definitions, DefinitionJsonOptions()), rubric, sources), "a changed canonical Case D intended label must be rejected");
}

static void SaveRestoreAndCorruption()
{
    var sim = NewSimulation(); sim.Start(TransformationRunMode.GuidedPractice); SetupNormalPlan(sim); var saved = sim.Save();
    var restored = TransformationSimulation.Restore(saved);
    Assert(restored.Save() == saved, "save/restore must preserve a deterministic ledger and definition snapshot");
    ExpectThrows(() => TransformationSimulation.Restore(saved.Replace("\"attemptId\": \"", "\"attemptId\": \"../unsafe", StringComparison.Ordinal)), "corrupted save must be rejected");
}

static void StoreAndExports()
{
    var root = Path.Combine(Path.GetTempPath(), "transformation-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        var sim = NewSimulation(); sim.Start(TransformationRunMode.GuidedPractice); SetupNormalPlan(sim); LockPredictions(sim); ObserveAll(sim);
        var store = new TransformationAttemptStore(root); store.Save(sim); var id = sim.Snapshot().AttemptId;
        Assert(store.ListAttempts().Single().Id == id, "level-two store must list its own saved attempt");
        var report = store.Load(id).BuildReport(); var exports = store.Export(report);
        var csv = File.ReadAllText(exports.CsvPath); var html = File.ReadAllText(exports.HtmlPath);
        Assert(csv.Contains("T=DNA") && csv.Contains("T=Transformation reaction") && csv.Contains("tube_setup_corrections"), "CSV export must render tube contents, labels, and correction fields");
        Assert(html.Contains("T=DNA") && html.Contains("Labels:") && html.Contains("Tube setup and corrections"), "HTML export must render tube contents, labels, and corrections");
        var malformedPath = Path.Combine(root, "level2-transformation", "attempts", "malformed.json"); File.WriteAllText(malformedPath, "{not-json"); store.ListAttempts(); Assert(store.Diagnostics.Any(diagnostic => diagnostic.Contains("malformed.json")), "malformed Level 2 files must be preserved and surfaced as recoverable diagnostics");
        File.WriteAllText(Path.Combine(root, "level1.json"), "preserve"); store.DeleteAttempt(id);
        Assert(File.Exists(Path.Combine(root, "level1.json")) && !store.ListAttempts().Any(), "level-two deletion must not affect adjacent files");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
}

static void SetupNormalPlan(TransformationSimulation sim)
{
    sim.Submit(new(TransformationActionType.AcknowledgeIntro)); LabelTubes(sim); TransferTube(sim, "T", "DNA"); TransferTube(sim, "NC", "Buffer"); CompletePlateSetup(sim);
}
static void CompletePlateSetup(TransformationSimulation sim)
{
    sim.Submit(new(TransformationActionType.AssignPlate, "P1", "T|LB+amp")); sim.Submit(new(TransformationActionType.AssignPlate, "P2", "NC|LB+amp")); sim.Submit(new(TransformationActionType.AssignPlate, "P3", "T|LB+amp+Ara")); sim.Submit(new(TransformationActionType.AssignPlate, "P4", "NC|LB"));
}
static void LockPredictions(TransformationSimulation sim)
{
    foreach (var plate in new[] { "P1", "P2", "P3", "P4" }) { sim.Submit(new(TransformationActionType.CommitPrediction, plate, "growth:NotSure")); sim.Submit(new(TransformationActionType.CommitPrediction, plate, "fluorescence:NotSure")); }
}
static void ObserveAll(TransformationSimulation sim)
{
    sim.Submit(new(TransformationActionType.AdvanceConceptualTime));
    foreach (var plate in new[] { "P1", "P2", "P3", "P4" }) { sim.Submit(new(TransformationActionType.ViewPlate, plate, "normal")); sim.Submit(new(TransformationActionType.ViewPlate, plate, "excitation")); }
    foreach (var plate in new[] { "P1", "P2", "P3", "P4" }) sim.Submit(new(TransformationActionType.RecordExplanation, plate, plate == "P2" ? "no-colonies-not-assessable" : plate == "P3" ? "green-consistent-gfp" : "nongreen-not-no-dna"));
}

static void CaseClaimDisplayScopesAndDefers()
{
    var preDebrief = new TransformationReport
    {
        Mode = TransformationRunMode.Assessment,
        ClaimReasons = new Dictionary<string, string>
        {
            ["main-selection"] = "main selection reason",
            ["main-expression"] = "main expression reason"
        },
        CaseObservations = [new TransformationPlateState { Id = "P3", Growth = TransformationGrowth.Present, Fluorescence = TransformationFluorescence.NoneDetected }]
    };
    var active = Level2ClaimDisplay.Sidebar(preDebrief);
    Assert(active.Contains("Main experiment — selection support: False") && active.Contains("Main experiment — expression support: False"), "the sidebar must scope each retained main-experiment claim");
    Assert(active.Contains("Active case — observed record only") && active.Contains("held for debrief"), "an active case must show only its observed-record boundary");
    Assert(!active.Contains("Case debrief") && !active.Contains("missing-arabinose") && !active.Contains("case expression reason"), "Assessment must not disclose a case answer or case support reason before debrief");

    preDebrief.Complete = true;
    preDebrief.DiagnosticCase = new TransformationDiagnosticReport
    {
        FinalFictionalCause = "missing-arabinose",
        SelectionClaimSupported = true,
        ExpressionClaimSupported = false,
        SelectionClaimReason = "case selection reason",
        ExpressionClaimReason = "case expression reason"
    };
    var debrief = Level2ClaimDisplay.Debrief(preDebrief);
    Assert(!debrief.Contains("[b]", StringComparison.Ordinal), "the plain center debrief must not receive RichTextLabel BBCode");
    Assert(debrief.Contains("Main experiment — selection support: False") && debrief.Contains("Case debrief — selection support: True") && debrief.Contains("Case debrief — expression support: False"), "completed debrief must display separate main and case support flags");
    Assert(debrief.Contains("main selection reason") && debrief.Contains("main expression reason") && debrief.Contains("case selection reason") && debrief.Contains("case expression reason"), "completed debrief must display separate main and case reasons");
}

static void CompletedCaseHtmlScopesClaims()
{
    var sim = NewSimulation(); sim.Start(TransformationRunMode.GuidedPractice); SetupNormalPlan(sim); LockPredictions(sim); ObserveAll(sim); sim.Submit(new(TransformationActionType.StartDiagnosticCase, "missing-arabinose"));
    foreach (var plate in new[] { "P1", "P2", "P3", "P4" }) { sim.Submit(new(TransformationActionType.ViewCasePlate, plate, "normal")); sim.Submit(new(TransformationActionType.ViewCasePlate, plate, "excitation")); }
    foreach (var card in new[] { "setup-record", "control-comparison", "observation-limit" }) sim.Submit(new(TransformationActionType.ViewEvidenceCard, card));
    sim.Submit(new(TransformationActionType.RecordDiagnosticDecision, "selection-supported", "control-pattern")); sim.Submit(new(TransformationActionType.RecordDiagnosticDecision, "expression-withheld", "phenotype-not-cause")); sim.Submit(new(TransformationActionType.RecordDiagnosticDecision, "uncertainty-cannot-prove", "phenotype-not-cause"));
    sim.Submit(new(TransformationActionType.RecordCleanup)); sim.Submit(new(TransformationActionType.RecordWasteDecision)); sim.Submit(new(TransformationActionType.RecordDebrief)); sim.Submit(new(TransformationActionType.CompleteDebrief));
    var html = TransformationSimulation.BuildHtml(sim.BuildReport());
    Assert(html.Contains("Main experiment claim status") && html.Contains("Main experiment selection support: True") && html.Contains("Main experiment expression support: True"), "completed HTML must label main-experiment support flags");
    Assert(html.Contains("Case debrief claim status") && html.Contains("Case selection support: True") && html.Contains("Case expression support: False"), "completed HTML must label Case A support flags separately");
    Assert(html.Contains("The curated controls support a limited selection claim in this fictional case.") && html.Contains("The curated control pattern withholds the planned expression claim; phenotype alone does not prove one cause."), "completed HTML must include both case-specific support reasons");
}
static void ObserveAllWithoutExplanations(TransformationSimulation sim)
{
    sim.Submit(new(TransformationActionType.AdvanceConceptualTime));
    foreach (var plate in new[] { "P1", "P2", "P3", "P4" }) { sim.Submit(new(TransformationActionType.ViewPlate, plate, "normal")); sim.Submit(new(TransformationActionType.ViewPlate, plate, "excitation")); }
}
static void LabelTubes(TransformationSimulation sim)
{
    sim.Submit(new(TransformationActionType.LabelTube, "T", "Transformation reaction")); sim.Submit(new(TransformationActionType.LabelTube, "NC", "Negative control"));
}
static void TransferTube(TransformationSimulation sim, string tube, string content)
{
    sim.Submit(new(TransformationActionType.UseFreshTip)); sim.Submit(new(TransformationActionType.SelectTubeSource, content)); sim.Submit(new(TransformationActionType.SelectTubeDestination, tube)); sim.Submit(new(TransformationActionType.LoadTube));
}
static void ExpectReject(TransformationSimulation sim, TransformationAction action) => Assert(!sim.Submit(action).Accepted, "action should be rejected: " + action.Type);
static void ExpectThrows(Action action, string message) { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException(message); }
static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static JsonSerializerOptions DefinitionJsonOptions() => new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };


