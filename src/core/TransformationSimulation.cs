using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CancerLabTrainer.Transformation;

/// <summary>Independent, qualitative Level 2 model. It intentionally contains no laboratory parameters or quantitative growth model.</summary>
public sealed class TransformationSimulation
{
    public const string ModelVersion = "1.0.0";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    private readonly TransformationScenarioDefinition _scenario;
    private readonly TransformationLessonsDefinition _lessons;
    private readonly TransformationCasesDefinition _cases;
    private readonly TransformationRubricDefinition _rubric;
    private readonly TransformationSourcesDefinition _sources;
    private TransformationAttemptSnapshot _state = new();
    private static readonly Dictionary<string, string> CanonicalCaseOutcomeSignatures = new()
    {
        ["missing-arabinose"] = "P1:Present:NoneDetected|P2:Absent:NotAssessable|P3:Present:NoneDetected|P4:Present:NoneDetected",
        ["swapped-tube-labels"] = "P1:Absent:NotAssessable|P2:Present:NoneDetected|P3:Absent:NotAssessable|P4:Present:NoneDetected",
        ["missing-dna"] = "P1:Absent:NotAssessable|P2:Absent:NotAssessable|P3:Absent:NotAssessable|P4:Present:NoneDetected",
        ["failed-antibiotic-negative-control"] = "P1:Present:NoneDetected|P2:Present:NoneDetected|P3:Present:Detected|P4:Present:NoneDetected"
    };
    private static readonly Dictionary<string, string> CanonicalCaseSetupSignatures = new()
    {
        ["missing-arabinose"] = "tubes:NC=Buffer|T=DNA;actual:P1=T|LB+amp|P2=NC|LB+amp|P3=T|LB+amp|P4=NC|LB;intended:P1=T|LB+amp|P2=NC|LB+amp|P3=T|LB+amp+Ara|P4=NC|LB",
        ["swapped-tube-labels"] = "tubes:NC=DNA|T=Buffer;actual:P1=T|LB+amp|P2=NC|LB+amp|P3=T|LB+amp+Ara|P4=NC|LB;intended:P1=T|LB+amp|P2=NC|LB+amp|P3=T|LB+amp+Ara|P4=NC|LB",
        ["missing-dna"] = "tubes:NC=Buffer|T=Buffer;actual:P1=T|LB+amp|P2=NC|LB+amp|P3=T|LB+amp+Ara|P4=NC|LB;intended:P1=T|LB+amp|P2=NC|LB+amp|P3=T|LB+amp+Ara|P4=NC|LB",
        ["failed-antibiotic-negative-control"] = "tubes:NC=Buffer|T=DNA;actual:P1=T|LB+amp|P2=NC|LB|P3=T|LB+amp+Ara|P4=NC|LB;intended:P1=T|LB+amp|P2=NC|LB+amp|P3=T|LB+amp+Ara|P4=NC|LB"
    };

