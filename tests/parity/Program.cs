using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CancerLabTrainer.Core;
using CancerLabTrainer.Transformation;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var output = args.Length == 1 ? Path.GetFullPath(args[0]) : Path.Combine(root, "tests", "parity", "fixtures", "csharp-oracle.json");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
var oracle = new Dictionary<string, object?>
{
    ["schemaVersion"] = 1,
    ["level1"] = RunLevel1(root),
    ["level1Edges"] = RunLevel1Edges(root),
    ["csharpPersistenceSamples"] = RunCSharpPersistenceSamples(root),
    ["level2"] = RunLevel2(root)
};
var serialized = JsonSerializer.Serialize(oracle, options);
serialized = Regex.Replace(serialized, "\"attemptId\": \"[0-9a-f]{32}\"", "\"attemptId\": \"00000000000000000000000000000000\"");
serialized = Regex.Replace(serialized, "\"retryOfAttemptId\": \"[0-9a-f]{32}\"", "\"retryOfAttemptId\": \"00000000000000000000000000000000\"");
serialized = CanonicalizeOracleJson(serialized);
File.WriteAllText(output, serialized);
Console.WriteLine($"PASS: C# oracle wrote {output}");

static object RunLevel1(string root)
{
    var runs = new Dictionary<string, object>();
    foreach (var mode in new[] { RunMode.GuidedPractice, RunMode.Assessment }) runs[mode.ToString()] = RunLevel1Trace(root, mode);
    return runs;
}

static object RunLevel1Trace(string root, RunMode mode)
{
    var sim = LabSimulation.FromJsonDocuments(Read(root, "scenario.v1.json"), Read(root, "rules.v1.json"), Read(root, "sources.v1.json"));
    var trace = new List<object>();
    trace.Add(new { start = sim.Start(mode), report = sim.BuildReport() });
    Record(sim, trace, new(LabActionType.SetVolume, Value: 50));
    Record(sim, trace, new(LabActionType.WearPpe)); Record(sim, trace, new(LabActionType.DisinfectBench)); Record(sim, trace, new(LabActionType.CheckMaterials));
    Record(sim, trace, new(LabActionType.VerifyLabels)); Record(sim, trace, new(LabActionType.ReviewPlateMap));
    foreach (var (well, role) in new[] { ("B1", "Blank"), ("B2", "Blank"), ("B3", "Vehicle control"), ("B4", "Vehicle control"), ("B5", "Fictional treatment"), ("B6", "Fictional treatment") })
    {
        Record(sim, trace, new(LabActionType.AttachTip)); Record(sim, trace, new(LabActionType.SetVolume, Value: 50)); Record(sim, trace, new(LabActionType.SelectSource, role));
        Record(sim, trace, new(LabActionType.PressFirstStop)); Record(sim, trace, new(LabActionType.MoveToSource, role)); Record(sim, trace, new(LabActionType.ReleaseSlow));
        Record(sim, trace, new(LabActionType.MoveToDestination, well)); Record(sim, trace, new(LabActionType.PressFirstStop)); Record(sim, trace, new(LabActionType.PressSecondStop)); Record(sim, trace, new(LabActionType.WithdrawAndRelease)); Record(sim, trace, new(LabActionType.EjectTip));
    }
    Record(sim, trace, new(LabActionType.LoadPlate, "correct")); Record(sim, trace, new(LabActionType.ConfigureReader, "Luminescence")); Record(sim, trace, new(LabActionType.RunReader));
    Record(sim, trace, new(LabActionType.ReviewResults)); Record(sim, trace, new(LabActionType.DecideSupportedConclusion)); Record(sim, trace, new(LabActionType.SortWaste)); Record(sim, trace, new(LabActionType.CleanBench)); Record(sim, trace, new(LabActionType.RecordHandoff));
    var save = sim.Save();
    return new { trace, report = sim.BuildReport(), replayIdentical = LabSimulation.Restore(save).Save() == save, tamperRejected = Rejects(() => LabSimulation.Restore(save.Replace(sim.Snapshot().AttemptId, "../../outside", StringComparison.Ordinal)))};
}

static object RunLevel1Edges(string root) => new Dictionary<string, object>
{
    ["guidedRecovery"] = RunGuidedRecoveryTrace(root),
    ["assessmentInvalidRun"] = RunAssessmentInvalidTrace(root)
};

