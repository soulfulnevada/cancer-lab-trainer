using System.Text.Json;
using System.Text.Json.Serialization;

namespace CancerLabTrainer.Core;

/// <summary>
/// The deep simulation module. Its interface is deliberately small: begin, submit one learner
/// action, observe the current state, save/restore, and obtain a debrief report.
/// </summary>
public sealed class LabSimulation
{
    public const string ModelVersion = "1.2.0";
    // 1.1.0 saves replay unchanged unless their controls failed after the reader ran.
    private static readonly string[] RestorableModelVersions = ["1.1.0", ModelVersion];
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ScenarioDefinition _scenario;
    private readonly TrainingRulesDefinition _rules;
    private readonly SourcesManifest _sources;
    private SimulationSnapshot _state = new();
    private readonly List<ScienceLesson> _lessons = [];

    public LabSimulation(ScenarioDefinition scenario, TrainingRulesDefinition? rules = null, SourcesManifest? sources = null)
    {
        ValidateScenario(scenario);
        _rules = rules ?? new TrainingRulesDefinition { SchemaVersion = 1, IllustrativeOnly = true, CriticalErrors = ["TransferBeforePpe", "WrongVolume", "MissingControl", "WrongWell", "Mislabel", "SampleIdentityMismatch", "TransferBeforeTraceability", "PlateOrientation", "ReaderMode", "Carryover", "FastAspirationRelease", "DuplicateTransfer", "UnsupportedConclusion"] };
        _sources = sources ?? new SourcesManifest { SchemaVersion = 1 };
        if (_rules.SchemaVersion != 1 || _sources.SchemaVersion != 1 || !_rules.IllustrativeOnly
            || _rules.CriticalErrors is null || _rules.AssessmentCategories is null || _sources.Entries is null
            || !double.IsFinite(_rules.MaxReplicateCvPercent) || _rules.MaxReplicateCvPercent <= 0
            || !double.IsFinite(_rules.MinimumReferenceAdjustedSignal) || _rules.MinimumReferenceAdjustedSignal <= 0
            || _sources.Entries.Any(s => s is null || !Uri.TryCreate(s.Url, UriKind.Absolute, out var url) || url.Scheme != "https")
            || !ChecksValid(_rules.ComprehensionChecks))
            throw new ArgumentException("Rules or source manifest is invalid or unsupported.");
        _scenario = JsonSerializer.Deserialize<ScenarioDefinition>(JsonSerializer.Serialize(scenario, JsonOptions), JsonOptions)!;
    }

    public static LabSimulation FromScenarioJson(string json) =>
        new(JsonSerializer.Deserialize<ScenarioDefinition>(json, JsonOptions) ?? throw new ArgumentException("Scenario JSON is invalid."));

    public static LabSimulation FromJsonDocuments(string scenarioJson, string rulesJson, string sourcesJson) => new(
        JsonSerializer.Deserialize<ScenarioDefinition>(scenarioJson, JsonOptions) ?? throw new ArgumentException("Scenario JSON is invalid."),
        JsonSerializer.Deserialize<TrainingRulesDefinition>(rulesJson, JsonOptions) ?? throw new ArgumentException("Rules JSON is invalid."),
        JsonSerializer.Deserialize<SourcesManifest>(sourcesJson, JsonOptions) ?? throw new ArgumentException("Sources JSON is invalid."));

    public SimulationSnapshot Start(RunMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Unknown learning mode.");
        _lessons.Clear();
        _state = new SimulationSnapshot
        {
            ScenarioId = _scenario.Id,
            ScenarioSchemaVersion = _scenario.SchemaVersion,
            AttemptId = Guid.NewGuid().ToString("N"),
            Mode = mode,
            Started = true,
            Phase = LabPhase.Preparation,
            SourceRemainingVolumeUl = new Dictionary<string, double>
            {
                ["Blank"] = 250, ["Vehicle control"] = 250, ["Fictional treatment"] = 250
            },
            Wells = _scenario.ActiveWells.Select(w => new WellRunState
            {
                Well = w.Well, Role = w.Role, ExpectedSignal = w.ExpectedSignal
            }).ToList()
        };
        return Snapshot();
    }

    public SimulationSnapshot Snapshot() => Clone(_state);