    public TransformationSimulation(TransformationScenarioDefinition scenario, TransformationLessonsDefinition lessons, TransformationCasesDefinition cases, TransformationRubricDefinition rubric, TransformationSourcesDefinition sources)
    {
        ValidateDefinitions(scenario, lessons, cases, rubric, sources);
        _scenario = Clone(scenario); _lessons = Clone(lessons); _cases = Clone(cases); _rubric = Clone(rubric); _sources = Clone(sources);
    }
    public static TransformationSimulation FromJsonDocuments(string scenario, string lessons, string cases, string rubric, string sources) => new(
        Read<TransformationScenarioDefinition>(scenario), Read<TransformationLessonsDefinition>(lessons), Read<TransformationCasesDefinition>(cases), Read<TransformationRubricDefinition>(rubric), Read<TransformationSourcesDefinition>(sources));
    public TransformationAttemptSnapshot Start(TransformationRunMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Unknown learning mode.");
        _state = new TransformationAttemptSnapshot { AttemptId = Guid.NewGuid().ToString("N"), Mode = mode, Phase = TransformationPhase.Intro, Started = true, ScenarioSeed = _scenario.Seed, TubeContents = new Dictionary<string, string>(), TubeLabels = new Dictionary<string, string>(), Plates = _scenario.Plates.Select(p => new TransformationPlateState { Id = p.Id, Label = p.Label }).ToList() };
        return Snapshot();
    }
    public TransformationAttemptSnapshot StartLinkedRetry(string priorAttemptId, TransformationRunMode mode)
    {
        if (!Guid.TryParseExact(priorAttemptId, "N", out _)) throw new ArgumentException("Prior attempt ID is invalid.");
        Start(mode); _state.RetryOfAttemptId = priorAttemptId; return Snapshot();
    }
    public TransformationAttemptSnapshot Snapshot()
    {
        var copy = Clone(_state);
        if (copy.Phase is not (TransformationPhase.Complete or TransformationPhase.Debrief) && copy.DiagnosticCaseId is not null)
        {
            copy.DiagnosticCaseId = "case-in-progress";
            if (!copy.EvidenceCardsViewed.Contains("setup-record")) copy.DiagnosticSetup = null;
            foreach (var record in copy.Ledger.Where(x => x.Type == TransformationActionType.StartDiagnosticCase)) record.Target = "case-in-progress";
        }
        return copy;
    }
    public TransformationActionResult Submit(TransformationAction action)
    {
        if (!_state.Started) throw new InvalidOperationException("Start an attempt first.");
        if (action is null || !Enum.IsDefined(action.Type)) return Record(false, "A recognized action is required.", "");
        if (_state.Phase is TransformationPhase.Complete) return Record(false, "This completed attempt is locked. Start a new linked attempt for another run.", "");
        var (accepted, message, coaching) = action.Type switch
        {
            TransformationActionType.AcknowledgeIntro => AcknowledgeIntro(),
            TransformationActionType.LabelTube => LabelTube(action.Target, action.Value),
            TransformationActionType.UseFreshTip => UseFreshTip(),
            TransformationActionType.SelectTubeSource => SelectTubeSource(action.Target),
            TransformationActionType.SelectTubeDestination => SelectTubeDestination(action.Target),
            TransformationActionType.LoadTube => LoadTube(),
            TransformationActionType.CorrectTubeContent => CorrectTubeContent(action.Target, action.Value),
            TransformationActionType.RecoverTubePreparation => RecoverTubePreparation(),
            TransformationActionType.AssignPlate => AssignPlate(action.Target, action.Value),
            TransformationActionType.CorrectPlateAssignment => CorrectPlateAssignment(action.Target, action.Value),
            TransformationActionType.CommitPrediction => CommitPrediction(action.Target, action.Value),
            TransformationActionType.AdvanceConceptualTime => AdvanceTime(),
            TransformationActionType.ViewPlate => ViewPlate(action.Target, action.Value, false),
            TransformationActionType.RecordExplanation => Explain(action.Target, action.Value),
            TransformationActionType.StartDiagnosticCase => StartCase(action.Target),
            TransformationActionType.ViewCasePlate => ViewPlate(action.Target, action.Value, true),
            TransformationActionType.ViewEvidenceCard => ViewEvidenceCard(action.Target),
            TransformationActionType.RecordDiagnosticDecision => RecordDecision(action.Target, action.Value),
            TransformationActionType.RequestHint => Hint(action.Target),
            TransformationActionType.RevisePrediction => RevisePrediction(action.Target, action.Value),
            TransformationActionType.RecordCleanup => RecordCloseout("cleanup"),
            TransformationActionType.RecordWasteDecision => RecordCloseout("waste"),
            TransformationActionType.RecordDebrief => RecordCloseout("document"),
            TransformationActionType.CompleteDebrief => CompleteDebrief(),
            _ => (false, "This action is not available in this version.", "")
        };
        return Record(accepted, message, coaching, action);
    }
    public string Save() => JsonSerializer.Serialize(new PersistedTransformationAttempt { Scenario = _scenario, Lessons = _lessons, Cases = _cases, Rubric = _rubric, Sources = _sources, Snapshot = _state }, JsonOptions);
    public static TransformationSimulation Restore(string json)
    {
        var saved = Read<PersistedTransformationAttempt>(json);
        if (saved.FormatVersion != 1 || saved.ModelVersion != ModelVersion || saved.Snapshot is null) throw new ArgumentException("This Level 2 file uses an incompatible model or save format. It has been preserved.");
        var simulation = new TransformationSimulation(saved.Scenario, saved.Lessons, saved.Cases, saved.Rubric, saved.Sources);
        if (!Guid.TryParseExact(saved.Snapshot.AttemptId, "N", out _)) throw new ArgumentException("Saved Level 2 attempt ID is invalid. Its file has been preserved.");
        simulation.Start(saved.Snapshot.Mode); simulation._state.AttemptId = saved.Snapshot.AttemptId;
        if (saved.Snapshot.RetryOfAttemptId is not null && !Guid.TryParseExact(saved.Snapshot.RetryOfAttemptId, "N", out _)) throw new ArgumentException("Saved linked retry provenance is invalid. Its file has been preserved.");
        foreach (var record in saved.Snapshot.Ledger)
        {
            if (!Enum.IsDefined(record.Type)) throw new ArgumentException("Saved Level 2 attempt contains an unknown action.");
            simulation.Submit(new TransformationAction(record.Type, record.Target, record.Value));
        }
        simulation._state.RetryOfAttemptId = saved.Snapshot.RetryOfAttemptId;
        if (JsonSerializer.Serialize(simulation._state, JsonOptions) != JsonSerializer.Serialize(saved.Snapshot, JsonOptions)) throw new ArgumentException("Saved Level 2 state does not agree with its action history. Its file has been preserved.");
        return simulation;
    }
    public TransformationReport BuildReport()
    {
        var diagnostic = _state.DiagnosticCaseId is null ? null : _cases.Cases.Single(caseDefinition => caseDefinition.Id == _state.DiagnosticCaseId);
        var revealCause = _state.Phase is TransformationPhase.Debrief or TransformationPhase.Complete;
        var ledger = LearnerFacingLedger(revealCause);
        var report = new TransformationReport
        {
            AttemptId = _state.AttemptId,
            Mode = _state.Mode,
            ScenarioSeed = _state.ScenarioSeed,
            Complete = _state.Phase == TransformationPhase.Complete,
            Plates = Clone(_state.Plates),
            TubeContents = Clone(_state.TubeContents),
            TubeLabels = Clone(_state.TubeLabels),
            TubeSetupCorrections = [.. _state.TubeSetupCorrections],
            PlateAssignmentCorrections = [.. _state.PlateAssignmentCorrections],
            CaseObservations = Clone(_state.CasePlates),
            Predictions = Clone(_state.Predictions),
            PrelockPredictionCorrections = [.. _state.PrelockPredictionCorrections],
            HintsUsed = [.. _state.HintsUsed],
            ExplanationChoices = [.. _state.ExplanationChoices],
            ExplanationChoiceHistory = [.. _state.ExplanationChoiceHistory],
            CaseSetup = _state.EvidenceCardsViewed.Contains("setup-record") ? Clone(_state.DiagnosticSetup ?? new TransformationCaseSetup()) : null,
            DiagnosticDecisions = [.. _state.DiagnosticDecisions],
            DiagnosticDecisionHistory = [.. _state.DiagnosticDecisionHistory],
            EvidenceViewed = [.. _state.EvidenceViewed],
            EvidenceCardsViewed = [.. _state.EvidenceCardsViewed],
            RetryOfAttemptId = _state.RetryOfAttemptId,
            Ledger = ledger,
            Sources = Clone(_sources.Entries),
            MainSelectionClaimSupported = MainSelectionSupported(),
            MainExpressionClaimSupported = MainExpressionSupported(),
            RubricStatus = RubricStatus(),
            ClaimReasons = MainClaimReasons()
        };
        if (revealCause && diagnostic is not null) report.DiagnosticCase = DiagnosticReport(diagnostic);
        return report;
    }
    private List<TransformationActionRecord> LearnerFacingLedger(bool revealCause)
    {
        var ledger = Clone(_state.Ledger);
        if (!revealCause) foreach (var record in ledger.Where(record => record.Type == TransformationActionType.StartDiagnosticCase)) record.Target = "case-in-progress";
        return ledger;
    }
    private Dictionary<string, string> MainClaimReasons() => new()
    {
        ["main-selection"] = SelectionReasonForMain(),
        ["main-expression"] = ExpressionReasonForMain()
    };
    private TransformationDiagnosticReport DiagnosticReport(TransformationCaseDefinition diagnostic) => new()
    {
        Id = "case-debrief",
        Title = diagnostic.Title,
        FinalFictionalCause = diagnostic.FictionalCause,
        SelectionClaimSupported = DerivedCaseSelectionSupported(diagnostic),
        ExpressionClaimSupported = DerivedCaseExpressionSupported(diagnostic),
        SelectionClaimReason = DerivedCaseSelectionSupported(diagnostic) ? "The curated controls support a limited selection claim in this fictional case." : "The curated control pattern withholds the planned selection claim; observations remain reportable.",
        ExpressionClaimReason = DerivedCaseExpressionSupported(diagnostic) ? "The curated controls support a limited expression claim in this fictional case." : "The curated control pattern withholds the planned expression claim; phenotype alone does not prove one cause."
    };
    public static string BuildCsv(TransformationReport report)
    {
        var rows = new List<string> { "application_version,model_version,save_format,attempt_id,scenario_seed,plate,label,tube_label,medium,growth,fluorescence,original_growth_prediction,original_fluorescence_prediction,revisions,prelock_prediction_corrections,hints,explanation_choices,main_selection_supported,main_expression_supported,case_id,case_cause,case_selection_supported,case_expression_supported,evidence_viewed,evidence_cards_viewed,diagnostic_decisions,rubric_status,actions,sources,limits,tube_contents,tube_labels,tube_setup_corrections,plate_assignment_corrections,case_setup" };
        foreach (var plate in report.Plates) { var prediction = report.Predictions.SingleOrDefault(p => p.PlateId == plate.Id); rows.Add(string.Join(",", new[] { Csv(report.ApplicationVersion), Csv(report.ModelVersion), report.SaveFormatVersion.ToString(), Csv(report.AttemptId), report.ScenarioSeed.ToString(), Csv(plate.Id), Csv(plate.Label), Csv(plate.TubeLabel), Csv(plate.Medium), plate.Growth.ToString(), plate.Fluorescence.ToString(), Csv(prediction?.OriginalGrowthPrediction ?? ""), Csv(prediction?.OriginalFluorescencePrediction ?? ""), Csv(string.Join(" | ", prediction?.Revisions ?? [])), Csv(string.Join(" | ", report.PrelockPredictionCorrections)), Csv(string.Join(" | ", report.HintsUsed)), Csv(string.Join(" | ", report.ExplanationChoices)), report.MainSelectionClaimSupported.ToString(), report.MainExpressionClaimSupported.ToString(), Csv(report.DiagnosticCase?.Id ?? ""), Csv(report.DiagnosticCase?.FinalFictionalCause ?? ""), (report.DiagnosticCase?.SelectionClaimSupported ?? false).ToString(), (report.DiagnosticCase?.ExpressionClaimSupported ?? false).ToString(), Csv(string.Join(" | ", report.EvidenceViewed)), Csv(string.Join(" | ", report.EvidenceCardsViewed)), Csv(string.Join(" | ", report.DiagnosticDecisions)), Csv(string.Join(" | ", report.RubricStatus.Select(item => item.Key + ":" + item.Value))), Csv(string.Join(" | ", report.Ledger.Select(item => item.Type + ":" + item.Target + ":" + item.Value + ":" + item.Accepted))), Csv(string.Join(" | ", report.Sources.Select(s => s.Id))), Csv(report.Limits), Csv(FormatDictionary(report.TubeContents)), Csv(FormatDictionary(report.TubeLabels)), Csv(string.Join(" | ", report.TubeSetupCorrections)), Csv(string.Join(" | ", report.PlateAssignmentCorrections)), Csv(FormatCaseSetup(report.CaseSetup)) })); }
        foreach (var plate in report.CaseObservations) rows.Add(string.Join(",", new[] { Csv(report.ApplicationVersion), Csv(report.ModelVersion), report.SaveFormatVersion.ToString(), Csv(report.AttemptId), report.ScenarioSeed.ToString(), Csv("case-observation:" + plate.Id), Csv(plate.Label), Csv(plate.TubeLabel), Csv(plate.Medium), plate.Growth.ToString(), plate.Fluorescence.ToString(), "", "", "", Csv(string.Join(" | ", report.PrelockPredictionCorrections)), Csv(string.Join(" | ", report.HintsUsed)), Csv(string.Join(" | ", report.ExplanationChoices)), report.MainSelectionClaimSupported.ToString(), report.MainExpressionClaimSupported.ToString(), Csv(report.DiagnosticCase?.Id ?? ""), Csv(report.DiagnosticCase?.FinalFictionalCause ?? ""), (report.DiagnosticCase?.SelectionClaimSupported ?? false).ToString(), (report.DiagnosticCase?.ExpressionClaimSupported ?? false).ToString(), Csv(string.Join(" | ", report.EvidenceViewed)), Csv(string.Join(" | ", report.EvidenceCardsViewed)), Csv(string.Join(" | ", report.DiagnosticDecisions)), Csv(string.Join(" | ", report.RubricStatus.Select(item => item.Key + ":" + item.Value))), Csv(string.Join(" | ", report.Ledger.Select(item => item.Type + ":" + item.Target + ":" + item.Value + ":" + item.Accepted))), Csv(string.Join(" | ", report.Sources.Select(s => s.Id))), Csv(report.Limits), Csv(FormatDictionary(report.TubeContents)), Csv(FormatDictionary(report.TubeLabels)), Csv(string.Join(" | ", report.TubeSetupCorrections)), Csv(string.Join(" | ", report.PlateAssignmentCorrections)), Csv(FormatCaseSetup(report.CaseSetup)) }));
        return string.Join(Environment.NewLine, rows);
    }
    public static string BuildHtml(TransformationReport report)
    {
        var plateRows = string.Join("", report.Plates.Select(plate =>
        {
            var prediction = report.Predictions.SingleOrDefault(item => item.PlateId == plate.Id);
            return $"<tr><td>{Html(plate.Id)}</td><td>{Html(plate.TubeLabel)}</td><td>{Html(plate.Medium)}</td><td>{Html(plate.Growth.ToString())}</td><td>{Html(plate.Fluorescence.ToString())}</td><td>{Html(prediction?.OriginalGrowthPrediction ?? "")}</td><td>{Html(prediction?.OriginalFluorescencePrediction ?? "")}</td><td>{Html(string.Join(" | ", prediction?.Revisions ?? []))}</td></tr>";
        }));
        var caseRows = string.Join("", report.CaseObservations.Select(plate => $"<tr><td>{Html(plate.Id)}</td><td>{Html(plate.Growth.ToString())}</td><td>{Html(plate.Fluorescence.ToString())}</td></tr>"));
        var rubric = string.Join("", report.RubricStatus.Select(item => $"<li>{Html(item.Key)}: {Html(item.Value)}</li>"));
        var actions = string.Join("<br>", report.Ledger.Select(item => Html($"{item.Type}: {item.Target}: {item.Value}: {item.Accepted}")));
        var mainSelectionReason = Html(report.ClaimReasons.GetValueOrDefault("main-selection", "No main selection reason is available."));
        var mainExpressionReason = Html(report.ClaimReasons.GetValueOrDefault("main-expression", "No main expression reason is available."));
        var mainClaims = $"<h2>Main experiment claim status</h2><p>Main experiment selection support: {report.MainSelectionClaimSupported}<br>{mainSelectionReason}</p><p>Main experiment expression support: {report.MainExpressionClaimSupported}<br>{mainExpressionReason}</p>";
        var caseClaims = report.DiagnosticCase is null ? "" : $"<h2>Case debrief claim status</h2><p>Case selection support: {report.DiagnosticCase.SelectionClaimSupported}<br>{Html(report.DiagnosticCase.SelectionClaimReason)}</p><p>Case expression support: {report.DiagnosticCase.ExpressionClaimSupported}<br>{Html(report.DiagnosticCase.ExpressionClaimReason)}</p>";
        return $"<!doctype html><html><head><meta charset=\"utf-8\"><title>Transformation Controls report</title></head><body><h1>Transformation Controls report</h1><p>Application {Html(report.ApplicationVersion)} · model {Html(report.ModelVersion)} · save format {report.SaveFormatVersion} · attempt {Html(report.AttemptId)} · seed {report.ScenarioSeed}</p><p>{Html(report.Limits)}</p><h2>Tube setup and corrections</h2><p>Contents: {Html(FormatDictionary(report.TubeContents))}<br>Labels: {Html(FormatDictionary(report.TubeLabels))}<br>Tube corrections: {Html(string.Join(" | ", report.TubeSetupCorrections))}<br>Plate corrections: {Html(string.Join(" | ", report.PlateAssignmentCorrections))}<br>Case setup record: {Html(FormatCaseSetup(report.CaseSetup))}</p><h2>Main experiment setup, predictions, and observations</h2><table><thead><tr><th>Plate</th><th>Tube label</th><th>Medium</th><th>Growth</th><th>Fluorescence</th><th>Original growth prediction</th><th>Original fluorescence prediction</th><th>Revisions</th></tr></thead><tbody>{plateRows}</tbody></table><h2>Pre-lock setup/prediction history</h2><p>{Html(string.Join(" | ", report.PrelockPredictionCorrections))}</p><h2>Case observations</h2><table><thead><tr><th>Plate</th><th>Growth</th><th>Fluorescence</th></tr></thead><tbody>{caseRows}</tbody></table>{mainClaims}{caseClaims}<h2>Diagnostic record</h2><p>{Html(report.DiagnosticCase?.FinalFictionalCause ?? "Not disclosed before debrief.")}</p><p>Evidence cards: {Html(string.Join(" | ", report.EvidenceCardsViewed))}</p><p>Decisions: {Html(string.Join(" | ", report.DiagnosticDecisions))}</p><h2>Rubric</h2><ul>{rubric}</ul><h2>Action ledger</h2><p>{actions}</p><h2>Sources</h2><ul>{string.Join("", report.Sources.Select(source => $"<li>{Html(source.Id)}: {Html(source.Label)}</li>"))}</ul></body></html>";
    }

