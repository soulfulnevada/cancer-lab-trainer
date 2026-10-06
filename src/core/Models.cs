using System.Text.Json.Serialization;

namespace CancerLabTrainer.Core;

public enum RunMode { GuidedPractice, Assessment }

public enum LabPhase { Welcome, Preparation, Organization, Transfers, Measurement, Interpretation, Closeout, Complete }

/// <summary>Observable forward-pipetting positions in the illustrative model.</summary>
public enum PipetteStage
{
    ReadyInAir,
    FirstStopInAir,
    FirstStopInSource,
    LoadedAtSource,
    LoadedAtDestination,
    FirstStopAtDestination,
    SecondStopAtDestination
}

public enum LabActionType
{
    WearPpe, DisinfectBench, CheckMaterials, VerifyLabels, ReviewPlateMap,
    AttachTip, EjectTip, ChangeTip, SetVolume, SelectSource,
    PressFirstStop, MoveToSource, ReleaseSlow, ReleaseFast, MoveToDestination, PressSecondStop, WithdrawAndRelease,
    Aspirate, Dispense, MislabelSelectedWell, CorrectLabel, RetryTransferCheckpoint, LoadPlate, ConfigureReader,
    RunReader, ReviewResults, DecideSupportedConclusion, EscalateInvalidRun,
    SortWaste, CleanBench, RecordHandoff, AnswerCheck
}

public sealed record LearnerAction(LabActionType Type, string? Target = null, double? Value = null);

public sealed class ActiveWellDefinition
{
    public string Well { get; set; } = "";
    public string Role { get; set; } = "";
    public double ExpectedSignal { get; set; }
}

public sealed class ScenarioDefinition
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public int Seed { get; set; }
    public string TrainingNotice { get; set; } = "";
    public double ToyTransferVolumeUl { get; set; }
    public string ReaderMode { get; set; } = "Luminescence";
    public List<ActiveWellDefinition> ActiveWells { get; set; } = [];
}

public sealed class TrainingRulesDefinition
{
    public int SchemaVersion { get; set; }
    public bool IllustrativeOnly { get; set; }
    public List<string> CriticalErrors { get; set; } = [];
    public List<string> AssessmentCategories { get; set; } = [];
    public double MaxReplicateCvPercent { get; set; } = 20;
    public double MinimumReferenceAdjustedSignal { get; set; } = 100;
    public List<ComprehensionCheck> ComprehensionChecks { get; set; } = [];
}

/// <summary>A multiple-choice check. "follow-up" opens after results are reviewed; "debrief" opens once the attempt is complete.</summary>
public sealed class ComprehensionCheck
{
    public string Id { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Prompt { get; set; } = "";
    public List<CheckOption> Options { get; set; } = [];
    public string CorrectOptionId { get; set; } = "";
    public string Explanation { get; set; } = "";
}

public sealed class CheckOption
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
}

public sealed class CheckAnswer
{
    public string QuestionId { get; set; } = "";
    public string OptionId { get; set; } = "";
    public bool Correct { get; set; }
}

public sealed class SourcesManifest
{
    public int SchemaVersion { get; set; }
    public List<SourceEntry> Entries { get; set; } = [];
}

public sealed class SourceEntry
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
    public string Use { get; set; } = "";
}

public sealed class SimulationIssue
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    public bool Critical { get; set; }
    public bool Recoverable { get; set; }
    public bool Resolved { get; set; }
    public string? Target { get; set; }
}

public sealed class WellRunState
{
    public string Well { get; set; } = "";
    public string Role { get; set; } = "";
    public double ExpectedSignal { get; set; }
    public double? TransferVolumeUl { get; set; }
    public int? TipId { get; set; }
    public string? SourceRole { get; set; }
    public double CarryoverFactor { get; set; } = 1;
    public Dictionary<string, double> ContributionsUl { get; set; } = [];
    public double? RawReading { get; set; }
    public double? BackgroundAdjusted { get; set; }
    public double? RelativeToVehicle { get; set; }
}

public sealed class LedgerEvent
{
    public int SimulatedMinute { get; set; }
    public int AttemptNumber { get; set; }
    public string Action { get; set; } = "";
    public string? Target { get; set; }
    public string Outcome { get; set; } = "";
    public double? Value { get; set; }
}

public sealed record ReplicateSummary(string Role, int Count, double Mean, double StandardDeviation, double? CvPercent);
public sealed record ScienceLesson(string Title, string Explanation, string WhyItMatters);