    public ActionResult Submit(LearnerAction action)
    {
        EnsureStarted();
        if (action is null || !Enum.IsDefined(action.Type)) return RejectWithSnapshot("A recognized action is required.");
        if (_state.Phase == LabPhase.Complete && action.Type != LabActionType.AnswerCheck) return RejectWithSnapshot("This completed attempt is locked. Start a new attempt to preserve its audit record.");
        if (_state.ReaderRan && action.Type is not (LabActionType.ReviewResults or LabActionType.DecideSupportedConclusion or LabActionType.EscalateInvalidRun or LabActionType.SortWaste or LabActionType.CleanBench or LabActionType.RecordHandoff or LabActionType.AnswerCheck))
            return RejectWithSnapshot("Acquired measurements are locked. Start a new attempt for a new preparation or reading.");
        if (action.Type is LabActionType.SortWaste or LabActionType.CleanBench or LabActionType.RecordHandoff && _state.Phase != LabPhase.Closeout)
            return RejectWithSnapshot("Record the result disposition before completing closeout.");
        var result = action.Type switch
        {
            LabActionType.WearPpe => SetOnce(() => _state.PpeWorn = true, _state.PpeWorn, "PPE check recorded.",
                "Preparation protects people and data", "Personal protective equipment is selected before handling materials. It reduces exposure risk and helps keep the workspace orderly.", "Skipping preparation can put people at risk and leaves no clear safety record."),
            LabActionType.DisinfectBench => SetOnce(() => _state.BenchDisinfected = true, _state.BenchDisinfected, "Workspace preparation recorded.",
                "Clean workspace", "A prepared bench separates the training task from residues or clutter left by earlier work.", "Contamination and mix-ups can change a measurement before the instrument ever sees the plate."),
            LabActionType.CheckMaterials => SetOnce(() => _state.MaterialsChecked = true, _state.MaterialsChecked, "Materials check recorded.",
                "Instrument readiness", "Checking materials and reader readiness before a run is a traceability step.", "Discovering a missing material midway can create an undocumented deviation."),
            LabActionType.VerifyLabels => SetOnce(() => _state.LabelsVerified = true, _state.LabelsVerified, "Sample identities verified.",
                "Traceability", "A sample label links a physical item to the plate map and later result.", "A label error can make a perfectly measured signal belong to the wrong sample."),
            LabActionType.ReviewPlateMap => SetOnce(() => _state.PlateMapReviewed = true, _state.PlateMapReviewed, "Plate map reviewed.",
                "Controls and replicates", "Blank wells estimate background; vehicle control wells provide a reference; replicate wells reveal variation.", "Without valid controls, a treatment-looking signal has no supported comparison."),
            LabActionType.AttachTip => AttachTip(),
            LabActionType.EjectTip => EjectTip(),
            LabActionType.ChangeTip => Rejected("Changing a tip cannot bypass a pipetting cycle. Eject the empty tip in air, then attach a fresh tip."),
            LabActionType.SetVolume => SetVolume(action.Value),
            LabActionType.SelectSource => SelectSource(action.Target),
            LabActionType.PressFirstStop => PressFirstStop(),
            LabActionType.MoveToSource => MoveToSource(action.Target),
            LabActionType.ReleaseSlow => ReleaseFromSource(false),
            LabActionType.ReleaseFast => ReleaseFromSource(true),
            LabActionType.MoveToDestination => MoveToDestination(action.Target),
            LabActionType.PressSecondStop => PressSecondStop(),
            LabActionType.WithdrawAndRelease => WithdrawAndRelease(),
            LabActionType.Aspirate => Rejected("Aspirate is no longer a shortcut. Press the first stop in air, move to the selected source, then release while immersed."),
            LabActionType.Dispense => Rejected("Dispense is no longer a shortcut. Position at the destination, press the first stop, clear with the second stop, then withdraw and release."),
            LabActionType.MislabelSelectedWell => Mislabel(action.Target),
            LabActionType.CorrectLabel => CorrectLabel(action.Target),
            LabActionType.RetryTransferCheckpoint => RetryTransferCheckpoint(),
            LabActionType.LoadPlate => LoadPlate(action.Target),
            LabActionType.ConfigureReader => ConfigureReader(action.Target),
            LabActionType.RunReader => RunReader(),
            LabActionType.ReviewResults => ReviewResults(),
            LabActionType.DecideSupportedConclusion => DecideConclusion(),
            LabActionType.EscalateInvalidRun => Escalate(),
            LabActionType.SortWaste => SetOnce(() => _state.WasteSorted = true, _state.WasteSorted, "Waste decision recorded.",
                "Closeout is part of the experiment", "Waste is sorted according to the local procedure; the simulation does not teach a real waste classification.", "A correct measurement still creates risk if materials are left for the next person without a clear disposition."),
            LabActionType.CleanBench => SetOnce(() => _state.BenchCleanedAtCloseout = true, _state.BenchCleanedAtCloseout, "Closeout cleaning recorded.",
                "Leave a usable workspace", "Cleanup ends the task and prepares the shared area for the next user.", "Closeout makes deviations visible instead of silently passing them forward."),
            LabActionType.RecordHandoff => SetOnce(() => _state.HandoffRecorded = true, _state.HandoffRecorded, "Handoff record completed.",
                "Scientific memory", "A handoff records what happened, including deviations and any escalation.", "Without documentation, a later reviewer cannot tell whether an unexpected result was experimental biology or a process problem."),
            LabActionType.AnswerCheck => AnswerCheck(action.Target),
            _ => Rejected("That action is not available in this version of the scenario.")
        };

        if (result.Accepted) _state.SimulatedMinutes += 2;
        _state.Ledger.Add(new LedgerEvent { SimulatedMinute = _state.SimulatedMinutes, AttemptNumber = _state.AttemptNumber, Action = action.Type.ToString(), Target = action.Target, Value = action.Value is { } v && double.IsFinite(v) ? v : null, Outcome = result.Message });
        UpdatePhase();
        if (result.Accepted && action.Type != LabActionType.AnswerCheck)
        {
            var lesson = new ScienceLesson(result.ScienceTitle, result.ScienceLesson, result.WhyItMatters);
            if (!_lessons.Contains(lesson)) _lessons.Add(lesson);
        }
        if (_state.Mode == RunMode.Assessment)
        {
            result.Message = result.Accepted ? "Action recorded. Details are held for the final debrief." : "Action was not recorded. Review the available workflow controls.";
            result.ScienceTitle = "Assessment record";
            result.ScienceLesson = "Procedural coaching is deferred until the final debrief.";
            result.WhyItMatters = "The same simulated outcomes and assessment rules apply in both modes.";
        }
        result.Snapshot = Snapshot();
        return result;
    }

    public string Save() => JsonSerializer.Serialize(new PersistedSession { Scenario = _scenario, Rules = _rules, Sources = _sources, Snapshot = _state }, JsonOptions);

    public static LabSimulation Restore(string json)
    {
        var saved = JsonSerializer.Deserialize<PersistedSession>(json, JsonOptions) ?? throw new ArgumentException("Saved session is invalid.");
        if (saved.FormatVersion != 3 || !RestorableModelVersions.Contains(saved.ModelVersion) || saved.Scenario is null || saved.Rules is null || saved.Sources is null || saved.Snapshot is null) throw new ArgumentException("This attempt uses an incompatible model or save format. Its file has been preserved.");
        var simulation = new LabSimulation(saved.Scenario, saved.Rules, saved.Sources);
        ValidateSnapshot(saved.Snapshot, saved.Scenario);
        // Rebuild from recorded actions so cached quantities, flags, readings and phase
        // cannot contradict the experiment history. This is consistency, not authentication.
        simulation.Start(saved.Snapshot.Mode);
        simulation._state.AttemptId = saved.Snapshot.AttemptId;
        foreach (var entry in saved.Snapshot.Ledger)
        {
            if (!Enum.TryParse<LabActionType>(entry.Action, out var type) || !Enum.IsDefined(type))
                throw new ArgumentException("Saved session contains an unknown action.");
            simulation.Submit(new LearnerAction(type, entry.Target, entry.Value));
        }
        if (JsonSerializer.Serialize(simulation._state, JsonOptions) != JsonSerializer.Serialize(saved.Snapshot, JsonOptions))
            throw new ArgumentException("Saved state does not agree with its action history. Its file has been preserved.");
        return simulation;
    }

    public static HistoricalAttempt ReadHistorical(string json)
    {
        var saved = JsonSerializer.Deserialize<PersistedSession>(json, JsonOptions) ?? throw new ArgumentException("Saved session is invalid.");
        if (saved.FormatVersion != 2 || saved.ModelVersion != "1.0.1" || saved.Snapshot is null)
            throw new ArgumentException("This file is not a recognized historical v1.0.1 attempt. Its file has been preserved.");
        if (!Guid.TryParseExact(saved.Snapshot.AttemptId, "N", out _) || saved.Snapshot.Wells is null || saved.Snapshot.Issues is null || saved.Snapshot.Ledger is null)
            throw new ArgumentException("Historical attempt is damaged. Its file has been preserved.");
        return new HistoricalAttempt { FormatVersion = saved.FormatVersion, ModelVersion = saved.ModelVersion, Snapshot = Clone(saved.Snapshot) };
    }