static object RunCSharpPersistenceSamples(string root)
{
    var level1 = LabSimulation.FromJsonDocuments(Read(root, "scenario.v1.json"), Read(root, "rules.v1.json"), Read(root, "sources.v1.json"));
    level1.Start(RunMode.GuidedPractice);
    var level2 = TransformationSimulation.FromJsonDocuments(Read(root, "transformation-scenario.v1.json"), Read(root, "transformation-lessons.v1.json"), Read(root, "transformation-cases.v1.json"), Read(root, "transformation-rubric.v1.json"), Read(root, "transformation-sources.v1.json"));
    var firstLevel2Attempt = level2.Start(TransformationRunMode.GuidedPractice);
    var normalLevel2Save = level2.Save();
    var linkedRetry = level2.StartLinkedRetry(firstLevel2Attempt.AttemptId, TransformationRunMode.Assessment);
    if (linkedRetry.RetryOfAttemptId != firstLevel2Attempt.AttemptId) throw new InvalidOperationException("Linked retry did not retain its parent attempt ID.");
    return new
    {
        level1 = NormalizeSaveIds(level1.Save()),
        level2 = NormalizeSaveIds(normalLevel2Save),
        level2LinkedRetry = NormalizeSaveIds(level2.Save())
    };
}

static string NormalizeSaveIds(string save)
{
    const string level1Attempt = "[0-9a-f]{32}";
    const string level2Attempt = "[0-9a-f]{32}";
    using var original = JsonDocument.Parse(save);
    var scenario = GetSaveProperty(original.RootElement, "Scenario", "scenario").GetRawText();
    var sources = GetSaveProperty(original.RootElement, "Sources", "sources").GetRawText();
    var normalized = Regex.Replace(save, "\"AttemptId\": \"" + level1Attempt + "\"", "\"AttemptId\": \"00000000000000000000000000000000\"");
    normalized = Regex.Replace(normalized, "\"attemptId\": \"" + level2Attempt + "\"", "\"attemptId\": \"00000000000000000000000000000000\"");
    normalized = Regex.Replace(normalized, "\"retryOfAttemptId\": \"" + level2Attempt + "\"", "\"retryOfAttemptId\": \"00000000000000000000000000000000\"");
    using var after = JsonDocument.Parse(normalized);
    if (GetSaveProperty(after.RootElement, "Scenario", "scenario").GetRawText() != scenario || GetSaveProperty(after.RootElement, "Sources", "sources").GetRawText() != sources)
        throw new InvalidOperationException("Persistence ID normalization changed scenario or source data.");
    return CanonicalizeOracleJson(normalized);
}

static string CanonicalizeOracleJson(string serialized) =>
    serialized.Replace("\r\n", "\n", StringComparison.Ordinal);

static JsonElement GetSaveProperty(JsonElement root, string pascalCase, string camelCase) =>
    root.TryGetProperty(pascalCase, out var pascal) ? pascal : root.GetProperty(camelCase);

static object RunGuidedRecoveryTrace(string root)
{
    var sim = LabSimulation.FromJsonDocuments(Read(root, "scenario.v1.json"), Read(root, "rules.v1.json"), Read(root, "sources.v1.json"));
    var trace = new List<object>();
    trace.Add(new { start = sim.Start(RunMode.GuidedPractice), report = sim.BuildReport() });
    Record(sim, trace, new(LabActionType.SetVolume, Value: 50));
    foreach (var action in new[] { LabActionType.WearPpe, LabActionType.DisinfectBench, LabActionType.CheckMaterials, LabActionType.VerifyLabels, LabActionType.ReviewPlateMap, LabActionType.AttachTip }) Record(sim, trace, new(action));
    Record(sim, trace, new(LabActionType.SetVolume, Value: 50));
    Record(sim, trace, new(LabActionType.SelectSource, "Blank"));
    Record(sim, trace, new(LabActionType.PressFirstStop));
    Record(sim, trace, new(LabActionType.MoveToSource, "Blank"));
    Record(sim, trace, new(LabActionType.ReleaseFast));
    Record(sim, trace, new(LabActionType.SelectSource, "Vehicle control"));
    Record(sim, trace, new(LabActionType.MoveToDestination, "B1"));
    Record(sim, trace, new(LabActionType.PressFirstStop));
    Record(sim, trace, new(LabActionType.PressSecondStop));
    Record(sim, trace, new(LabActionType.WithdrawAndRelease));
    Record(sim, trace, new(LabActionType.SelectSource, "Vehicle control"));
    Record(sim, trace, new(LabActionType.PressFirstStop));
    Record(sim, trace, new(LabActionType.MoveToSource, "Vehicle control"));
    Record(sim, trace, new(LabActionType.ReleaseSlow));
    Record(sim, trace, new(LabActionType.MoveToDestination, "B1"));
    Record(sim, trace, new(LabActionType.PressFirstStop));
    Record(sim, trace, new(LabActionType.PressSecondStop));
    Record(sim, trace, new(LabActionType.WithdrawAndRelease));
    Record(sim, trace, new(LabActionType.MislabelSelectedWell, "B1"));
    Record(sim, trace, new(LabActionType.CorrectLabel, "B1"));
    Record(sim, trace, new(LabActionType.RetryTransferCheckpoint));
    Record(sim, trace, new(LabActionType.LoadPlate, "rotated"));
    Record(sim, trace, new(LabActionType.LoadPlate, "correct"));
    Record(sim, trace, new(LabActionType.ConfigureReader, "Absorbance"));
    Record(sim, trace, new(LabActionType.ConfigureReader, "Luminescence"));
    var save = sim.Save();
    return new { trace, report = sim.BuildReport(), replayIdentical = LabSimulation.Restore(save).Save() == save };
}