    private (bool, string, string) AcknowledgeIntro() => _state.Phase != TransformationPhase.Intro ? (false, "Intro has already been acknowledged.", "") : Move(TransformationPhase.TubeSetup, "Intro concepts recorded. Label the transformation reaction (T) and negative control (NC).", "DNA can be expressed through RNA to protein; GFP fluorescence requires excitation in this fictional model.");
    private (bool, string, string) LabelTube(string? tube, string? label)
    {
        if (_state.Phase != TransformationPhase.TubeSetup || tube is not ("T" or "NC") || string.IsNullOrWhiteSpace(label)) return (false, "Record each tube label before its conceptual transfer.", "");
        if (_state.TubeLabels.ContainsKey(tube)) return (false, "That tube label is already recorded.", "");
        _state.TubeLabels[tube] = label; return (true, $"Label recorded for tube {tube}.", "Labels identify a planned role; they do not replace actual contents.");
    }
    private (bool, string, string) UseFreshTip()
    {
        if (_state.Phase != TransformationPhase.TubeSetup || _state.TubeLabels.Count != 2) return (false, "Record T and NC labels before a conceptual fresh-tip transfer.", "");
        _state.FreshTipReady = true; _state.SelectedTubeSource = null; _state.SelectedTubeDestination = null; return (true, "Fresh conceptual tip recorded for one source-to-destination transfer.", "The model tracks source and destination as traceability decisions, not physical technique.");
    }
    private (bool, string, string) SelectTubeSource(string? source)
    {
        if (!_state.FreshTipReady || source is not ("DNA" or "Buffer")) return (false, "Use a fresh conceptual tip, then choose DNA or Buffer as source.", "");
        _state.SelectedTubeSource = source; return (true, $"Source {source} selected.", "");
    }
    private (bool, string, string) SelectTubeDestination(string? tube)
    {
        if (!_state.FreshTipReady || _state.SelectedTubeSource is null || tube is not ("T" or "NC") || _state.TubeContents.ContainsKey(tube)) return (false, "Choose an unused T or NC destination after selecting a source.", "");
        _state.SelectedTubeDestination = tube; return (true, $"Destination {tube} selected.", "");
    }
    private (bool, string, string) LoadTube()
    {
        var tube = _state.SelectedTubeDestination; var content = _state.SelectedTubeSource;
        if (_state.Phase != TransformationPhase.TubeSetup || !_state.FreshTipReady || tube is null || content is null) return (false, "Use fresh tip, source, and destination controls before recording an actual tube content.", "");
        _state.TubeContents[tube] = content; _state.FreshTipReady = false; _state.SelectedTubeSource = null; _state.SelectedTubeDestination = null;
        if (_state.TubeContents.Count == 2) _state.Phase = TransformationPhase.PlateSetup;
        return (true, $"Actual content recorded for tube {tube}. Labels stay separate from contents.", "Adding DNA does not mean every cell will receive it; the label alone cannot change the recorded content.");
    }
    private (bool, string, string) CorrectTubeContent(string? tube, string? content)
    {
        if (_state.Phase is not (TransformationPhase.TubeSetup or TransformationPhase.PlateSetup or TransformationPhase.Predictions) || _state.PredictionsLocked || tube is not ("T" or "NC") || content is not ("DNA" or "Buffer") || !_state.TubeContents.ContainsKey(tube)) return (false, "Committed tube contents can be corrected before prediction lock; provisional predictions affected by the correction are retained in history and must be re-recorded.", "");
        _state.TubeSetupCorrections.Add($"{tube}:{_state.TubeContents[tube]}->{content}"); _state.TubeContents[tube] = content;
        ClearProvisionalPredictions(_state.Plates.Where(plate => plate.TubeLabel == tube), "tube-content-correction");
        return (true, "Committed actual tube content corrected before prediction lock; affected provisional predictions were retained in history and must be recorded again.", "");
    }
    private (bool, string, string) RecoverTubePreparation()
    {
        if (_state.Phase != TransformationPhase.TubeSetup) return (false, "Tube preparation recovery is only available before plate setup.", "");
        _state.TubeSetupCorrections.Add("reset-preparation"); _state.FreshTipReady = false; _state.SelectedTubeSource = null; _state.SelectedTubeDestination = null;
        return (true, "Conceptual tube preparation reset; prior selections remain in correction history.", "");
    }
    private (bool, string, string) AssignPlate(string? plateId, string? assignment)
    {
        if (_state.Phase != TransformationPhase.PlateSetup || plateId is null || assignment is null) return (false, "Set each planned plate label before making predictions.", "");
        var plate = _state.Plates.SingleOrDefault(p => p.Id == plateId); if (plate is null || assignment.Split('|') is not [var tube, var medium] || tube is not ("T" or "NC") || medium is not ("LB" or "LB+amp" or "LB+amp+Ara")) return (false, "Use a named plate, T or NC, and one listed conceptual medium.", "");
        if (plate.TubeLabel.Length > 0) return (false, "That plate assignment is already recorded.", "");
        plate.TubeLabel = tube; plate.Medium = medium;
        if (_state.Plates.All(p => p.TubeLabel.Length > 0)) _state.Phase = TransformationPhase.Predictions;
        return (true, $"Plate {plateId} assignment recorded.", "The media labels represent selection and induction concepts, not a protocol.");
    }
    private (bool, string, string) CorrectPlateAssignment(string? plateId, string? assignment)
    {
        if (_state.Phase is not (TransformationPhase.PlateSetup or TransformationPhase.Predictions) || _state.PredictionsLocked || plateId is null || assignment is null || assignment.Split('|') is not [var tube, var medium] || tube is not ("T" or "NC") || medium is not ("LB" or "LB+amp" or "LB+amp+Ara")) return (false, "Plate assignments can be corrected only before all original predictions lock.", "");
        var plate = _state.Plates.SingleOrDefault(p => p.Id == plateId); if (plate is null || plate.TubeLabel.Length == 0) return (false, "Choose an already assigned plate.", "");
        _state.PlateAssignmentCorrections.Add($"{plateId}:{plate.TubeLabel}|{plate.Medium}->{assignment}"); plate.TubeLabel = tube; plate.Medium = medium; ClearProvisionalPredictions([plate], "plate-assignment-correction"); _state.Phase = TransformationPhase.Predictions;
        return (true, "Plate assignment correction appended before outcomes; affected provisional predictions were retained in history and must be recorded again.", "A correction updates the conceptual setup but does not erase traceability history.");
    }
    private void ClearProvisionalPredictions(IEnumerable<TransformationPlateState> plates, string reason)
    {
        foreach (var plate in plates)
        {
            var prediction = _state.Predictions.SingleOrDefault(item => item.PlateId == plate.Id);
            if (prediction is null) continue;
            _state.PrelockPredictionCorrections.Add($"{reason}:{plate.Id}:growth={prediction.OriginalGrowthPrediction};fluorescence={prediction.OriginalFluorescencePrediction}");
            _state.Predictions.Remove(prediction);
        }
    }
    private (bool, string, string) CommitPrediction(string? plateId, string? prediction)
    {
        if (_state.Phase != TransformationPhase.Predictions || plateId is null || !IsPredictionValue(prediction, out var dimension, out var claim)) return (false, "Record independent growth and fluorescence predictions, or NotSure, for every plate before outcomes are available.", "");
        if (!_state.Plates.Any(p => p.Id == plateId)) return (false, "Choose a listed plate.", "");
        var item = _state.Predictions.SingleOrDefault(p => p.PlateId == plateId); if (item is null) { item = new TransformationPrediction { PlateId = plateId }; _state.Predictions.Add(item); }
        if (dimension == "growth") { if (item.OriginalGrowthPrediction.Length > 0) return (false, "That original growth prediction is already locked.", ""); item.OriginalGrowthPrediction = claim; }
        else { if (item.OriginalFluorescencePrediction.Length > 0) return (false, "That original fluorescence prediction is already locked.", ""); item.OriginalFluorescencePrediction = claim; }
        if (_state.Predictions.Count == 4 && _state.Predictions.All(p => p.OriginalGrowthPrediction.Length > 0 && p.OriginalFluorescencePrediction.Length > 0)) { _state.PredictionsLocked = true; _state.Phase = TransformationPhase.TimeJump; return (true, "All original predictions are locked. Use the conceptual time jump when ready; no outcomes are shown yet.", "Predictions remain intact; later corrections append without replacing the original record."); }
        return (true, "Prediction dimension recorded. Complete both dimensions for every plate before any observation.", "");
    }
    private (bool, string, string) AdvanceTime() => _state.Phase != TransformationPhase.TimeJump ? (false, "Lock all predictions before the conceptual time jump.", "") : Move(TransformationPhase.Observe, "Conceptual time jump complete. Choose normal or excitation view for each plate.", "This animation represents time passing conceptually and does not give a laboratory duration.");
    private (bool, string, string) ViewPlate(string? plateId, string? view, bool isCase)
    {
        if ((!isCase && _state.Phase is not (TransformationPhase.Observe or TransformationPhase.Explain)) || (isCase && _state.Phase != TransformationPhase.DiagnosticCase) || plateId is null || view is not ("normal" or "excitation")) return (false, "This view is not available in the current stage.", "");
        var displayPlates = isCase ? _state.CasePlates : _state.Plates;
        var plate = displayPlates.SingleOrDefault(p => p.Id == plateId); if (plate is null) return (false, "Choose a listed plate.", "");
        if (view == "excitation" && !plate.NormalViewSeen) return (false, "Record the normal view before judging whether fluorescence is assessable.", "");
        var outcome = isCase ? _cases.Cases.Single(c => c.Id == _state.DiagnosticCaseId).Outcomes[plateId] : OutcomeFor(plate);
        plate.ActiveView = view;
        if (view == "normal") { plate.NormalViewSeen = true; plate.Growth = outcome.Growth; } else { plate.ExcitationViewSeen = true; plate.Fluorescence = plate.Growth == TransformationGrowth.Absent ? TransformationFluorescence.NotAssessable : outcome.Fluorescence; }
        var evidence = $"{(isCase ? "case:" : "main:")}{plateId}:{view}"; if (!_state.EvidenceViewed.Contains(evidence)) _state.EvidenceViewed.Add(evidence);
        if (!isCase && _state.Plates.All(p => p.NormalViewSeen && p.ExcitationViewSeen)) _state.Phase = TransformationPhase.Explain;
        return (true, $"{view} view recorded for {plateId}. The observed phenotype is available for explanation.", "A no-growth plate makes fluorescence not assessable; it does not mean GFP is absent.");
    }
    private (bool, string, string) Explain(string? plateId, string? choice)
    {
        if (_state.Phase != TransformationPhase.Explain || plateId is null || choice is not ("no-colonies-not-assessable" or "nongreen-not-no-dna" or "green-consistent-gfp" or "growth-proves-all" or "nonfluorescence-proves-no-dna") || !_state.Plates.Any(p => p.Id == plateId)) return (false, "Choose a plate-specific observation explanation after observations.", "");
        var entry = $"{plateId}:{choice}"; var prior = _state.ExplanationChoices.SingleOrDefault(item => item.StartsWith(plateId + ":", StringComparison.Ordinal)); if (prior is not null) { _state.ExplanationChoiceHistory.Add(prior); _state.ExplanationChoices.Remove(prior); } _state.ExplanationChoices.Add(entry);
        return (true, "Explanation choice recorded. The debrief distinguishes observations from what the controls can support.", "Phenotypes can fit several causes, so the model scores evidence limits rather than guessing a hidden cause.");
    }
    private (bool, string, string) StartCase(string? id)
    {
        if (_state.Phase != TransformationPhase.Explain || !_state.Plates.All(p => p.NormalViewSeen && p.ExcitationViewSeen) || !_state.Plates.All(HasMainExplanation)) return (false, "Record one plate-specific explanation for every main observation before choosing one replayable Lab Detective case.", "");
        var chosen = id is null ? _cases.Cases.Single(c => c.Id == "missing-arabinose") : _cases.Cases.SingleOrDefault(c => c.Id == id); if (chosen is null) return (false, "Choose one listed fictional diagnostic case.", "");
        _state.DiagnosticCaseId = chosen.Id; _state.DiagnosticSetup = Clone(chosen.Setup); _state.Phase = TransformationPhase.DiagnosticCase;
        _state.CasePlates = _scenario.Plates.Select(p => new TransformationPlateState { Id = p.Id, Label = p.Label }).ToList();
        return (true, "Lab Detective case selected. Observe evidence before the fictional cause is disclosed in debrief.", "The case is curated separately from the normal run; a pattern does not prove a unique real-world cause.");
    }
    private (bool, string, string) RecordDecision(string? decision, string? rationale)
    {
        if (_state.Phase != TransformationPhase.DiagnosticCase || !_state.CasePlates.All(p => p.NormalViewSeen && p.ExcitationViewSeen) || !_state.EvidenceCardsViewed.Contains("setup-record") || !_state.EvidenceCardsViewed.Contains("control-comparison") || decision is not ("selection-supported" or "selection-withheld" or "expression-supported" or "expression-withheld" or "uncertainty-cannot-prove" or "uncertainty-proves-cause" or "uncertainty-not-sure") || rationale is not ("phenotype-not-cause" or "control-pattern" or "uncertain")) return (false, "View case evidence cards and choose a structured claim plus uncertainty statement.", "");
        var dimension = decision.StartsWith("selection", StringComparison.Ordinal) ? "selection" : decision.StartsWith("expression", StringComparison.Ordinal) ? "expression" : "uncertainty";
        var prior = _state.DiagnosticDecisions.SingleOrDefault(item => item.StartsWith(dimension + "-", StringComparison.Ordinal));
        if (prior is not null) { _state.DiagnosticDecisionHistory.Add(prior); _state.DiagnosticDecisions.Remove(prior); }
        _state.DiagnosticDecisions.Add(decision + ":" + rationale); return (true, "Diagnostic decision recorded. It will be assessed for observation, plausibility, and evidence limits.", "");
    }
    private (bool, string, string) ViewEvidenceCard(string? card)
    {
        if (_state.Phase != TransformationPhase.DiagnosticCase || card is not ("setup-record" or "control-comparison" or "observation-limit")) return (false, "This evidence card is available during the Lab Detective case.", "");
        if (!_state.EvidenceCardsViewed.Contains(card)) _state.EvidenceCardsViewed.Add(card); return (true, "Evidence card viewed: " + card + ".", "This is an inspectable record, not a hidden-cause answer.");
    }
    private (bool, string, string) Hint(string? id)
    {
        var hint = id ?? "controls"; if (!_state.HintsUsed.Contains(hint)) _state.HintsUsed.Add(hint); return (true, "Hint recorded in this attempt history.", "Use controls to distinguish selection from expression. A planned claim is withheld when its control is not interpretable.");
    }
    private (bool, string, string) RevisePrediction(string? plateId, string? revised)
    {
        if (!_state.PredictionsLocked || _state.Phase is not (TransformationPhase.Explain or TransformationPhase.DiagnosticCase or TransformationPhase.Complete) || plateId is null || !IsPredictionValue(revised, out _, out _)) return (false, "A later growth or fluorescence revision must use a valid dimension and value after original predictions lock.", "");
        var prediction = _state.Predictions.SingleOrDefault(p => p.PlateId == plateId); if (prediction is null || prediction.OriginalGrowthPrediction.Length == 0 || prediction.OriginalFluorescencePrediction.Length == 0) return (false, "Choose a fully predicted plate.", "");
        prediction.Revisions.Add(revised!); return (true, "Revision appended; the original prediction remains unchanged.", "");
    }
    private (bool, string, string) RecordCloseout(string item)
    {
        if (_state.Phase != TransformationPhase.DiagnosticCase || !_state.CasePlates.All(p => p.NormalViewSeen && p.ExcitationViewSeen)) return (false, "View all case evidence before simulated cleanup and documentation.", "");
        if (item == "cleanup") _state.CleanupRecorded = true; else if (item == "waste") _state.WasteDecisionRecorded = true; else _state.DebriefDocumented = true;
        return (true, "Simulated " + item + " recorded.", "This is a documentation and closeout concept, not a local disposal procedure.");
    }
    private (bool, string, string) CompleteDebrief()
    {
        if (_state.Phase != TransformationPhase.DiagnosticCase || _state.DiagnosticCaseId is null || !_state.CasePlates.All(p => p.NormalViewSeen && p.ExcitationViewSeen) || _state.EvidenceCardsViewed.Count < 3 || !_state.Plates.All(HasMainExplanation) || _state.DiagnosticDecisions.Count != 3 || _state.DiagnosticDecisions.Count(item => item.StartsWith("selection-", StringComparison.Ordinal)) != 1 || _state.DiagnosticDecisions.Count(item => item.StartsWith("expression-", StringComparison.Ordinal)) != 1 || _state.DiagnosticDecisions.Count(item => item.StartsWith("uncertainty-", StringComparison.Ordinal)) != 1 || !_state.CleanupRecorded || !_state.WasteDecisionRecorded || !_state.DebriefDocumented) return (false, "View all case evidence cards, record two structured decisions, and record simulated cleanup, waste decision, and documentation before debrief.", "");
        _state.Phase = TransformationPhase.Complete; return (true, "Debrief complete. The fictional case cause is now displayed with evidence limits.", "");
    }
    private (bool, string, string) Move(TransformationPhase next, string message, string coaching) { _state.Phase = next; return (true, message, coaching); }
    private TransformationActionResult Record(bool accepted, string message, string coaching, TransformationAction? action = null)
    {
        if (action is not null) _state.Ledger.Add(new TransformationActionRecord { Type = action.Type, Target = action.Target, Value = action.Value, Accepted = accepted, Outcome = message });
        if (_state.Mode == TransformationRunMode.Assessment) { coaching = "Procedural coaching is deferred to debrief."; message = accepted ? "Action recorded. Coaching is held for debrief." : "Action was not recorded. Review the available controls."; }
        return new TransformationActionResult { Accepted = accepted, Message = message, Coaching = coaching, Snapshot = Snapshot() };
    }
    private static bool IsPredictionValue(string? value, out string dimension, out string claim)
    {
        dimension = ""; claim = "";
        if (value?.Split(':') is not [var parsedDimension, var parsedClaim]) return false;
        var valid = (parsedDimension == "growth" && parsedClaim is "Present" or "Absent" or "NotSure") || (parsedDimension == "fluorescence" && parsedClaim is "Detected" or "NoneDetected" or "NoColoniesToAssess" or "NotSure");
        if (!valid) return false;
        dimension = parsedDimension; claim = parsedClaim; return true;
    }
    private TransformationCasePlateOutcome OutcomeFor(TransformationPlateState plate)
    {
        var hasDna = _state.TubeContents.GetValueOrDefault(plate.TubeLabel) == "DNA"; var amp = plate.Medium.Contains("amp", StringComparison.Ordinal); var ara = plate.Medium.Contains("Ara", StringComparison.Ordinal);
        var growth = amp && !hasDna ? TransformationGrowth.Absent : TransformationGrowth.Present;
        return new TransformationCasePlateOutcome { Growth = growth, Fluorescence = growth == TransformationGrowth.Absent ? TransformationFluorescence.NotAssessable : hasDna && ara ? TransformationFluorescence.Detected : TransformationFluorescence.NoneDetected };
    }
    private bool HasAssignment(string id) { var plate = _state.Plates.Single(p => p.Id == id); return _scenario.Plates.Single(s => s.Id == id).ExpectedAssignment == plate.TubeLabel + "|" + plate.Medium; }
    private bool MainSelectionSupported() => _state.Plates.Count == 4 && HasAssignment("P1") && HasAssignment("P2") && HasAssignment("P4") && _state.Plates.Where(p => p.Id is "P1" or "P2" or "P4").All(p => p.NormalViewSeen) && _state.TubeContents.GetValueOrDefault("T") == "DNA" && _state.TubeContents.GetValueOrDefault("NC") == "Buffer" && _state.Plates.Single(p => p.Id == "P1").Growth == TransformationGrowth.Present && _state.Plates.Single(p => p.Id == "P2").Growth == TransformationGrowth.Absent && _state.Plates.Single(p => p.Id == "P4").Growth == TransformationGrowth.Present;
    private bool MainExpressionSupported() => MainSelectionSupported() && HasAssignment("P3") && _state.Plates.Single(p => p.Id == "P3").NormalViewSeen && _state.Plates.All(p => p.ExcitationViewSeen) && _state.Plates.Single(p => p.Id == "P1").Fluorescence == TransformationFluorescence.NoneDetected && _state.Plates.Single(p => p.Id == "P2").Fluorescence == TransformationFluorescence.NotAssessable && _state.Plates.Single(p => p.Id == "P3").Growth == TransformationGrowth.Present && _state.Plates.Single(p => p.Id == "P3").Fluorescence == TransformationFluorescence.Detected && _state.Plates.Single(p => p.Id == "P4").Fluorescence == TransformationFluorescence.NoneDetected;
    private string SelectionReasonForMain() => _state.Plates.Any(p => !p.NormalViewSeen) ? "Selection claim is withheld until all planned normal-view observations are recorded." : MainSelectionSupported() ? "The planned fictional controls support only a limited selection interpretation; observed growth does not mean every cell transformed." : "The planned selection claim is withheld because the actual contents, control pattern, assignments, or observations are not interpretable as the intended four-plate comparison.";
    private string ExpressionReasonForMain() => _state.Plates.Any(p => !p.ExcitationViewSeen) ? "Expression claim is withheld until all planned excitation observations are recorded." : MainExpressionSupported() ? "The planned fictional controls support only a limited induced-expression interpretation; nonfluorescence does not establish absence of GFP DNA." : "The planned expression claim is withheld because the selection claim or excitation-control pattern is not interpretable; observations remain reportable.";
    private Dictionary<string, string> RubricStatus()
    {
        var diagnostic = _state.DiagnosticCaseId is null ? null : _cases.Cases.Single(item => item.Id == _state.DiagnosticCaseId);
        var selection = DecisionStatus("selection", diagnostic is not null && DerivedCaseSelectionSupported(diagnostic));
        var expression = DecisionStatus("expression", diagnostic is not null && DerivedCaseExpressionSupported(diagnostic));
        var uncertainty = DecisionStatus("uncertainty", true);
        var mainExplanations = _state.Plates.All(HasMainExplanation);
        var alignedMainExplanations = _state.Plates.All(plate => _state.ExplanationChoices.Contains(plate.Id + ":" + ExpectedMainExplanation(plate.Id)));
        return new()
        {
            ["observations"] = _state.EvidenceViewed.Count >= 16 ? "main and case views recorded" : "incomplete",
            ["plausibility"] = _state.EvidenceCardsViewed.Contains("control-comparison") && _state.DiagnosticDecisions.Count == 3 ? "selection " + selection + "; expression " + expression : "not yet recorded",
            ["evidence-limits"] = !mainExplanations ? "main explanation pending" : !_state.EvidenceCardsViewed.Contains("observation-limit") || !_state.DiagnosticDecisions.Any(item => item.StartsWith("uncertainty-", StringComparison.Ordinal)) ? (alignedMainExplanations ? "main explanations aligned; case uncertainty pending" : "main explanation mismatch recorded; case uncertainty pending") : (alignedMainExplanations ? "main explanations aligned; case uncertainty " + uncertainty : "main explanation mismatch recorded; case uncertainty " + uncertainty),
            ["support-status"] = _state.Phase == TransformationPhase.Complete ? "debriefed from control comparisons: selection " + selection + "; expression " + expression + "; uncertainty " + uncertainty : "awaiting debrief"
        };
    }
    private bool HasMainExplanation(TransformationPlateState plate) => _state.ExplanationChoices.Any(choice => choice.StartsWith(plate.Id + ":", StringComparison.Ordinal));
    private static string ExpectedMainExplanation(string plateId) => plateId switch { "P2" => "no-colonies-not-assessable", "P3" => "green-consistent-gfp", _ => "nongreen-not-no-dna" };
    private string DecisionStatus(string dimension, bool expectedSupport)
    {
        var current = _state.DiagnosticDecisions.SingleOrDefault(item => item.StartsWith(dimension + "-", StringComparison.Ordinal));
        if (current is null) return "not recorded";
        var expected = dimension == "uncertainty" ? "uncertainty-cannot-prove" : dimension + (expectedSupport ? "-supported" : "-withheld");
        return current.StartsWith(expected, StringComparison.Ordinal) ? "aligned" : "mismatch";
    }
    private static TransformationCasePlateOutcome CaseSetupOutcome(TransformationCaseSetup setup, string plateId)
    {
        var assignment = setup.PlateAssignments[plateId].Split('|'); var hasDna = setup.TubeContents[assignment[0]] == "DNA"; var amp = assignment[1].Contains("amp", StringComparison.Ordinal); var ara = assignment[1].Contains("Ara", StringComparison.Ordinal); var growth = amp && !hasDna ? TransformationGrowth.Absent : TransformationGrowth.Present;
        return new TransformationCasePlateOutcome { Growth = growth, Fluorescence = growth == TransformationGrowth.Absent ? TransformationFluorescence.NotAssessable : hasDna && ara ? TransformationFluorescence.Detected : TransformationFluorescence.NoneDetected };
    }
    private static string CaseOutcomeSignature(TransformationCaseDefinition definition) => string.Join("|", new[] { "P1", "P2", "P3", "P4" }.Select(id => $"{id}:{definition.Outcomes[id].Growth}:{definition.Outcomes[id].Fluorescence}"));
    private static string CaseSetupSignature(TransformationCaseSetup setup) => "tubes:" + string.Join("|", setup.TubeContents.OrderBy(item => item.Key).Select(item => item.Key + "=" + item.Value)) + ";actual:" + string.Join("|", setup.PlateAssignments.OrderBy(item => item.Key).Select(item => item.Key + "=" + item.Value)) + ";intended:" + string.Join("|", setup.IntendedPlateLabels.OrderBy(item => item.Key).Select(item => item.Key + "=" + item.Value));
    private static bool DerivedCaseSelectionSupported(TransformationCaseDefinition definition) => definition.Id == "missing-arabinose" && CanonicalCaseOutcomeSignatures[definition.Id] == CaseOutcomeSignature(definition);
    private static bool DerivedCaseExpressionSupported(TransformationCaseDefinition definition) => DerivedCaseSelectionSupported(definition) && definition.Outcomes["P3"].Fluorescence == TransformationFluorescence.Detected;
    private static T Read<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions) ?? throw new ArgumentException("A Level 2 definition is invalid.");
    private static T Clone<T>(T value) => Read<T>(JsonSerializer.Serialize(value, JsonOptions));
    private static string FormatDictionary(Dictionary<string, string> values) => string.Join(" | ", values.OrderBy(item => item.Key).Select(item => item.Key + "=" + item.Value));
    private static string FormatCaseSetup(TransformationCaseSetup? setup) => setup is null ? "" : "tube contents: " + FormatDictionary(setup.TubeContents) + "; actual plate assignments: " + FormatDictionary(setup.PlateAssignments) + "; intended plate labels: " + FormatDictionary(setup.IntendedPlateLabels);
    private static string Csv(string value) => '"' + value.Replace("\"", "\"\"") + '"';
    private static string Html(string value) => System.Net.WebUtility.HtmlEncode(value);
    private static void ValidateDefinitions(TransformationScenarioDefinition scenario, TransformationLessonsDefinition lessons, TransformationCasesDefinition cases, TransformationRubricDefinition rubric, TransformationSourcesDefinition sources)
    {
        var requiredAssignments = new Dictionary<string, string> { ["P1"] = "T|LB+amp", ["P2"] = "NC|LB+amp", ["P3"] = "T|LB+amp+Ara", ["P4"] = "NC|LB" };
        var canonicalCases = new[] { "missing-arabinose", "swapped-tube-labels", "missing-dna", "failed-antibiotic-negative-control" };
        if (scenario.SchemaVersion != 1 || lessons.SchemaVersion != 1 || cases.SchemaVersion != 1 || rubric.SchemaVersion != 1 || sources.SchemaVersion != 1 || scenario.Id.Length == 0 || scenario.Plates.Count != 4 || scenario.Plates.Select(p => p.Id).Distinct().Count() != 4 || scenario.Plates.Any(p => !requiredAssignments.TryGetValue(p.Id, out var assignment) || p.ExpectedAssignment != assignment) || cases.Cases.Select(c => c.Id).OrderBy(x => x).SequenceEqual(canonicalCases.OrderBy(x => x)) == false || rubric.Dimensions.OrderBy(x => x).SequenceEqual(new[] { "observations", "plausibility", "evidence-limits", "support-status" }.OrderBy(x => x)) == false || rubric.Criteria.Keys.OrderBy(x => x).SequenceEqual(new[] { "observations", "plausibility", "evidence-limits", "support-status" }.OrderBy(x => x)) == false || rubric.Criteria.Any(item => string.IsNullOrWhiteSpace(item.Value)) || lessons.Lessons.Count < 4 || sources.Entries.Count != 3 || sources.Entries.Any(s => s.Id.Length == 0 || (s.Url.Length > 0 && (!Uri.TryCreate(s.Url, UriKind.Absolute, out var u) || u.Scheme != "https")))) throw new ArgumentException("Level 2 definitions are invalid or unsupported.");
        foreach (var c in cases.Cases)
        {
            if (c.Title.StartsWith("Missing", StringComparison.OrdinalIgnoreCase) || c.Outcomes.Count != 4 || c.Setup.TubeContents.Keys.OrderBy(x => x).SequenceEqual(new[] { "NC", "T" }) == false || c.Setup.PlateAssignments.Keys.OrderBy(x => x).SequenceEqual(new[] { "P1", "P2", "P3", "P4" }) == false || c.Setup.IntendedPlateLabels.Keys.OrderBy(x => x).SequenceEqual(new[] { "P1", "P2", "P3", "P4" }) == false || c.Setup.TubeContents.Values.Any(value => value is not ("DNA" or "Buffer")) || c.Setup.PlateAssignments.Values.Any(value => value.Split('|') is not [var tube, var medium] || tube is not ("T" or "NC") || medium is not ("LB" or "LB+amp" or "LB+amp+Ara")) || scenario.Plates.Any(p => !c.Outcomes.ContainsKey(p.Id))) throw new ArgumentException("A curated case does not contain a neutral title and all plate outcomes.");
            foreach (var outcome in c.Outcomes.Values) if (!Enum.IsDefined(outcome.Growth) || !Enum.IsDefined(outcome.Fluorescence) || (outcome.Growth == TransformationGrowth.Absent && outcome.Fluorescence != TransformationFluorescence.NotAssessable) || (outcome.Growth == TransformationGrowth.Present && outcome.Fluorescence == TransformationFluorescence.NotAssessable)) throw new ArgumentException("A curated outcome contradicts its observable state.");
            if (!CanonicalCaseOutcomeSignatures.TryGetValue(c.Id, out var signature) || CaseOutcomeSignature(c) != signature) throw new ArgumentException("A curated case does not match the approved fictional control pattern.");
            if (!CanonicalCaseSetupSignatures.TryGetValue(c.Id, out var setupSignature) || CaseSetupSignature(c.Setup) != setupSignature) throw new ArgumentException("A curated case does not match the approved fictional setup record.");
            if (c.Setup.PlateAssignments.Any(item => CaseSetupOutcome(c.Setup, item.Key).Growth != c.Outcomes[item.Key].Growth || CaseSetupOutcome(c.Setup, item.Key).Fluorescence != c.Outcomes[item.Key].Fluorescence)) throw new ArgumentException("A curated case setup does not agree with its declared observations.");
            if (c.SelectionClaimSupported != DerivedCaseSelectionSupported(c) || c.ExpressionClaimSupported != DerivedCaseExpressionSupported(c)) throw new ArgumentException("Curated case claim support must agree with its control outcomes.");
        }
    }
}