public sealed class ActionResult
{
    public bool Accepted { get; set; }
    public string Message { get; set; } = "";
    public string ScienceTitle { get; set; } = "";
    public string ScienceLesson { get; set; } = "";
    public string WhyItMatters { get; set; } = "";
    public SimulationSnapshot Snapshot { get; set; } = new();
}

public sealed class SimulationSnapshot
{
    public string ScenarioId { get; set; } = "";
    public int ScenarioSchemaVersion { get; set; }
    public string AttemptId { get; set; } = "";
    public RunMode Mode { get; set; }
    public LabPhase Phase { get; set; }
    public bool Started { get; set; }
    public bool PpeWorn { get; set; }
    public bool BenchDisinfected { get; set; }
    public bool MaterialsChecked { get; set; }
    public bool LabelsVerified { get; set; }
    public bool PlateMapReviewed { get; set; }
    public bool TipAttached { get; set; }
    public int TipId { get; set; }
    public PipetteStage PipetteStage { get; set; } = PipetteStage.ReadyInAir;
    public double? SelectedVolumeUl { get; set; }
    public string? SelectedSource { get; set; }
    public string? LoadedSource { get; set; }
    public string? BoundDestination { get; set; }
    public double AspiratedVolumeUl { get; set; }
    public double DiscardedVolumeUl { get; set; }
    public string? TipContaminationRole { get; set; }
    public bool CarryoverRisk { get; set; }
    public Dictionary<string, double> SourceRemainingVolumeUl { get; set; } = [];
    public int SimulatedMinutes { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public List<LedgerEvent> Ledger { get; set; } = [];
    public bool PlateLoaded { get; set; }
    public bool CorrectOrientation { get; set; }
    public bool ReaderConfigured { get; set; }
    public bool ReaderRan { get; set; }
    public bool ResultsReviewed { get; set; }
    public bool Escalated { get; set; }
    public bool WasteSorted { get; set; }
    public bool BenchCleanedAtCloseout { get; set; }
    public bool HandoffRecorded { get; set; }
    public bool ControlsValid { get; set; }
    public bool CanSupportConclusion { get; set; }
    public string? InterpretationDecision { get; set; }
    public List<WellRunState> Wells { get; set; } = [];
    public List<SimulationIssue> Issues { get; set; } = [];
    public List<CheckAnswer> CheckAnswers { get; set; } = [];
}

public sealed class LabReport
{
    public string ModelVersion { get; set; } = LabSimulation.ModelVersion;
    public string ScenarioId { get; set; } = "";
    public int ScenarioSchemaVersion { get; set; }
    public RunMode Mode { get; set; }
    public bool Complete { get; set; }
    public bool ControlsValid { get; set; }
    public bool CanSupportConclusion { get; set; }
    public bool Escalated { get; set; }
    public string AttemptId { get; set; } = "";
    public int AttemptNumber { get; set; }
    public int ScenarioSeed { get; set; }
    public int RulesSchemaVersion { get; set; }
    public int SourcesSchemaVersion { get; set; }
    public string? InterpretationDecision { get; set; }
    public string Conclusion { get; set; } = "";
    public Dictionary<string, string> CategoryStatus { get; set; } = [];
    public List<WellRunState> Wells { get; set; } = [];
    public List<SimulationIssue> Issues { get; set; } = [];
    public List<LedgerEvent> Ledger { get; set; } = [];
    public List<SourceEntry> Sources { get; set; } = [];
    public List<ReplicateSummary> Replicates { get; set; } = [];
    public List<ScienceLesson> Lessons { get; set; } = [];
    public List<CheckAnswer> CheckAnswers { get; set; } = [];
    public string ScientificLimit { get; set; } = "ATP-associated luminescence is a viability proxy and does not establish a cell-death mechanism. Stored source and well quantities are nominal training amounts, not physical volume measurements or calibration results.";
}

public sealed class PersistedSession
{
    public int FormatVersion { get; set; } = 3;
    public string ModelVersion { get; set; } = LabSimulation.ModelVersion;
    public ScenarioDefinition Scenario { get; set; } = new();
    public TrainingRulesDefinition Rules { get; set; } = new();
    public SourcesManifest Sources { get; set; } = new();
    public SimulationSnapshot Snapshot { get; set; } = new();
}

/// <summary>A preserved v1.0.1 attempt. It is intentionally display-only in this model.</summary>
public sealed class HistoricalAttempt
{
    public int FormatVersion { get; init; }
    public string ModelVersion { get; init; } = "";
    public SimulationSnapshot Snapshot { get; init; } = new();
}
