using CancerLabTrainer.Core;
using CancerLabTrainer.Ui;
using System.Security.Cryptography;
using System.Text.Json;

var tests = new (string Name, Action Body)[]
{
    ("valid workflow produces supported fictional observation", ValidWorkflowProducesSupportedObservation),
    ("missing control blocks a supported conclusion", MissingControlBlocksConclusion),
    ("wrong reader mode blocks a supported conclusion", WrongReaderModeBlocksConclusion),
    ("wrong transfer volume changes the modeled measurement", WrongVolumeChangesMeasurement),
    ("incomplete treatment replicates block a supported conclusion", IncompleteTreatmentBlocksConclusion),
    ("nonfinite volume is rejected without corrupting session state", NonfiniteVolumeIsRejectedAtomically),
    ("corrected pre-acquisition reader setup can be valid", CorrectedReaderSetupCanBeValid),
    ("source inventory and carryover are modeled and recur after retry", CarryoverRecursAfterGuidedRetry),
    ("assessment feedback is deferred at the core seam", AssessmentFeedbackIsDeferred),
    ("reviewing readings does not silently record a conclusion", ReviewRequiresDecision),
    ("invalid run can be escalated and completed", InvalidRunCanBeEscalated),
    ("completed attempts are locked", CompletedAttemptIsLocked),
    ("saved sessions restore the same observable state", SavedSessionRestoresState),
    ("malformed scenarios and saved state are rejected", MalformedStateIsRejected),
    ("loaded liquid cannot be relabeled by source selection", LoadedSourceCannotChange),
    ("duplicate dispensing conserves volume", DuplicateDispenseConservesVolume),
    ("measurements and pre-closeout records are locked", AcquisitionAndCloseoutGates),
    ("label corrections only affect the selected well", LabelCorrectionIsScoped),
    ("pre-transfer volume correction does not poison a run", VolumeCorrectionBeforeDispense),
    ("different attempt ids preserve deterministic readings", DeterministicReadings),
    ("replicate spread agrees with a hand calculation", ReplicateStatistics),
    ("malformed provenance cannot escape attempt storage", InvalidAttemptIdentity),
    ("restore rejects quantities and conclusions inconsistent with history", InconsistentSaveRejected),
    ("replay preserves rejected actions, errors and guided retry", ReplayPreservesRecovery),
    ("assessment coaching is revealed at debrief and survives resume", DeferredCoachingSurvivesResume)
    ,("forward sequence rejects out of order actions atomically", ForwardSequenceRejectsOutOfOrderActionsAtomically)
    ,("destination first stop records nominal transfer and second stop adds none", FirstStopTransfersNominalAndSecondStopAddsNone)
    ,("fast aspiration release blocks support without changing nominal quantity", FastReleaseIsQualitativeAndRecoverable)
    ,("destination stays bound while the UI selection changes", DestinationBindingSurvivesSelectionChange)
    ,("reader rejects an active pipette cycle", ReaderRejectsActivePipetteCycle)
    ,("insufficient nominal source inventory rejects before cycle lock", InsufficientInventoryRejectsBeforeCycleLock)
    ,("current saves restore every forward-pipette stage and reproduce readings", CurrentSaveRestoresDetailedPipetteState)
    ,("synthetic v1.0.1 fixture remains historical read-only", HistoricalFixtureRemainsPreservedAndViewOnly)
    ,("default rules classify fast release as critical", DefaultRulesClassifyFastReleaseAsCritical)
    ,("tip ejection only occurs in air after a completed cycle", TipEjectionRespectsCycle)
    ,("assessment defers fast-release coaching", AssessmentDefersFastReleaseCoaching)
    ,("pipetting-quality teaching keeps guidance optional and assessment defers it", PipettingQualityTeachingKeepsGuidanceOptionalAndAssessmentDefersIt)
    ,("saved source manifests preserve legacy absence of the SOP entry", SavedSourceManifestsPreserveLegacyAbsenceOfSopEntry)
};
var failures = new List<string>();
foreach (var test in tests)
{
    try { test.Body(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failures.Add($"FAIL {test.Name}: {ex.Message}"); }
}
foreach (var failure in failures) Console.Error.WriteLine(failure);
Console.WriteLine($"{(failures.Count == 0 ? "PASS" : "FAIL")}: {tests.Length - failures.Count}/{tests.Length} simulation contract tests");
return failures.Count == 0 ? 0 : 1;

static LabSimulation NewSimulation()
{
    var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "scenario.v1.json");
    return LabSimulation.FromJsonDocuments(File.ReadAllText(path), File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "rules.v1.json")), File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "sources.v1.json")));
}
static void Prepare(LabSimulation sim, RunMode mode = RunMode.GuidedPractice)
{
    sim.Start(mode);
    sim.Submit(new(LabActionType.WearPpe)); sim.Submit(new(LabActionType.DisinfectBench)); sim.Submit(new(LabActionType.CheckMaterials));
    sim.Submit(new(LabActionType.VerifyLabels)); sim.Submit(new(LabActionType.ReviewPlateMap));
    sim.Submit(new(LabActionType.AttachTip)); sim.Submit(new(LabActionType.SetVolume, Value: 50));
}
static void TransferAll(LabSimulation sim, IEnumerable<string>? omit = null)
{
    var omitted = (omit ?? []).ToHashSet();
    foreach (var well in new[] { "B1", "B2", "B3", "B4", "B5", "B6" }) if (!omitted.Contains(well)) TransferOne(sim, well);
}
static void TransferOne(LabSimulation sim, string well)
{
    if (sim.Snapshot().TipAttached) sim.Submit(new(LabActionType.EjectTip));
    sim.Submit(new(LabActionType.AttachTip));
    if (sim.Snapshot().SelectedVolumeUl is null) sim.Submit(new(LabActionType.SetVolume, Value: 50));
    sim.Submit(new(LabActionType.SelectSource, sim.Snapshot().Wells.Single(w => w.Well == well).Role));
    ForwardLoad(sim, sim.Snapshot().SelectedSource!);
    Assert(sim.Submit(new(LabActionType.MoveToDestination, well)).Accepted, "destination positioning should succeed");
    Assert(sim.Submit(new(LabActionType.PressFirstStop)).Accepted, "first-stop nominal transfer should succeed");
    Assert(sim.Submit(new(LabActionType.PressSecondStop)).Accepted, "second-stop clearing should succeed");
    Assert(sim.Submit(new(LabActionType.WithdrawAndRelease)).Accepted, "withdraw/release should complete the cycle");
}
static void Measure(LabSimulation sim, string readerMode = "Luminescence")
{
    sim.Submit(new(LabActionType.LoadPlate, "correct")); sim.Submit(new(LabActionType.ConfigureReader, readerMode)); sim.Submit(new(LabActionType.RunReader)); sim.Submit(new(LabActionType.ReviewResults));
}
static void ValidWorkflowProducesSupportedObservation()
{
    var sim = NewSimulation(); Prepare(sim); TransferAll(sim); Measure(sim); sim.Submit(new(LabActionType.DecideSupportedConclusion));
    var report = sim.BuildReport(); Assert(report.ControlsValid && report.CanSupportConclusion, "valid controls should support a fictional observation");
    Assert(report.Wells.Single(w => w.Well == "B5").RelativeToVehicle < 50, "fictional treatment should normalize below 50%");
}
static void MissingControlBlocksConclusion()
{
    var sim = NewSimulation(); Prepare(sim); TransferAll(sim, ["B3"]); Measure(sim); var result = sim.Submit(new(LabActionType.DecideSupportedConclusion));
    Assert(!sim.BuildReport().CanSupportConclusion, "missing vehicle control must block conclusion"); Assert(result.Accepted, "attempt must be preserved for debrief");
}
static void WrongReaderModeBlocksConclusion()
{
    var sim = NewSimulation(); Prepare(sim); TransferAll(sim); sim.Submit(new(LabActionType.LoadPlate, "correct")); sim.Submit(new(LabActionType.ConfigureReader, "Absorbance"));
    Assert(!sim.Submit(new(LabActionType.RunReader)).Accepted, "reader should not run after wrong configuration");
    Assert(sim.Snapshot().Issues.Any(x => x.Code == "ReaderMode"), "configuration error should remain visible");
}
static void WrongVolumeChangesMeasurement()
{
    var correct = NewSimulation(); Prepare(correct); TransferAll(correct); Measure(correct);
    var wrong = NewSimulation(); Prepare(wrong); wrong.Submit(new(LabActionType.SetVolume, Value: 25)); TransferAll(wrong); Measure(wrong);
    Assert(wrong.Snapshot().Wells.Single(w => w.Well == "B5").RawReading < correct.Snapshot().Wells.Single(w => w.Well == "B5").RawReading, "half-volume toy transfer should lower modeled signal");
}
static void IncompleteTreatmentBlocksConclusion()
{
    var sim = NewSimulation(); Prepare(sim); TransferAll(sim, ["B5", "B6"]); Measure(sim);
    Assert(sim.BuildReport().ControlsValid, "blank and vehicle controls can be valid independently");
    Assert(!sim.BuildReport().CanSupportConclusion, "unprepared treatment replicates must block a treatment conclusion");
}
static void NonfiniteVolumeIsRejectedAtomically()
{
    var sim = NewSimulation(); Prepare(sim); var before = sim.Snapshot(); var result = sim.Submit(new(LabActionType.SetVolume, Value: double.NaN)); var after = sim.Snapshot();
    Assert(!result.Accepted, "nonfinite volume should be rejected"); Assert(after.SelectedVolumeUl == before.SelectedVolumeUl && after.Issues.Count == before.Issues.Count, "rejected volume must leave state unchanged");
}
static void CorrectedReaderSetupCanBeValid()
{
    var sim = NewSimulation(); Prepare(sim); TransferAll(sim); sim.Submit(new(LabActionType.LoadPlate, "wrong")); sim.Submit(new(LabActionType.LoadPlate, "correct"));
    sim.Submit(new(LabActionType.ConfigureReader, "Absorbance")); sim.Submit(new(LabActionType.ConfigureReader, "Luminescence")); sim.Submit(new(LabActionType.RunReader)); sim.Submit(new(LabActionType.ReviewResults));
    Assert(sim.BuildReport().CanSupportConclusion, "corrected setup before acquisition should be valid in this training model");
    Assert(sim.BuildReport().Issues.Any(x => x.Resolved), "corrected setup should remain audited");
}
static void CarryoverRecursAfterGuidedRetry()
{
    var sim = NewSimulation(); Prepare(sim);
    sim.Submit(new(LabActionType.SelectSource, "Blank")); ForwardLoad(sim, "Blank"); CompleteForwardTransfer(sim, "B1");
    sim.Submit(new(LabActionType.SelectSource, "Vehicle control")); ForwardLoad(sim, "Vehicle control"); CompleteForwardTransfer(sim, "B3");
    var first = sim.Snapshot(); Assert(first.SourceRemainingVolumeUl["Blank"] == 200 && first.SourceRemainingVolumeUl["Vehicle control"] == 200, "two 50 uL aspirates should be conserved"); Assert(first.Wells.Single(w => w.Well == "B3").CarryoverFactor > 1, "reused tip should apply configured illustrative carryover factor");
    sim.Submit(new(LabActionType.RetryTransferCheckpoint)); sim.Submit(new(LabActionType.AttachTip)); sim.Submit(new(LabActionType.SetVolume, Value: 50)); sim.Submit(new(LabActionType.SelectSource, "Blank")); ForwardLoad(sim, "Blank"); CompleteForwardTransfer(sim, "B1"); sim.Submit(new(LabActionType.SelectSource, "Vehicle control")); ForwardLoad(sim, "Vehicle control");
    Assert(sim.Snapshot().Issues.Count(x => x.Code == "Carryover") == 2 && sim.Snapshot().Issues.Any(x => x.Code == "Carryover" && !x.Resolved), "a recurring carryover condition must be a new active incident after retry");
}
static void AssessmentFeedbackIsDeferred()
{
    var sim = NewSimulation(); sim.Start(RunMode.Assessment); var result = sim.Submit(new(LabActionType.WearPpe));
    Assert(result.ScienceTitle == "Assessment record" && !result.ScienceLesson.Contains("Personal protective"), "assessment result must not leak guided procedural coaching");
}
static void ReviewRequiresDecision()
{
    var sim = NewSimulation(); Prepare(sim); TransferAll(sim); Measure(sim);
    Assert(sim.Snapshot().Phase == LabPhase.Interpretation && sim.Snapshot().InterpretationDecision is null, "review must leave an explicit conclusion or escalation decision pending");
}
static void InvalidRunCanBeEscalated()
{
    var sim = NewSimulation(); Prepare(sim); TransferAll(sim, ["B4"]); Measure(sim); sim.Submit(new(LabActionType.EscalateInvalidRun));
    sim.Submit(new(LabActionType.SortWaste)); sim.Submit(new(LabActionType.CleanBench)); sim.Submit(new(LabActionType.RecordHandoff));
    var report = sim.BuildReport(); Assert(report.Escalated && report.Complete, "escalated invalid run should reach a documented closeout");
}
static void CompletedAttemptIsLocked()
{
    var sim = NewSimulation(); Prepare(sim); TransferAll(sim, ["B4"]); Measure(sim); sim.Submit(new(LabActionType.EscalateInvalidRun));
    sim.Submit(new(LabActionType.SortWaste)); sim.Submit(new(LabActionType.CleanBench)); sim.Submit(new(LabActionType.RecordHandoff)); var before = sim.Snapshot();
    var result = sim.Submit(new(LabActionType.EjectTip)); Assert(!result.Accepted, "completed attempt should reject further mutations"); Assert(sim.Snapshot().TipId == before.TipId, "lock should preserve completed state");
}
static void SavedSessionRestoresState()
{
    var sim = NewSimulation(); Prepare(sim); TransferOne(sim, "B1"); var restored = LabSimulation.Restore(sim.Save());
    Assert(restored.Snapshot().Wells.Single(w => w.Well == "B1").TransferVolumeUl == 50, "saved transfer should restore");
    Assert(restored.Snapshot().Mode == RunMode.GuidedPractice, "run mode should restore");
}
static void MalformedStateIsRejected()
{
    ExpectThrows(() => new LabSimulation(new ScenarioDefinition { SchemaVersion = 1, Id = "bad", ReaderMode = "L", ToyTransferVolumeUl = 1, ActiveWells = [new() { Well = "Z99", Role = "Blank" }] }), "invalid scenario should not construct");
    ExpectThrows(() => LabSimulation.Restore("{}"), "malformed saved state should not restore");
}
static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static void LoadedSourceCannotChange()
{
    var sim = NewSimulation(); Prepare(sim); sim.Submit(new(LabActionType.SelectSource, "Blank")); ForwardLoad(sim, "Blank");
    Assert(!sim.Submit(new(LabActionType.SelectSource, "Vehicle control")).Accepted, "cannot change identity of loaded liquid");
    CompleteForwardTransfer(sim, "B1");
    Assert(sim.Snapshot().Wells[0].SourceRole == "Blank", "identity must survive dispensing");
}
static void DuplicateDispenseConservesVolume()
{
    var sim = NewSimulation(); Prepare(sim); TransferOne(sim, "B1"); TransferOne(sim, "B1");
    var s = sim.Snapshot();
    Assert(s.Wells[0].TransferVolumeUl == 100 && s.SourceRemainingVolumeUl["Blank"] == 150, "source loss must equal well gain");
    Assert(s.Wells[0].ContributionsUl["Blank"] == 100, "composition must retain both transfers");
}
static void AcquisitionAndCloseoutGates()
{
    var sim = NewSimulation(); Prepare(sim); Assert(!sim.Submit(new(LabActionType.CleanBench)).Accepted, "cannot pre-credit closeout"); TransferAll(sim); Measure(sim);
    var reading = sim.Snapshot().Wells[0].RawReading;
    Assert(!sim.Submit(new(LabActionType.PressFirstStop)).Accepted && !sim.Submit(new(LabActionType.RunReader)).Accepted, "acquisition is immutable");
    Assert(sim.Snapshot().Wells[0].RawReading == reading, "measurement remains unchanged");
}
static void LabelCorrectionIsScoped()
{
    var sim = NewSimulation(); Prepare(sim); sim.Submit(new(LabActionType.MislabelSelectedWell, "B1")); sim.Submit(new(LabActionType.MislabelSelectedWell, "B2")); sim.Submit(new(LabActionType.CorrectLabel, "B1"));
    Assert(sim.Snapshot().Issues.Count(i => i.Code == "Mislabel" && !i.Resolved) == 1, "one correction cannot resolve a different well");
}
static void VolumeCorrectionBeforeDispense()
{
    var sim = NewSimulation(); Prepare(sim); sim.Submit(new(LabActionType.SetVolume, Value:25)); sim.Submit(new(LabActionType.SetVolume, Value:50)); TransferAll(sim); Measure(sim);
    Assert(sim.BuildReport().CanSupportConclusion, "a corrected setting before any dispense must not contaminate results");
}
static void DeterministicReadings()
{
    var a = NewSimulation(); var b = NewSimulation(); Prepare(a); Prepare(b); TransferAll(a); TransferAll(b); Measure(a); Measure(b);
    Assert(a.Snapshot().AttemptId != b.Snapshot().AttemptId, "attempts need independent identities");
    Assert(a.BuildReport().Wells.Select(w=>w.RawReading).SequenceEqual(b.BuildReport().Wells.Select(w=>w.RawReading)), "same seed and actions must reproduce readings");
}
static void ReplicateStatistics()
{
    var sim = NewSimulation(); Prepare(sim); TransferAll(sim); Measure(sim); var r=sim.BuildReport(); var pair=r.Wells.Where(w=>w.Role=="Fictional treatment").Select(w=>w.RawReading!.Value).ToArray();
    var summary=r.Replicates.Single(x=>x.Role=="Fictional treatment");
    Assert(Math.Abs(summary.Mean-(pair[0]+pair[1])/2)<1e-9 && Math.Abs(summary.StandardDeviation-Math.Abs(pair[0]-pair[1])/Math.Sqrt(2))<1e-9, "sample SD/mean must agree with independent calculation");
}
static void InvalidAttemptIdentity()
{
    var sim=NewSimulation(); Prepare(sim); var json=sim.Save().Replace(sim.Snapshot().AttemptId,"../../outside"); ExpectThrows(()=>LabSimulation.Restore(json),"unsafe attempt id should be rejected");
}
static void InconsistentSaveRejected()
{
    var sim = NewSimulation(); Prepare(sim); TransferAll(sim); Measure(sim);
    foreach (var field in new[] { "source", "volume", "reading", "phase", "issue", "ledger" })
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(sim.Save())!;
        var state = json["Snapshot"]!;
        switch (field)
        {
            case "source": state["SourceRemainingVolumeUl"]!["Blank"] = 249; break;
            case "volume": state["Wells"]![0]!["TransferVolumeUl"] = 100; break;
            case "reading": state["Wells"]![0]!["RawReading"] = 9000; break;
            case "phase": state["Phase"] = "Complete"; break;
            case "issue": state["CanSupportConclusion"] = false; break;
            case "ledger": state["Ledger"]!.AsArray().RemoveAt(0); break;
        }
        ExpectThrows(() => LabSimulation.Restore(json.ToJsonString()), field + " must agree with replay");
    }
}
static void ReplayPreservesRecovery()
{
    var sim = NewSimulation(); Prepare(sim);
    sim.Submit(new(LabActionType.SetVolume, Value: double.NaN));
    sim.Submit(new(LabActionType.SelectSource, "Blank")); ForwardLoad(sim, "Blank");
    sim.Submit(new(LabActionType.Dispense));
    sim.Submit(new(LabActionType.RetryTransferCheckpoint));
    var restored = LabSimulation.Restore(sim.Save());
    Assert(restored.Save() == sim.Save(), "replay must retain recovery and rejected actions exactly");
}
static void DeferredCoachingSurvivesResume()
{
    var sim = NewSimulation(); Prepare(sim, RunMode.Assessment); TransferAll(sim); Measure(sim);
    Assert(sim.Snapshot().Mode == RunMode.Assessment, "this regression must exercise Assessment");
    Assert(sim.BuildReport().Lessons.Count == 0, "report must not reveal coaching before completion");
    sim.Submit(new(LabActionType.DecideSupportedConclusion));
    foreach (var type in new[] { LabActionType.SortWaste, LabActionType.CleanBench, LabActionType.RecordHandoff }) sim.Submit(new(type));
    var restored = LabSimulation.Restore(sim.Save());
    Assert(restored.BuildReport().Lessons.Any(l => l.Explanation.Contains("Personal protective equipment")), "debrief must reconstruct action science from the ledger");
    Assert(restored.BuildReport().Lessons.SequenceEqual(sim.BuildReport().Lessons), "resume must preserve deferred lessons");
}
static void ExpectThrows(Action action, string message) { try { action(); } catch (ArgumentException) { return; } throw new Exception(message); }
static void ForwardSequenceRejectsOutOfOrderActionsAtomically()
{
    var sim = NewSimulation(); Prepare(sim); var before = sim.Snapshot();
    var result = sim.Submit(new(LabActionType.MoveToSource));
    Assert(!result.Accepted && sim.Snapshot().PipetteStage == before.PipetteStage && sim.Snapshot().AspiratedVolumeUl == 0, "source movement must wait for the in-air first stop without mutating the cycle");
}
static void FirstStopTransfersNominalAndSecondStopAddsNone()
{
    var sim = NewSimulation(); Prepare(sim); ForwardLoad(sim, "Blank");
    Assert(sim.Submit(new(LabActionType.MoveToDestination, "B1")).Accepted, "destination should bind after loading");
    Assert(sim.Submit(new(LabActionType.PressFirstStop)).Accepted, "destination first stop should be accepted");
    var afterFirst = sim.Snapshot(); Assert(afterFirst.Wells.Single(w => w.Well == "B1").TransferVolumeUl == 50 && afterFirst.AspiratedVolumeUl == 0, "first stop should deliver the nominal tracked quantity");
    Assert(sim.Submit(new(LabActionType.PressSecondStop)).Accepted, "second stop should clear the residue state");
    Assert(sim.Snapshot().Wells.Single(w => w.Well == "B1").TransferVolumeUl == 50, "second stop must not add invented microlitres");
}
static void FastReleaseIsQualitativeAndRecoverable()
{
    var sim = NewSimulation(); Prepare(sim); sim.Submit(new(LabActionType.SelectSource, "Blank")); sim.Submit(new(LabActionType.PressFirstStop)); sim.Submit(new(LabActionType.MoveToSource));
    Assert(sim.Submit(new(LabActionType.ReleaseFast)).Accepted, "fast release branch should be recorded");
    var state = sim.Snapshot(); Assert(state.AspiratedVolumeUl == 50 && state.SourceRemainingVolumeUl["Blank"] == 200 && state.Issues.Any(i => i.Code == "FastAspirationRelease" && i.Critical), "fast release must preserve nominal bookkeeping while blocking support qualitatively");
    CompleteForwardTransfer(sim, "B1"); TransferAll(sim, ["B1"]); Measure(sim); Assert(!sim.BuildReport().CanSupportConclusion, "an unresolved qualitative fast-release condition must block a supported conclusion");
    var retry = NewSimulation(); Prepare(retry); retry.Submit(new(LabActionType.SelectSource, "Blank")); retry.Submit(new(LabActionType.PressFirstStop)); retry.Submit(new(LabActionType.MoveToSource)); retry.Submit(new(LabActionType.ReleaseFast)); retry.Submit(new(LabActionType.RetryTransferCheckpoint)); Assert(retry.Snapshot().Issues.Any(i => i.Code == "FastAspirationRelease" && i.Resolved), "guided retry retains and resolves the fast-release history");
}
static void DestinationBindingSurvivesSelectionChange()
{
    var sim = NewSimulation(); Prepare(sim); ForwardLoad(sim, "Blank"); sim.Submit(new(LabActionType.MoveToDestination, "B1"));
    Assert(!sim.Submit(new(LabActionType.MoveToDestination, "B2")).Accepted, "an in-flight transfer cannot retarget a different well"); sim.Submit(new(LabActionType.PressFirstStop));
    Assert(sim.Snapshot().Wells.Single(w => w.Well == "B1").TransferVolumeUl == 50 && sim.Snapshot().Wells.Single(w => w.Well == "B2").TransferVolumeUl is null, "bound destination must receive the nominal transfer");
}
static void ReaderRejectsActivePipetteCycle()
{
    var sim = NewSimulation(); Prepare(sim); ForwardLoad(sim, "Blank"); sim.Submit(new(LabActionType.LoadPlate, "correct")); sim.Submit(new(LabActionType.ConfigureReader, "Luminescence"));
    Assert(!sim.Submit(new(LabActionType.RunReader)).Accepted, "reader must not run while a tip contains a cycle in progress");
}
static void InsufficientInventoryRejectsBeforeCycleLock()
{
    var sim = NewSimulation(); Prepare(sim); sim.Submit(new(LabActionType.SetVolume, Value: 250)); sim.Submit(new(LabActionType.SelectSource, "Blank"));
    ForwardLoad(sim, "Blank"); CompleteForwardTransfer(sim, "B1");
    var result = sim.Submit(new(LabActionType.PressFirstStop)); Assert(!result.Accepted && sim.Snapshot().PipetteStage == PipetteStage.ReadyInAir, "insufficient source inventory must not trap a learner in a cycle");
    Assert(sim.Submit(new(LabActionType.SetVolume, Value: 50)).Accepted, "learner can correct the nominal setting in air");
}
static void CurrentSaveRestoresDetailedPipetteState()
{
    var sim = NewSimulation(); Prepare(sim); Assert(sim.Submit(new(LabActionType.SelectSource, "Blank")).Accepted, "source selection should be accepted before the forward cycle");

    sim = RestoreStage(sim, PipetteStage.ReadyInAir, "Blank", null, null, 0, 250);
    sim = RestoreStage(sim, PipetteStage.FirstStopInAir, "Blank", null, null, 0, 250, LabActionType.PressFirstStop);
    sim = RestoreStage(sim, PipetteStage.FirstStopInSource, "Blank", null, null, 0, 250, LabActionType.MoveToSource);
    sim = RestoreStage(sim, PipetteStage.LoadedAtSource, "Blank", "Blank", null, 50, 200, LabActionType.ReleaseSlow);
    sim = RestoreStage(sim, PipetteStage.LoadedAtDestination, "Blank", "Blank", "B1", 50, 200, LabActionType.MoveToDestination, "B1");
    sim = RestoreStage(sim, PipetteStage.FirstStopAtDestination, "Blank", "Blank", "B1", 0, 200, LabActionType.PressFirstStop);
    sim = RestoreStage(sim, PipetteStage.SecondStopAtDestination, "Blank", "Blank", "B1", 0, 200, LabActionType.PressSecondStop);
    sim.Submit(new(LabActionType.WithdrawAndRelease));
    sim = RestoreStage(sim, PipetteStage.ReadyInAir, "Blank", null, null, 0, 200);

    TransferAll(sim, ["B1"]); Measure(sim);
    var uninterrupted = NewSimulation(); Prepare(uninterrupted); TransferAll(uninterrupted); Measure(uninterrupted);
    Assert(sim.BuildReport().Wells.Select(w => (w.RawReading, w.BackgroundAdjusted, w.RelativeToVehicle)).SequenceEqual(uninterrupted.BuildReport().Wells.Select(w => (w.RawReading, w.BackgroundAdjusted, w.RelativeToVehicle))), "flow continued after each restore must reproduce the uninterrupted final readings");
}
static LabSimulation RestoreStage(LabSimulation simulation, PipetteStage expectedStage, string expectedSelectedSource, string? expectedLoadedSource, string? expectedDestination, double expectedAspiratedVolume, double expectedSourceRemaining, LabActionType? action = null, string? target = null)
{
    if (action is not null) Assert(simulation.Submit(new LearnerAction(action.Value, target)).Accepted, $"{expectedStage} action should be accepted");
    var saved = simulation.Save(); var restored = LabSimulation.Restore(saved); var state = restored.Snapshot();
    Assert(restored.Save() == saved, $"{expectedStage} restore must preserve the complete observable state");
    Assert(state.PipetteStage == expectedStage && state.SelectedSource == expectedSelectedSource && state.LoadedSource == expectedLoadedSource && state.BoundDestination == expectedDestination && state.AspiratedVolumeUl == expectedAspiratedVolume && state.SourceRemainingVolumeUl["Blank"] == expectedSourceRemaining, $"{expectedStage} restore must retain the frozen source, destination, and nominal quantities");
    return restored;
}
static void HistoricalFixtureRemainsPreservedAndViewOnly()
{
    const string fixture = """
    {
      "FormatVersion": 2,
      "ModelVersion": "1.0.1",
      "Snapshot": {
        "AttemptId": "d6d70cb9539a4c30a457fad26efdf7ec",
        "Mode": "GuidedPractice",
        "Phase": "Transfers",
        "Wells": [],
        "Issues": [],
        "Ledger": [
          { "SimulatedMinute": 5, "AttemptNumber": 1, "Action": "Aspirate", "Target": "Blank", "Outcome": "Accepted", "Value": 50 },
          { "SimulatedMinute": 6, "AttemptNumber": 1, "Action": "Dispense", "Target": "B1", "Outcome": "Accepted", "Value": 50 }
        ]
      }
    }
    """;
    var before = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fixture)); var historical = LabSimulation.ReadHistorical(fixture); var after = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fixture));
    Assert(before.SequenceEqual(after) && historical.FormatVersion == 2 && historical.ModelVersion == "1.0.1" && historical.Snapshot.Ledger.Select(x => x.Action).SequenceEqual(["Aspirate", "Dispense"]), "the representative coarse historical ledger must be viewable without mutation or replay");
    ExpectThrows(() => LabSimulation.Restore(fixture), "current model must not resume a historical fixture");
}
static void DefaultRulesClassifyFastReleaseAsCritical()
{
    var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "scenario.v1.json"); var sim = LabSimulation.FromScenarioJson(File.ReadAllText(path)); Prepare(sim); sim.Submit(new(LabActionType.SelectSource, "Blank")); sim.Submit(new(LabActionType.PressFirstStop)); sim.Submit(new(LabActionType.MoveToSource)); sim.Submit(new(LabActionType.ReleaseFast));
    Assert(sim.Snapshot().Issues.Any(i => i.Code == "FastAspirationRelease" && i.Critical), "default construction must keep fast release critical");
}
static void TipEjectionRespectsCycle()
{
    var sim = NewSimulation(); Prepare(sim); Assert(sim.Submit(new(LabActionType.EjectTip)).Accepted && !sim.Snapshot().TipAttached, "an empty attached tip can be ejected in air"); sim.Submit(new(LabActionType.AttachTip)); ForwardLoad(sim, "Blank");
    var before = sim.Snapshot(); Assert(!sim.Submit(new(LabActionType.EjectTip)).Accepted && sim.Snapshot().TipAttached && sim.Snapshot().PipetteStage == before.PipetteStage, "a loaded tip cannot be ejected to bypass a cycle"); CompleteForwardTransfer(sim, "B1"); Assert(sim.Submit(new(LabActionType.EjectTip)).Accepted && !sim.Snapshot().TipAttached, "ejection returns only after the complete forward cycle");
}
static void AssessmentDefersFastReleaseCoaching()
{
    var sim = NewSimulation(); Prepare(sim, RunMode.Assessment); sim.Submit(new(LabActionType.SelectSource, "Blank")); sim.Submit(new(LabActionType.PressFirstStop)); sim.Submit(new(LabActionType.MoveToSource)); var result = sim.Submit(new(LabActionType.ReleaseFast));
    Assert(result.Accepted && result.ScienceTitle == "Assessment record" && !result.Message.Contains("fast", StringComparison.OrdinalIgnoreCase) && sim.Snapshot().Issues.Any(i => i.Code == "FastAspirationRelease"), "Assessment must record the fast condition while holding coaching for debrief");
}
static void PipettingQualityTeachingKeepsGuidanceOptionalAndAssessmentDefersIt()
{
    var sourced = new[] { new SourceEntry { Id = "eppendorf-liquid-handling-sop", Label = "SOP", Url = "https://example.test/sop", Use = "test" } };
    var guided = PipettingQualityTeaching.OptionalWhy(RunMode.GuidedPractice, sourced);
    var assessment = PipettingQualityTeaching.OptionalWhy(RunMode.Assessment, sourced);
    var debrief = PipettingQualityTeaching.DebriefCard(sourced);
    Assert(guided is not null && guided.Title.Contains("accuracy and precision", StringComparison.OrdinalIgnoreCase) && guided.Body.Contains("45, 45, 45") && guided.Body.Contains("not proof of volume accuracy"), "Guided Practice must expose the original, bounded optional accuracy-and-precision lesson");
    Assert(assessment is null, "Assessment must defer the optional pipetting-quality teaching until debrief");
    Assert(PipettingQualityTeaching.OptionalWhy(RunMode.GuidedPractice, []) is null && PipettingQualityTeaching.DebriefCard([]) is null && PipettingQualityTeaching.HtmlCard([]) is null, "an existing source manifest without the SOP entry must not add the new UI or HTML teaching card");
    Assert(debrief is not null && debrief.Body.Contains("gravimetric") && debrief.Body.Contains("density") && debrief.Body.Contains("does not provide calibration instructions") && !debrief.Body.Contains("tolerance", StringComparison.OrdinalIgnoreCase), "the shared debrief must describe controlled gravimetric checking only as a bounded concept");
}
static void SavedSourceManifestsPreserveLegacyAbsenceOfSopEntry()
{
    var legacySources = new SourcesManifest { SchemaVersion = 1, Entries = [new SourceEntry { Id = "legacy", Label = "Legacy source", Url = "https://example.test/legacy", Use = "Preserved source." }] };
    var scenarioPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "data", "scenario.v1.json");
    var scenario = JsonSerializer.Deserialize<ScenarioDefinition>(File.ReadAllText(scenarioPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    var sim = new LabSimulation(scenario, sources: legacySources);
    sim.Start(RunMode.GuidedPractice);
    var saved = sim.Save();
    var restored = LabSimulation.Restore(saved);
    Assert(saved == restored.Save() && restored.BuildReport().Sources.Select(source => source.Id).SequenceEqual(["legacy"]) && !saved.Contains("eppendorf-liquid-handling-sop", StringComparison.Ordinal), "opening a legacy attempt must retain its original source manifest without retroactive SOP injection");
}
static void ForwardLoad(LabSimulation sim, string source)
{
    sim.Submit(new(LabActionType.SelectSource, source)); Assert(sim.Submit(new(LabActionType.PressFirstStop)).Accepted, "first stop in air should be accepted"); Assert(sim.Submit(new(LabActionType.MoveToSource)).Accepted, "move to source should be accepted"); Assert(sim.Submit(new(LabActionType.ReleaseSlow)).Accepted, "slow immersed release should load nominal quantity");
}
static void CompleteForwardTransfer(LabSimulation sim, string well)
{
    Assert(sim.Submit(new(LabActionType.MoveToDestination, well)).Accepted, "destination should bind"); Assert(sim.Submit(new(LabActionType.PressFirstStop)).Accepted, "destination first stop should transfer nominal quantity"); Assert(sim.Submit(new(LabActionType.PressSecondStop)).Accepted, "second stop should clear the cycle"); Assert(sim.Submit(new(LabActionType.WithdrawAndRelease)).Accepted, "withdraw/release should return to air");
}