    public LabReport BuildReport()
    {
        var controlsValid = ComputeControlsValid();
        var canSupport = controlsValid && AllRequiredWellsReady() && !_state.Issues.Any(x => x.Critical && !x.Resolved) && _state.ReaderRan;
        return new LabReport
        {
            ScenarioId = _scenario.Id,
            ScenarioSchemaVersion = _scenario.SchemaVersion,
            AttemptId = _state.AttemptId,
            AttemptNumber = _state.AttemptNumber,
            ScenarioSeed = _scenario.Seed,
            RulesSchemaVersion = _rules.SchemaVersion,
            SourcesSchemaVersion = _sources.SchemaVersion,
            InterpretationDecision = _state.InterpretationDecision,
            Mode = _state.Mode,
            Complete = _state.Phase == LabPhase.Complete,
            ControlsValid = controlsValid,
            CanSupportConclusion = canSupport,
            Escalated = _state.Escalated,
            Conclusion = _state.Escalated
                ? "Run was escalated for review. No biological conclusion is claimed by this attempt."
                : _state.InterpretationDecision == "supported" && canSupport
                    ? "In this fictional training model, the treatment wells have a lower normalized luminescence signal than the vehicle controls. This supports an observed viability-associated difference only."
                    : _state.InterpretationDecision == "unsupported-claim"
                        ? "An unsupported conclusion was attempted; no biological conclusion is claimed by this attempt."
                        : "No conclusion was recorded. A valid measurement still needs an explicit, appropriately limited interpretation.",
            CategoryStatus = CategoryStatus(controlsValid, canSupport),
            Wells = _state.Wells.Select(Clone).ToList(),
            Issues = _state.Issues.Select(Clone).ToList(),
            Ledger = _state.Ledger.Select(x => new LedgerEvent { SimulatedMinute = x.SimulatedMinute, AttemptNumber = x.AttemptNumber, Action = x.Action, Target = x.Target, Value = x.Value, Outcome = x.Outcome }).ToList(),
            Sources = _sources.Entries.Select(x => new SourceEntry { Id = x.Id, Label = x.Label, Url = x.Url, Use = x.Use }).ToList(),
            Replicates = ReplicateSummaries(),
            Lessons = _state.Phase == LabPhase.Complete ? _lessons.ToList() : [],
            CheckAnswers = _state.CheckAnswers.Select(x => new CheckAnswer { QuestionId = x.QuestionId, OptionId = x.OptionId, Correct = x.Correct }).ToList()
        };
    }

    private ActionResult AttachTip()
    {
        if (_state.TipAttached) return Rejected("Eject the current empty tip in air before attaching another tip.");
        if (_state.PipetteStage != PipetteStage.ReadyInAir) return Rejected("Finish the active pipetting cycle before attaching a tip.");
        _state.TipAttached = true;
        _state.TipId++;
        _state.TipContaminationRole = null;
        _state.CarryoverRisk = false;
        return Accepted("Tip attached in air.", "Pipette tip history", "A tip is the disposable part that contacts liquid. This demo records it to teach traceability, not a real contamination policy.", "A fresh tip can help prevent material carryover between distinct samples. Local SOPs define the actual rule.");
    }

    private ActionResult EjectTip()
    {
        if (!_state.TipAttached) return Rejected("Attach a tip before ejecting it.");
        if (_state.PipetteStage != PipetteStage.ReadyInAir) return Rejected("A tip can be ejected only in air after the cycle has been completed.");
        _state.TipAttached = false;
        _state.TipContaminationRole = null;
        _state.CarryoverRisk = false;
        _state.SelectedSource = null;
        return Accepted("Empty tip ejected in air.", "Separate tip disposal", "The model separates an empty-tip ejection from liquid movement so a replacement cannot erase an unfinished cycle.", "This is an illustrative interaction guard. Local procedures define real disposal and tip-use rules.");
    }

    private ActionResult SetVolume(double? volume)
    {
        if (!_state.TipAttached) return Rejected("Attach a tip before setting up the illustrative transfer.");
        if (_state.PipetteStage != PipetteStage.ReadyInAir) return Rejected("Finish the active pipetting cycle before changing the volume setting.");
        if (volume is null || !double.IsFinite(volume.Value) || volume <= 0 || volume > 250) return Rejected("Choose an illustrative volume between 0 and 250 µL.");
        _state.SelectedVolumeUl = volume;
        return Accepted("Volume setting recorded.", "Accuracy and precision", "Accuracy means closeness to the intended amount; precision means repeated amounts are close to each other. The app uses a toy 50 uL target only to make error propagation visible.", "A volume mismatch changes the modeled signal. Real effects depend on the assay, liquids, equipment, and local procedure.");
    }

    private ActionResult SelectSource(string? source)
    {
        if (_state.PipetteStage != PipetteStage.ReadyInAir) return Rejected("The active cycle retains its source identity. Complete it before selecting another source.");
        if (source is null || !_state.SourceRemainingVolumeUl.ContainsKey(source)) return Rejected("Choose one of the visible fictional sample sources.");
        _state.SelectedSource = source;
        return Accepted($"Selected fictional source: {source}.", "Source identity", "Each source in this model has a finite toy inventory and planned destination wells.", "Keeping source identity attached to each transfer makes a sample swap visible rather than treating every liquid as interchangeable.");
    }

    private ActionResult PressFirstStop()
    {
        if (_state.PipetteStage == PipetteStage.ReadyInAir)
        {
            if (!_state.TipAttached || _state.SelectedVolumeUl is null || _state.SelectedSource is null) return Rejected("Attach a tip, select a source, and set a nominal volume before pressing the first stop in air.");
            if (_state.SourceRemainingVolumeUl[_state.SelectedSource] < _state.SelectedVolumeUl.Value) return Rejected("That fictional source does not have enough nominal volume remaining; adjust the setting before starting the cycle.");
            _state.PipetteStage = PipetteStage.FirstStopInAir;
            return Accepted("First stop pressed in air. Move the tip to the selected source before releasing.", "Forward aspiration sequence", "The illustrated forward sequence begins with the first stop in air before the tip enters the source.", "The order creates a clear learning record; this simplified scene does not model immersion depth, timing, or calibration.");
        }
        if (_state.PipetteStage == PipetteStage.LoadedAtDestination)
        {
            var source = _state.LoadedSource ?? throw new InvalidOperationException("Loaded source is missing.");
            var destination = _state.BoundDestination ?? throw new InvalidOperationException("Bound destination is missing.");
            var result = ApplyTransfer(destination, source, _state.AspiratedVolumeUl, _state.CarryoverRisk);
            if (!result.Accepted) return result;
            _state.PipetteStage = PipetteStage.FirstStopAtDestination;
            return Accepted($"Nominal {_state.SelectedVolumeUl:0.#} µL transfer recorded at {destination}. Keep the plunger pressed for the second stop.", "Nominal delivery at first stop", "In this training model, the first stop at the destination records the nominal set amount immediately.", "The second stop clears the modeled residue state; it does not add a fabricated extra volume.");
        }
        return Rejected("Press the first stop only in air before aspiration or at the bound destination after loading.");
    }