static object RunAssessmentInvalidTrace(string root)
{
    var sim = LabSimulation.FromJsonDocuments(Read(root, "scenario.v1.json"), Read(root, "rules.v1.json"), Read(root, "sources.v1.json"));
    var trace = new List<object>();
    trace.Add(new { start = sim.Start(RunMode.Assessment), report = sim.BuildReport() });
    foreach (var action in new[] { LabActionType.WearPpe, LabActionType.DisinfectBench, LabActionType.CheckMaterials, LabActionType.VerifyLabels, LabActionType.ReviewPlateMap, LabActionType.AttachTip }) Record(sim, trace, new(action));
    Record(sim, trace, new(LabActionType.SetVolume, Value: 50));
    Record(sim, trace, new(LabActionType.SelectSource, "Blank"));
    Record(sim, trace, new(LabActionType.PressFirstStop));
    Record(sim, trace, new(LabActionType.MoveToSource, "Blank"));
    Record(sim, trace, new(LabActionType.ReleaseSlow));
    Record(sim, trace, new(LabActionType.MoveToDestination, "B1"));
    Record(sim, trace, new(LabActionType.PressFirstStop));
    Record(sim, trace, new(LabActionType.PressSecondStop));
    Record(sim, trace, new(LabActionType.WithdrawAndRelease));
    Record(sim, trace, new(LabActionType.LoadPlate, "wrong"));
    Record(sim, trace, new(LabActionType.ConfigureReader, "Luminescence"));
    Record(sim, trace, new(LabActionType.RunReader));
    Record(sim, trace, new(LabActionType.ReviewResults));
    Record(sim, trace, new(LabActionType.DecideSupportedConclusion));
    Record(sim, trace, new(LabActionType.EscalateInvalidRun));
    Record(sim, trace, new(LabActionType.SortWaste));
    Record(sim, trace, new(LabActionType.CleanBench));
    Record(sim, trace, new(LabActionType.RecordHandoff));
    var save = sim.Save();
    return new { trace, report = sim.BuildReport(), replayIdentical = LabSimulation.Restore(save).Save() == save };
}

