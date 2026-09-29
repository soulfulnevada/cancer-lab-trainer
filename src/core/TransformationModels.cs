namespace CancerLabTrainer.Transformation;

public enum TransformationRunMode { GuidedPractice, Assessment }
public enum TransformationPhase { Intro, TubeSetup, PlateSetup, Predictions, TimeJump, Observe, Explain, DiagnosticCase, Debrief, Complete }
public enum TransformationGrowth { NotViewed, Present, Absent }
public enum TransformationFluorescence { NotViewed, Detected, NoneDetected, NotAssessable }
public enum TransformationActionType
{
    AcknowledgeIntro, LabelTube, UseFreshTip, SelectTubeSource, SelectTubeDestination, LoadTube, CorrectTubeContent, RecoverTubePreparation, AssignPlate, CorrectPlateAssignment, CommitPrediction, AdvanceConceptualTime,
    ViewPlate, RecordExplanation, StartDiagnosticCase, ViewCasePlate, ViewEvidenceCard, RecordDiagnosticDecision,
    RequestHint, RevisePrediction, RecordCleanup, RecordWasteDecision, RecordDebrief, CompleteDebrief
}

public sealed record TransformationAction(TransformationActionType Type, string? Target = null, string? Value = null);

public sealed class TransformationScenarioDefinition
{
    public int SchemaVersion { get; set; }
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public int Seed { get; set; }
    public string TrainingNotice { get; set; } = "";
    public List<TransformationPlateDefinition> Plates { get; set; } = [];
}
public sealed class TransformationPlateDefinition { public string Id { get; set; } = ""; public string Label { get; set; } = ""; public string ExpectedAssignment { get; set; } = ""; }
public sealed class TransformationLessonsDefinition { public int SchemaVersion { get; set; } public List<TransformationLessonDefinition> Lessons { get; set; } = []; }
public sealed class TransformationLessonDefinition { public string Id { get; set; } = ""; public string Title { get; set; } = ""; public string Text { get; set; } = ""; }
public sealed class TransformationCasesDefinition { public int SchemaVersion { get; set; } public List<TransformationCaseDefinition> Cases { get; set; } = []; }
public sealed class TransformationCaseDefinition { public string Id { get; set; } = ""; public string Title { get; set; } = ""; public string FictionalCause { get; set; } = ""; public TransformationCaseSetup Setup { get; set; } = new(); public Dictionary<string, TransformationCasePlateOutcome> Outcomes { get; set; } = []; public bool SelectionClaimSupported { get; set; } public bool ExpressionClaimSupported { get; set; } }
public sealed class TransformationCaseSetup { public Dictionary<string, string> TubeContents { get; set; } = []; public Dictionary<string, string> PlateAssignments { get; set; } = []; public Dictionary<string, string> IntendedPlateLabels { get; set; } = []; }
public sealed class TransformationCasePlateOutcome { public TransformationGrowth Growth { get; set; } public TransformationFluorescence Fluorescence { get; set; } }
public sealed class TransformationRubricDefinition { public int SchemaVersion { get; set; } public List<string> Dimensions { get; set; } = []; public Dictionary<string, string> Criteria { get; set; } = []; }
public sealed class TransformationSourcesDefinition { public int SchemaVersion { get; set; } public List<TransformationSourceEntry> Entries { get; set; } = []; }
public sealed class TransformationSourceEntry { public string Id { get; set; } = ""; public string Label { get; set; } = ""; public string Url { get; set; } = ""; public string Use { get; set; } = ""; }