    private ActionResult MoveToSource(string? source)
    {
        if (_state.PipetteStage != PipetteStage.FirstStopInAir) return Rejected("Press the first stop in air before moving to a source.");
        if (source is not null && !string.Equals(source, _state.SelectedSource, StringComparison.Ordinal)) return Rejected("Move to the selected source; source identity is fixed for this cycle.");
        _state.PipetteStage = PipetteStage.FirstStopInSource;
        return Accepted($"Tip positioned in {_state.SelectedSource}. Release slowly while immersed to record the nominal load.", "Source position", "The source vial position is illustrative; the cap is visibly open so the scene does not imply a blocked immersion.", "The app records the documented order, not a physical depth, dwell time, or calibration claim.");
    }

    private ActionResult ReleaseFromSource(bool fast)
    {
        if (_state.PipetteStage != PipetteStage.FirstStopInSource || !_state.TipAttached || _state.SelectedVolumeUl is null || _state.SelectedSource is null)
            return Rejected("Press the first stop in air and move to the selected source before releasing.");
        if (_state.SourceRemainingVolumeUl[_state.SelectedSource] < _state.SelectedVolumeUl.Value) return Rejected("That fictional source does not have enough modeled volume remaining.");
        if (_state.TipContaminationRole is not null && _state.TipContaminationRole != _state.SelectedSource)
        {
            _state.CarryoverRisk = true;
            AddIssue("Carryover", "A tip was reused across distinct fictional sources. The model applies an illustrative carryover factor to the next dispense.", true, true);
        }
        _state.SourceRemainingVolumeUl[_state.SelectedSource] -= _state.SelectedVolumeUl.Value;
        _state.AspiratedVolumeUl = _state.SelectedVolumeUl.Value;
        _state.LoadedSource = _state.SelectedSource;
        _state.TipContaminationRole = _state.SelectedSource;
        _state.PipetteStage = PipetteStage.LoadedAtSource;
        if (fast) AddIssue("FastAspirationRelease", "The source release was recorded as fast. This is a qualitative, recoverable handling condition; the model does not quantify a physical volume error, so nominal inventory and fictional readings remain unchanged.", true, true);
        return Accepted($"Nominal {_state.AspiratedVolumeUl:0.#} µL loaded from {_state.SelectedSource}; the toy source inventory is conserved.", "Nominal source quantity", fast ? "A fast release is recorded as a qualitative training condition, not a percent-error or calibration calculation." : "A slow release is recorded while the illustrated tip is in the selected source. The quantity is nominal bookkeeping, not a physical calibration claim.", "Nominal conservation makes an impossible sequence visible without claiming a measured real-world volume.");
    }

    private ActionResult MoveToDestination(string? target)
    {
        if (_state.PipetteStage != PipetteStage.LoadedAtSource || _state.AspiratedVolumeUl <= 0 || _state.LoadedSource is null) return Rejected("Load a nominal quantity from a source before positioning at a destination.");
        var well = _state.Wells.FirstOrDefault(w => string.Equals(w.Well, target, StringComparison.OrdinalIgnoreCase));
        if (well is null) return Rejected("Choose one of the active training wells before positioning at a destination.");
        _state.BoundDestination = well.Well;
        _state.PipetteStage = PipetteStage.LoadedAtDestination;
        return Accepted($"Withdrew from {_state.LoadedSource} and positioned at {well.Well}. This well is now bound for the cycle.", "Destination binding", "The selected well is frozen when the loaded tip is positioned at the plate.", "Changing the visible well selection later cannot retarget liquid already positioned for this modeled transfer.");
    }

    private ActionResult PressSecondStop()
    {
        if (_state.PipetteStage != PipetteStage.FirstStopAtDestination) return Rejected("Press the destination first stop before clearing the modeled residue with the second stop.");
        _state.PipetteStage = PipetteStage.SecondStopAtDestination;
        return Accepted("Second stop recorded. No additional nominal microlitres were added.", "Clearing without inventing quantity", "The second stop records a completed clearing step in the forward sequence.", "The model does not claim a separate measured blow-out volume or calibration result.");
    }

    private ActionResult WithdrawAndRelease()
    {
        if (_state.PipetteStage != PipetteStage.SecondStopAtDestination) return Rejected("Complete the destination second stop before withdrawing while held and releasing in air.");
        _state.PipetteStage = PipetteStage.ReadyInAir;
        _state.LoadedSource = null;
        _state.BoundDestination = null;
        _state.CarryoverRisk = false;
        return Accepted("Tip withdrawn while held, then released in air. The pipette is ready for the next explicit cycle.", "Finish the forward cycle", "The illustrated sequence keeps the plunger pressed during withdrawal and releases only after the tip is back in air.", "This is a documented sequence model, not a substitute for a local SOP or hands-on assessment.");
    }

    private ActionResult ApplyTransfer(string? target, string sourceRole, double volume, bool carryoverRisk)
    {
        if (!_state.PpeWorn) AddIssue("TransferBeforePpe", "A transfer was attempted before the preparation check was recorded.", true, true);
        if (!_state.LabelsVerified || !_state.PlateMapReviewed) AddIssue("TransferBeforeTraceability", "A transfer was attempted before sample identity and plate-map checks.", true, true);
        var well = _state.Wells.FirstOrDefault(w => string.Equals(w.Well, target, StringComparison.OrdinalIgnoreCase));
        if (well is null)
        {
            AddIssue("WrongWell", $"{target ?? "No well"} is outside the active training layout.", true, true);
            _state.AspiratedVolumeUl = 0;
            return Accepted("The selected destination is not in the active layout; the error is preserved for debrief.", "Plate maps prevent quiet errors", "A plate map is the planned connection between location and sample identity.", "A wrong-well transfer can look like a biological effect unless traceability catches it.");
        }
        if (well.TransferVolumeUl is not null) AddIssue("DuplicateTransfer", $"{well.Well} received an additional modeled transfer.", true, true);
        if (sourceRole != well.Role) AddIssue("SampleIdentityMismatch", $"{sourceRole} was dispensed into {well.Well}, which is planned as {well.Role}.", true, true);
        if (Math.Abs(volume - _scenario.ToyTransferVolumeUl) > .01) AddIssue("WrongVolume", $"{well.Well} received {volume:0.#} µL instead of the scenario's {_scenario.ToyTransferVolumeUl:0.#} µL.", true, true);
        well.TransferVolumeUl = (well.TransferVolumeUl ?? 0) + volume;
        well.ContributionsUl[sourceRole] = well.ContributionsUl.GetValueOrDefault(sourceRole) + volume;
        well.TipId = _state.TipId;
        well.SourceRole = sourceRole;
        well.CarryoverFactor = carryoverRisk ? 1.08 : 1;
        _state.AspiratedVolumeUl = 0;
        return Accepted($"Illustrative dispense recorded for {well.Well} ({well.Role}).", "From liquid movement to a result", "Each active well receives a named fictional source in this scenario. The plate is a grid of separate mini-reactions.", "A plate can be measured quickly, but a source swap, wrong well, or illustrative carryover condition can change the controls or a replicate comparison.");
    }