static object RunLevel2(string root)
{
    var allModes = new Dictionary<string, object>();
    foreach (var mode in new[] { TransformationRunMode.GuidedPractice, TransformationRunMode.Assessment })
    {
        var allCases = new Dictionary<string, object>();
        foreach (var caseId in new[] { "missing-arabinose", "swapped-tube-labels", "missing-dna", "failed-antibiotic-negative-control" })
        {
            var sim = TransformationSimulation.FromJsonDocuments(Read(root, "transformation-scenario.v1.json"), Read(root, "transformation-lessons.v1.json"), Read(root, "transformation-cases.v1.json"), Read(root, "transformation-rubric.v1.json"), Read(root, "transformation-sources.v1.json"));
            var trace = new List<object>(); trace.Add(new { start = sim.Start(mode), report = sim.BuildReport() });
            RecordTransformation(sim, trace, new(TransformationActionType.AssignPlate, "P1", "T|LB+amp"));
            RecordTransformation(sim, trace, new(TransformationActionType.AcknowledgeIntro)); RecordTransformation(sim, trace, new(TransformationActionType.LabelTube, "T", "Transformation reaction")); RecordTransformation(sim, trace, new(TransformationActionType.LabelTube, "NC", "Negative control"));
            TransferTube(sim, trace, "T", "DNA"); TransferTube(sim, trace, "NC", "Buffer");
            foreach (var pair in new[] { ("P1", "T|LB+amp"), ("P2", "NC|LB+amp"), ("P3", "T|LB+amp+Ara"), ("P4", "NC|LB") }) RecordTransformation(sim, trace, new(TransformationActionType.AssignPlate, pair.Item1, pair.Item2));
            foreach (var plate in new[] { "P1", "P2", "P3", "P4" }) { RecordTransformation(sim, trace, new(TransformationActionType.CommitPrediction, plate, "growth:NotSure")); RecordTransformation(sim, trace, new(TransformationActionType.CommitPrediction, plate, "fluorescence:NotSure")); }
            RecordTransformation(sim, trace, new(TransformationActionType.AdvanceConceptualTime)); foreach (var plate in new[] { "P1", "P2", "P3", "P4" }) { RecordTransformation(sim, trace, new(TransformationActionType.ViewPlate, plate, "normal")); RecordTransformation(sim, trace, new(TransformationActionType.ViewPlate, plate, "excitation")); }
            foreach (var plate in new[] { "P1", "P2", "P3", "P4" }) RecordTransformation(sim, trace, new(TransformationActionType.RecordExplanation, plate, plate == "P2" ? "no-colonies-not-assessable" : plate == "P3" ? "green-consistent-gfp" : "nongreen-not-no-dna"));
            RecordTransformation(sim, trace, new(TransformationActionType.StartDiagnosticCase, caseId)); foreach (var plate in new[] { "P1", "P2", "P3", "P4" }) { RecordTransformation(sim, trace, new(TransformationActionType.ViewCasePlate, plate, "normal")); RecordTransformation(sim, trace, new(TransformationActionType.ViewCasePlate, plate, "excitation")); }
            foreach (var card in new[] { "setup-record", "control-comparison", "observation-limit" }) RecordTransformation(sim, trace, new(TransformationActionType.ViewEvidenceCard, card)); RecordTransformation(sim, trace, new(TransformationActionType.RecordDiagnosticDecision, "selection-supported", "control-pattern")); RecordTransformation(sim, trace, new(TransformationActionType.RecordDiagnosticDecision, "expression-withheld", "phenotype-not-cause")); RecordTransformation(sim, trace, new(TransformationActionType.RecordDiagnosticDecision, "uncertainty-cannot-prove", "phenotype-not-cause"));
            RecordTransformation(sim, trace, new(TransformationActionType.RecordCleanup)); RecordTransformation(sim, trace, new(TransformationActionType.RecordWasteDecision)); RecordTransformation(sim, trace, new(TransformationActionType.RecordDebrief)); RecordTransformation(sim, trace, new(TransformationActionType.CompleteDebrief));
            var save = sim.Save(); allCases[caseId] = new { trace, report = sim.BuildReport(), replayIdentical = TransformationSimulation.Restore(save).Save() == save, tamperRejected = Rejects(() => TransformationSimulation.Restore(save.Replace("\"formatVersion\": 1", "\"formatVersion\": 99", StringComparison.Ordinal))) };
        }
        allModes[mode.ToString()] = allCases;
    }
    return allModes;
}

static void Record(LabSimulation sim, List<object> trace, LearnerAction action) { var result = sim.Submit(action); trace.Add(new { action, accepted = result.Accepted, message = result.Message, scienceTitle = result.ScienceTitle, scienceLesson = result.ScienceLesson, whyItMatters = result.WhyItMatters, phase = result.Snapshot.Phase, snapshot = result.Snapshot, report = sim.BuildReport() }); }
static void RecordTransformation(TransformationSimulation sim, List<object> trace, TransformationAction action) { var result = sim.Submit(action); trace.Add(new { action, accepted = result.Accepted, message = result.Message, coaching = result.Coaching, phase = result.Snapshot.Phase, snapshot = result.Snapshot, report = sim.BuildReport() }); }
static void TransferTube(TransformationSimulation sim, List<object> trace, string tube, string source) { RecordTransformation(sim, trace, new(TransformationActionType.UseFreshTip)); RecordTransformation(sim, trace, new(TransformationActionType.SelectTubeSource, source)); RecordTransformation(sim, trace, new(TransformationActionType.SelectTubeDestination, tube)); RecordTransformation(sim, trace, new(TransformationActionType.LoadTube)); }
static bool Rejects(Action action) { try { action(); return false; } catch (ArgumentException) { return true; } }
static string Read(string root, string name) => File.ReadAllText(Path.Combine(root, "data", name));