public sealed class TransformationPrediction { public string PlateId { get; set; } = ""; public string OriginalGrowthPrediction { get; set; } = ""; public string OriginalFluorescencePrediction { get; set; } = ""; public List<string> Revisions { get; set; } = []; }
public sealed class TransformationPlateState { public string Id { get; set; } = ""; public string Label { get; set; } = ""; public string TubeLabel { get; set; } = ""; public string Medium { get; set; } = ""; public TransformationGrowth Growth { get; set; } = TransformationGrowth.NotViewed; public TransformationFluorescence Fluorescence { get; set; } = TransformationFluorescence.NotViewed; public string ActiveView { get; set; } = ""; public bool NormalViewSeen { get; set; } public bool ExcitationViewSeen { get; set; } }
public sealed class TransformationActionRecord { public TransformationActionType Type { get; set; } public string? Target { get; set; } public string? Value { get; set; } public bool Accepted { get; set; } public string Outcome { get; set; } = ""; }
public sealed class TransformationAttemptSnapshot
{
    public string AttemptId { get; set; } = "";
    public TransformationRunMode Mode { get; set; }
    public TransformationPhase Phase { get; set; }
    public bool Started { get; set; }
    public int ScenarioSeed { get; set; }
    public Dictionary<string, string> TubeContents { get; set; } = [];
    public Dictionary<string, string> TubeLabels { get; set; } = [];
    public bool FreshTipReady { get; set; }
    public string? SelectedTubeSource { get; set; }
    public string? SelectedTubeDestination { get; set; }
    public List<string> TubeSetupCorrections { get; set; } = [];
    public List<string> PlateAssignmentCorrections { get; set; } = [];
    public List<string> PrelockPredictionCorrections { get; set; } = [];
    public List<TransformationPlateState> Plates { get; set; } = [];
    public List<TransformationPlateState> CasePlates { get; set; } = [];
    public List<TransformationPrediction> Predictions { get; set; } = [];
    public bool PredictionsLocked { get; set; }
    public List<string> HintsUsed { get; set; } = [];
    public List<string> ExplanationChoices { get; set; } = [];
    public List<string> ExplanationChoiceHistory { get; set; } = [];
    public TransformationCaseSetup? DiagnosticSetup { get; set; }
    public string? DiagnosticCaseId { get; set; }
    public List<string> DiagnosticDecisions { get; set; } = [];
    public List<string> DiagnosticDecisionHistory { get; set; } = [];
    public List<string> EvidenceViewed { get; set; } = [];
    public List<string> EvidenceCardsViewed { get; set; } = [];
    public List<TransformationActionRecord> Ledger { get; set; } = [];
    public bool CleanupRecorded { get; set; }
    public bool WasteDecisionRecorded { get; set; }
    public bool DebriefDocumented { get; set; }
    public string? RetryOfAttemptId { get; set; }
}
public sealed class TransformationActionResult { public bool Accepted { get; set; } public string Message { get; set; } = ""; public string Coaching { get; set; } = ""; public TransformationAttemptSnapshot Snapshot { get; set; } = new(); }
public sealed class TransformationDiagnosticReport { public string Id { get; set; } = ""; public string Title { get; set; } = ""; public string? FinalFictionalCause { get; set; } public bool SelectionClaimSupported { get; set; } public bool ExpressionClaimSupported { get; set; } public string SelectionClaimReason { get; set; } = ""; public string ExpressionClaimReason { get; set; } = ""; }
public sealed class TransformationReport
{
    public string ApplicationVersion { get; set; } = "1.2.1";
    public string ModelVersion { get; set; } = TransformationSimulation.ModelVersion;
    public int SaveFormatVersion { get; set; } = 1;
    public string AttemptId { get; set; } = "";
    public TransformationRunMode Mode { get; set; }
    public int ScenarioSeed { get; set; }
    public bool Complete { get; set; }
    public List<TransformationPlateState> Plates { get; set; } = [];
    public Dictionary<string, string> TubeContents { get; set; } = [];
    public Dictionary<string, string> TubeLabels { get; set; } = [];
    public List<string> TubeSetupCorrections { get; set; } = [];
    public List<string> PlateAssignmentCorrections { get; set; } = [];
    public List<TransformationPlateState> CaseObservations { get; set; } = [];
    public List<TransformationPrediction> Predictions { get; set; } = [];
    public List<string> PrelockPredictionCorrections { get; set; } = [];
    public List<string> HintsUsed { get; set; } = [];
    public List<string> ExplanationChoices { get; set; } = [];
    public List<string> ExplanationChoiceHistory { get; set; } = [];
    public TransformationCaseSetup? CaseSetup { get; set; }
    public List<string> DiagnosticDecisions { get; set; } = [];
    public List<string> DiagnosticDecisionHistory { get; set; } = [];
    public List<string> EvidenceViewed { get; set; } = [];
    public List<string> EvidenceCardsViewed { get; set; } = [];
    public string? RetryOfAttemptId { get; set; }
    public List<TransformationActionRecord> Ledger { get; set; } = [];
    public TransformationDiagnosticReport? DiagnosticCase { get; set; }
    public Dictionary<string, string> ClaimReasons { get; set; } = [];
    public bool MainSelectionClaimSupported { get; set; }
    public bool MainExpressionClaimSupported { get; set; }
    public string Limits { get; set; } = "This fictional model does not demonstrate wet-lab competence. Growth on antibiotic media does not show that every cell was transformed; nonfluorescence does not establish absence of GFP DNA.";
    public List<TransformationSourceEntry> Sources { get; set; } = [];
    public Dictionary<string, string> RubricStatus { get; set; } = [];
}
public sealed class PersistedTransformationAttempt
{
    public int FormatVersion { get; set; } = 1;
    public string ModelVersion { get; set; } = TransformationSimulation.ModelVersion;
    public TransformationScenarioDefinition Scenario { get; set; } = new();
    public TransformationLessonsDefinition Lessons { get; set; } = new();
    public TransformationCasesDefinition Cases { get; set; } = new();
    public TransformationRubricDefinition Rubric { get; set; } = new();
    public TransformationSourcesDefinition Sources { get; set; } = new();
    public TransformationAttemptSnapshot Snapshot { get; set; } = new();
}
public sealed record TransformationExportPaths(string JsonPath, string CsvPath, string HtmlPath);