    private ActionResult Mislabel(string? target)
    {
        if (_state.Wells.All(w => !string.Equals(w.Well, target, StringComparison.OrdinalIgnoreCase))) return Rejected("Select an active well before recording a label mismatch.");
        AddIssue("Mislabel", $"A fictional label mismatch was recorded for {target}. Correct it before acquisition or escalate it.", true, true, target!.ToUpperInvariant());
        return Accepted("Fictional label mismatch recorded.", "Labels create provenance", "The same number can tell a different story if its identity link is wrong.", "A label correction before acquisition is auditable; an unresolved label mismatch blocks interpretation.");
    }

    private ActionResult CorrectLabel(string? target)
    {
        if (!_state.Issues.Any(i => i.Code == "Mislabel" && !i.Resolved && i.Target == target?.ToUpperInvariant())) return Rejected("No active label mismatch is recorded for that well.");
        ResolveIssue("Mislabel", target?.ToUpperInvariant());
        return Accepted($"Label check for {target ?? "selected well"} recorded as corrected before acquisition.", "Correcting without erasing", "The training ledger keeps the earlier mismatch and marks it resolved before measurement.", "Auditable correction preserves what happened while allowing a correctly identified pre-acquisition run to proceed.");
    }

    private ActionResult RetryTransferCheckpoint()
    {
        if (_state.Mode != RunMode.GuidedPractice) return Rejected("Checkpoint retry is available only in Guided Practice; Assessment preserves the original attempt.");
        if (_state.ReaderRan) return Rejected("Start a new attempt after acquisition; this checkpoint only resets the pre-reader transfer stage.");
        foreach (var well in _state.Wells) { well.TransferVolumeUl = null; well.TipId = null; well.SourceRole = null; well.ContributionsUl.Clear(); well.CarryoverFactor = 1; well.RawReading = null; well.BackgroundAdjusted = null; well.RelativeToVehicle = null; }
        _state.SourceRemainingVolumeUl = new Dictionary<string, double> { ["Blank"] = 250, ["Vehicle control"] = 250, ["Fictional treatment"] = 250 };
        _state.TipAttached = false; _state.TipContaminationRole = null; _state.SelectedSource = null; _state.LoadedSource = null; _state.BoundDestination = null; _state.PipetteStage = PipetteStage.ReadyInAir; _state.AspiratedVolumeUl = 0; _state.CarryoverRisk = false; _state.SelectedVolumeUl = null;
        _state.PlateLoaded = false; _state.ReaderConfigured = false; _state.CorrectOrientation = false;
        _state.DiscardedVolumeUl = 0;
        foreach (var issue in _state.Issues.Where(i => i.Recoverable && !i.Resolved)) { issue.Resolved = true; issue.Message += " Resolved by Guided Practice transfer-checkpoint retry; retained in attempt lineage."; }
        _state.AttemptNumber++;
        return Accepted("Started a new Guided transfer checkpoint while preserving the earlier error ledger.", "Recovery with an audit trail", "Practice can reset the pre-reader transfer work without pretending the first attempt never happened.", "This separates learning recovery from silently overwriting an error record.");
    }

    private ActionResult LoadPlate(string? orientation)
    {
        if (_state.PipetteStage != PipetteStage.ReadyInAir) return Rejected("Complete the active pipette cycle before loading the plate.");
        _state.PlateLoaded = true;
        _state.CorrectOrientation = string.Equals(orientation, "correct", StringComparison.OrdinalIgnoreCase);
        if (!_state.CorrectOrientation) AddIssue("PlateOrientation", "Plate was loaded with an incorrect orientation in the training reader.", true, true);
        else ResolveIssue("PlateOrientation");
        return Accepted(_state.CorrectOrientation ? "Plate loaded with the A1 corner in the illustrated correct position." : "Incorrect orientation recorded for debrief.", "Plate orientation", "A plate position connects a physical grid to the software’s grid. Orientation is a traceability check.", "A rotated plate can assign a valid reading to the wrong planned sample.");
    }

    private ActionResult ConfigureReader(string? mode)
    {
        _state.ReaderConfigured = string.Equals(mode, _scenario.ReaderMode, StringComparison.OrdinalIgnoreCase);
        if (!_state.ReaderConfigured) AddIssue("ReaderMode", $"Reader was configured as {mode ?? "unspecified"}, not {_scenario.ReaderMode}.", true, true);
        else ResolveIssue("ReaderMode");
        return Accepted(_state.ReaderConfigured ? "Reader configured for the scenario’s luminescence mode." : "Reader configuration mismatch recorded for debrief.", "What the reader observes", "A luminescence reader measures light from the assay reaction. It does not directly watch cells dying.", "Instrument configuration determines what the numbers mean. A number without its measurement context is hard to interpret.");
    }

    private ActionResult RunReader()
    {
        if (_state.PipetteStage != PipetteStage.ReadyInAir) return Rejected("Complete the active pipette cycle before running the reader.");
        if (!_state.PlateLoaded || !_state.ReaderConfigured) return Rejected("Load the plate and configure the reader before running it.");
        foreach (var well in _state.Wells) well.RawReading = ModeledReading(well);
        var blanks = _state.Wells.Where(w => w.Role == "Blank").Select(w => w.RawReading ?? 0).ToList();
        var background = blanks.Count == 0 ? 0 : blanks.Average();
        foreach (var well in _state.Wells) well.BackgroundAdjusted = (well.RawReading ?? 0) - background;
        var vehicle = _state.Wells.Where(w => w.Role == "Vehicle control").Select(w => w.BackgroundAdjusted ?? 0).ToList();
        var vehicleMean = vehicle.Count == 0 ? 0 : vehicle.Average();
        foreach (var well in _state.Wells) well.RelativeToVehicle = vehicleMean > 0 ? (well.BackgroundAdjusted ?? 0) / vehicleMean * 100 : null;
        _state.ReaderRan = true;
        if (_state.Wells.Any(w => w.TransferVolumeUl is null)) AddIssue("MissingControl", "Required samples or controls were not prepared. The partial plate cannot support a treatment comparison.", true, false);
        if (ReplicateSummaries().Any(r => r.Role != "Blank" && r.CvPercent > _rules.MaxReplicateCvPercent)) AddIssue("ReplicateVariation", "Technical replicate spread exceeds this scenario's illustrative QC threshold.", true, false);
        _state.ControlsValid = ComputeControlsValid();
        _state.CanSupportConclusion = _state.ControlsValid && AllRequiredWellsReady() && !_state.Issues.Any(x => x.Critical && !x.Resolved);
        // Without a valid blank and vehicle reference the percentage has no sound basis, so it is withheld.
        if (!_state.ControlsValid) foreach (var well in _state.Wells) well.RelativeToVehicle = null;
        return Accepted(_state.ControlsValid ? "Fictional readings generated from the versioned scenario seed." : "Fictional readings generated from the versioned scenario seed. The blank or vehicle reference is not valid, so no percentage of vehicle is calculated.", "Signal, background, and comparison", "Raw light includes background. The app subtracts the average blank signal, then expresses each active well relative to the vehicle-control average.", "These calculations organize a comparison; they do not tell you why a signal changed or establish a real effect.");
    }

    private ActionResult ReviewResults()
    {
        if (!_state.ReaderRan) return Rejected("Run the reader before reviewing results.");
        _state.ResultsReviewed = true;
        return Accepted(_state.CanSupportConclusion ? "Controls pass the fictional model’s validity checks." : "The model marks this run as unsuitable for a supported conclusion: " + string.Join("; ", SupportProblems()) + ".", "Interpretation starts with controls", "Replicate spread gives context for a mean, and controls tell whether a comparison is interpretable.", "A lower treatment signal is an observation. It becomes a supported comparison only when the necessary controls and traceability conditions hold.");
    }

    private ActionResult DecideConclusion()
    {
        if (!_state.ResultsReviewed) return Rejected("Review the results before recording a conclusion.");
        if (!_state.CanSupportConclusion && _state.Mode == RunMode.GuidedPractice)
            return Rejected("This run cannot support a conclusion: " + string.Join("; ", SupportProblems()) + ". Escalate it for review instead.");
        if (!_state.CanSupportConclusion)
        {
            _state.InterpretationDecision = "unsupported-claim";
            AddIssue("UnsupportedConclusion", "A supported conclusion was selected despite invalid controls or workflow conditions.", true, true);
            return Accepted("Unsupported conclusion recorded for debrief. Escalation remains available.", "Know when not to conclude", "An experiment can produce numbers yet still be invalid for the question being asked.", "Recognizing that limit protects later decisions from an attractive but unsupported result.");
        }
        _state.InterpretationDecision = "supported";
        return Accepted("Supported fictional observation recorded.", "Claim only what the data support", "The valid run supports a lower viability-associated signal in fictional treatment wells relative to fictional vehicle controls.", "The model does not identify a mechanism, predict a real drug response, or prove that cells died.");
    }

    private ActionResult Escalate()
    {
        if (!_state.ReaderRan) return Rejected("Escalation is available after a measurement or documented invalid condition.");
        _state.Escalated = true;
        return Accepted(_state.CanSupportConclusion ? "Run escalated for review despite passing the fictional QC checks; no conclusion will be claimed." : "Invalid run escalated and preserved in the handoff record.", "Escalation is scientific judgment", "Escalation documents that a control or setup condition prevents a supported conclusion.", "Stopping an invalid run is often better practice than forcing an interpretation from unreliable evidence.");
    }

    private ActionResult AnswerCheck(string? target)
    {
        if (!_state.ResultsReviewed) return Rejected("Review the results before answering the check questions.");
        var parts = (target ?? "").Split(':', 2);
        var check = _rules.ComprehensionChecks.FirstOrDefault(c => c.Id == parts[0]);
        if (check is null || parts.Length != 2) return Rejected("Choose one of the listed check questions.");
        if (check.Stage == "debrief" && _state.Phase != LabPhase.Complete) return Rejected("These questions open after the debrief.");
        if (_state.CheckAnswers.Any(a => a.QuestionId == check.Id)) return Rejected("That question already has a recorded answer.");
        var option = check.Options.FirstOrDefault(o => o.Id == parts[1]);
        if (option is null) return Rejected("Choose one of the listed answers.");
        var correct = option.Id == check.CorrectOptionId;
        _state.CheckAnswers.Add(new CheckAnswer { QuestionId = check.Id, OptionId = option.Id, Correct = correct });
        var best = check.Options.Single(o => o.Id == check.CorrectOptionId).Text;
        return Accepted(correct ? "Correct. Your answer is recorded." : "Not quite. Your answer is recorded. The best answer: " + best + ".", "Check your understanding", check.Explanation, "Your first answer to each question is kept in the attempt record.");
    }

    private static readonly Dictionary<string, string> FlagLabels = new()
    {
        ["TransferBeforePpe"] = "transfer before PPE",
        ["WrongVolume"] = "wrong volume",
        ["ReaderMode"] = "reader mode",
        ["Carryover"] = "tip carryover",
        ["FastAspirationRelease"] = "fast aspiration release",
        ["DuplicateTransfer"] = "duplicate transfer",
        ["UnsupportedConclusion"] = "unsupported conclusion",
        ["ReplicateVariation"] = "replicate spread above the QC threshold"
    };

    /// <summary>Plain-language reasons a run cannot support a conclusion, in a fixed order.</summary>
    private List<string> SupportProblems()
    {
        var problems = new List<string>();
        var controls = _state.Wells.Where(w => w.Role is "Blank" or "Vehicle control").ToList();
        var treatments = _state.Wells.Where(w => w.Role == "Fictional treatment").ToList();
        bool WrongVolume(WellRunState w) => Math.Abs((w.TransferVolumeUl ?? 0) - _scenario.ToyTransferVolumeUl) >= 0.01;
        bool WrongLiquid(WellRunState w) => w.SourceRole is not null && w.SourceRole != w.Role;
        if (!_state.CorrectOrientation) problems.Add("the plate orientation is incorrect");
        if (controls.Any(WrongVolume)) problems.Add("a blank or vehicle well is missing or has the wrong volume");
        if (controls.Any(WrongLiquid)) problems.Add("a blank or vehicle well received the wrong liquid");
        if (_state.ReaderRan && controls.Where(w => w.Role == "Vehicle control").Average(w => w.BackgroundAdjusted ?? 0) < _rules.MinimumReferenceAdjustedSignal)
            problems.Add("the vehicle reference signal is too low to compare against");
        if (_state.Issues.Any(x => !x.Resolved && x.Code is "WrongWell" or "Mislabel" or "SampleIdentityMismatch" or "TransferBeforeTraceability")) problems.Add("a traceability issue is unresolved");
        if (treatments.Any(WrongVolume)) problems.Add("a treatment well is missing or has the wrong volume");
        if (treatments.Any(WrongLiquid)) problems.Add("a treatment well received the wrong liquid");
        var flags = _state.Issues.Where(x => x.Critical && !x.Resolved && x.Code is not ("WrongWell" or "Mislabel" or "SampleIdentityMismatch" or "TransferBeforeTraceability" or "PlateOrientation" or "MissingControl")).Select(x => FlagLabels.GetValueOrDefault(x.Code, x.Code)).Distinct().ToList();
        if (flags.Count > 0) problems.Add("unresolved handling flags: " + string.Join(", ", flags));
        if (problems.Count == 0) problems.Add("the controls or workflow conditions do not pass the model's checks");
        return problems;
    }

    private static bool ChecksValid(List<ComprehensionCheck>? checks) => checks is not null
        && checks.Select(c => c?.Id).Distinct().Count() == checks.Count
        && checks.All(c => c is not null && c.Id.Length > 0 && c.Stage is "follow-up" or "debrief" && c.Prompt.Length > 0 && c.Explanation.Length > 0
            && c.Options is { Count: >= 2 } && c.Options.All(o => o is not null && o.Id.Length > 0 && o.Text.Length > 0)
            && c.Options.Select(o => o.Id).Distinct().Count() == c.Options.Count && c.Options.Any(o => o.Id == c.CorrectOptionId));

    private double ModeledReading(WellRunState well)
    {
        var factor = well.TransferVolumeUl is null ? 0.02 : well.TransferVolumeUl.Value / _scenario.ToyTransferVolumeUl * well.CarryoverFactor;
        var sourceSignal = well.ContributionsUl.Count == 0 || well.ContributionsUl.Count == 1 && well.ContributionsUl.ContainsKey(well.Role) ? well.ExpectedSignal
            : well.ContributionsUl.Sum(c => c.Value * _scenario.ActiveWells.Where(w => w.Role == c.Key).Average(w => w.ExpectedSignal)) / well.ContributionsUl.Values.Sum();
        var random = new Random(_scenario.Seed + WellIndex(well.Well) * 7919);
        var noise = (random.NextDouble() - 0.5) * 18;
        return Math.Round(Math.Max(0, sourceSignal * factor + noise), 1);
    }

    private bool ComputeControlsValid()
    {
        var controlWells = _state.Wells.Where(w => w.Role is "Blank" or "Vehicle control").ToList();
        var controlsTransferred = controlWells.Count >= 4 && controlWells.All(w => Math.Abs((w.TransferVolumeUl ?? 0) - _scenario.ToyTransferVolumeUl) < 0.01);
        return _state.ReaderRan && _state.CorrectOrientation && _state.ReaderConfigured && controlsTransferred
            && controlWells.All(w => w.SourceRole == w.Role)
            && controlWells.Where(w => w.Role == "Vehicle control").Average(w => w.BackgroundAdjusted ?? 0) >= _rules.MinimumReferenceAdjustedSignal
            && !_state.Issues.Any(x => !x.Resolved && x.Code is "WrongWell" or "Mislabel" or "SampleIdentityMismatch" or "TransferBeforeTraceability" or "PlateOrientation" or "ReaderMode");
    }

    private bool AllRequiredWellsReady() => _state.Wells.Count == _scenario.ActiveWells.Count
        && _state.Wells.All(w => Math.Abs((w.TransferVolumeUl ?? 0) - _scenario.ToyTransferVolumeUl) < 0.01 && w.RawReading is not null && w.SourceRole == w.Role);

    private List<ReplicateSummary> ReplicateSummaries() => _state.Wells.Where(w => w.RawReading.HasValue).GroupBy(w => w.Role).Select(group =>
    {
        var values = group.Select(w => w.RawReading!.Value).ToArray();
        var mean = values.Average();
        var sd = values.Length > 1 ? Math.Sqrt(values.Sum(v => Math.Pow(v - mean, 2)) / (values.Length - 1)) : 0;
        return new ReplicateSummary(group.Key, values.Length, mean, sd, mean > 0 ? sd / mean * 100 : null);
    }).ToList();

    private static void ValidateScenario(ScenarioDefinition? scenario)
    {
        if (scenario is null || scenario.SchemaVersion != 1) throw new ArgumentException("Only scenario schema version 1 is supported.");
        if (string.IsNullOrWhiteSpace(scenario.Id) || string.IsNullOrWhiteSpace(scenario.ReaderMode) || !double.IsFinite(scenario.ToyTransferVolumeUl) || scenario.ToyTransferVolumeUl <= 0)
            throw new ArgumentException("Scenario identity, reader mode, and toy volume must be valid.");
        if (scenario.ActiveWells is null || scenario.ActiveWells.Count < 6) throw new ArgumentException("The training scenario requires active control and treatment wells.");
        if (scenario.ActiveWells.Any(w => w is null || !IsValidWell(w.Well) || string.IsNullOrWhiteSpace(w.Role) || !double.IsFinite(w.ExpectedSignal) || w.ExpectedSignal < 0))
            throw new ArgumentException("Scenario contains an invalid active-well definition.");
        if (scenario.ActiveWells.Select(w => w.Well).Distinct(StringComparer.OrdinalIgnoreCase).Count() != scenario.ActiveWells.Count)
            throw new ArgumentException("Scenario contains duplicate active wells.");
        if (scenario.ActiveWells.Count(w => w.Role == "Blank") < 2 || scenario.ActiveWells.Count(w => w.Role == "Vehicle control") < 2 || scenario.ActiveWells.Count(w => w.Role == "Fictional treatment") < 2)
            throw new ArgumentException("Scenario requires two blanks, two vehicle controls, and two fictional treatment replicates.");
    }

    private static void ValidateSnapshot(SimulationSnapshot snapshot, ScenarioDefinition scenario)
    {
        if (!snapshot.Started || snapshot.Wells is null || snapshot.Issues is null || snapshot.Ledger is null || snapshot.SourceRemainingVolumeUl is null
            || !Guid.TryParseExact(snapshot.AttemptId, "N", out _) || !Enum.IsDefined(snapshot.Mode) || !Enum.IsDefined(snapshot.Phase)
            || snapshot.Wells.Any(w => w is null || w.ContributionsUl is null) || snapshot.Issues.Any(i => i is null)
            || snapshot.Ledger.Any(e => e is null) || snapshot.ScenarioId != scenario.Id || snapshot.ScenarioSchemaVersion != scenario.SchemaVersion)
            throw new ArgumentException("Saved session provenance or state is invalid.");
        var scenarioWells = scenario.ActiveWells.Select(w => w.Well).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var savedWells = snapshot.Wells.Select(w => w.Well).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!scenarioWells.SequenceEqual(savedWells, StringComparer.Ordinal)
            || snapshot.Wells.Any(w => w.TransferVolumeUl is { } v && (!double.IsFinite(v) || v < 0)
                || w.ContributionsUl.Any(c => !double.IsFinite(c.Value) || c.Value < 0 || c.Key is not ("Blank" or "Vehicle control" or "Fictional treatment"))
                || w.Role != scenario.ActiveWells.Single(s => s.Well == w.Well).Role)
            || snapshot.SourceRemainingVolumeUl.Count != 3 || new[] { "Blank", "Vehicle control", "Fictional treatment" }.Any(role => !snapshot.SourceRemainingVolumeUl.ContainsKey(role))
            || snapshot.SourceRemainingVolumeUl.Any(p => !double.IsFinite(p.Value) || p.Value < 0 || p.Value > 250)
            || !Enum.IsDefined(snapshot.PipetteStage)
            || snapshot.SelectedSource is not null && !snapshot.SourceRemainingVolumeUl.ContainsKey(snapshot.SelectedSource)
            || snapshot.LoadedSource is not null && !snapshot.SourceRemainingVolumeUl.ContainsKey(snapshot.LoadedSource)
            || snapshot.BoundDestination is not null && !scenario.ActiveWells.Any(w => string.Equals(w.Well, snapshot.BoundDestination, StringComparison.OrdinalIgnoreCase))
            || !double.IsFinite(snapshot.AspiratedVolumeUl) || snapshot.AspiratedVolumeUl < 0 || snapshot.AspiratedVolumeUl > 250
            || snapshot.SelectedVolumeUl is { } volume && (!double.IsFinite(volume) || volume <= 0 || volume > 250))
            throw new ArgumentException("Saved session wells are incompatible with this scenario.");
        if (snapshot.PipetteStage == PipetteStage.ReadyInAir && (snapshot.LoadedSource is not null || snapshot.BoundDestination is not null || snapshot.AspiratedVolumeUl != 0)
            || snapshot.PipetteStage is PipetteStage.LoadedAtSource or PipetteStage.LoadedAtDestination && (snapshot.LoadedSource is null || snapshot.AspiratedVolumeUl <= 0)
            || snapshot.PipetteStage is PipetteStage.FirstStopAtDestination or PipetteStage.SecondStopAtDestination && (snapshot.LoadedSource is null || snapshot.BoundDestination is null || snapshot.AspiratedVolumeUl != 0))
            throw new ArgumentException("Saved pipette cycle is incompatible with the model.");
    }

    private Dictionary<string, string> CategoryStatus(bool controlsValid, bool canSupport) => new()
    {
        ["Preparation"] = _state.PpeWorn && _state.BenchDisinfected && _state.MaterialsChecked ? "Complete" : "Incomplete",
        ["Traceability"] = _state.LabelsVerified && _state.PlateMapReviewed && _state.CorrectOrientation && !_state.Issues.Any(i => !i.Resolved && i.Code is "Mislabel" or "SampleIdentityMismatch" or "WrongWell") ? "Complete" : "Needs review",
        ["Handling"] = AllRequiredWellsReady() && !_state.Issues.Any(i => !i.Resolved && i.Critical) ? "Complete" : "Needs review",
        ["Interpretation"] = _state.ResultsReviewed && (_state.InterpretationDecision == "supported" && canSupport || _state.Escalated) ? "Complete" : "Needs review",
        ["Closeout"] = _state.WasteSorted && _state.BenchCleanedAtCloseout && _state.HandoffRecorded ? "Complete" : "Incomplete"
    };

    private void UpdatePhase()
    {
        if (!_state.Started) { _state.Phase = LabPhase.Welcome; return; }
        if (!(_state.PpeWorn && _state.BenchDisinfected && _state.MaterialsChecked)) { _state.Phase = LabPhase.Preparation; return; }
        if (!(_state.LabelsVerified && _state.PlateMapReviewed)) { _state.Phase = LabPhase.Organization; return; }
        if (!_state.ReaderRan) { _state.Phase = LabPhase.Transfers; return; }
        if (!(_state.ResultsReviewed && (_state.InterpretationDecision == "supported" || _state.Escalated))) { _state.Phase = LabPhase.Interpretation; return; }
        _state.Phase = _state.WasteSorted && _state.BenchCleanedAtCloseout && _state.HandoffRecorded ? LabPhase.Complete : LabPhase.Closeout;
    }

    private ActionResult SetOnce(Action setter, bool alreadySet, string message, string title, string lesson, string why)
    {
        if (alreadySet) return Rejected("That item is already recorded.");
        setter();
        return Accepted(message, title, lesson, why);
    }

    private void AddIssue(string code, string message, bool critical, bool recoverable, string? target = null)
    {
        if (_state.Issues.Any(x => x.Code == code && x.Target == target && !x.Resolved)) return;
        _state.Issues.Add(new SimulationIssue { Code = code, Target = target, Message = message, Critical = critical && (_rules.CriticalErrors.Contains(code) || code == "ReplicateVariation"), Recoverable = recoverable });
    }
    private void ResolveIssue(string code, string? target = null)
    {
        foreach (var issue in _state.Issues.Where(x => x.Code == code && x.Target == target && x.Recoverable && !x.Resolved))
        {
            issue.Resolved = true;
            issue.Message += " Corrected before acquisition; retained as an audit note.";
        }
    }

    private ActionResult Accepted(string message, string title, string lesson, string why) => new()
    {
        Accepted = true, Message = message, ScienceTitle = title, ScienceLesson = lesson, WhyItMatters = why
    };
    private ActionResult Rejected(string message) => new()
    {
        Accepted = false, Message = message, ScienceTitle = "Check the workflow", ScienceLesson = "The simulation keeps actions ordered so that the next observation has context.", WhyItMatters = "In real work, pause and follow the local procedure when the next safe step is unclear."
    };
    private ActionResult RejectWithSnapshot(string message)
    {
        var result = Rejected(_state.Mode == RunMode.Assessment ? "Action is unavailable at this stage." : message);
        result.Snapshot = Snapshot();
        return result;
    }
    private void EnsureStarted() { if (!_state.Started) throw new InvalidOperationException("Start a scenario before submitting actions."); }
    private static bool IsValidWell(string? well) => well is { Length: >= 2 and <= 3 }
        && well[0] is >= 'A' and <= 'H' && int.TryParse(well[1..], out var column) && column is >= 1 and <= 12;
    private static int WellIndex(string well) => IsValidWell(well) ? (well[0] - 'A') * 12 + int.Parse(well[1..]) : throw new ArgumentException("Invalid well identifier.");
    private static SimulationSnapshot Clone(SimulationSnapshot source) => JsonSerializer.Deserialize<SimulationSnapshot>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions)!;
    private static WellRunState Clone(WellRunState source) => JsonSerializer.Deserialize<WellRunState>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions)!;
    private static SimulationIssue Clone(SimulationIssue source) => JsonSerializer.Deserialize<SimulationIssue>(JsonSerializer.Serialize(source, JsonOptions), JsonOptions)!;
}
