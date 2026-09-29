using System.Net;
using CancerLabTrainer.Core;

namespace CancerLabTrainer.Ui;

/// <summary>General teaching text only; it never records or evaluates a learner action.</summary>
public static class PipettingQualityTeaching
{
    public sealed record Card(string Title, string Body);

    private static readonly Card SharedCard = new(
        "Pipetting quality: accuracy and precision",
        "Accuracy means how close the transferred volume is to the intended target. Precision means how closely repeated transfers agree with one another. For a fictional 50-unit target, 45, 45, 45 is precise because the transfers agree, but inaccurate because they share the same systematic error: a consistent bias. Nearly identical ATP-associated replicates are not proof of volume accuracy.\n\nA controlled gravimetric check is one way to evaluate transfer quality: it weighs liquid and relates mass to volume using density and appropriate conditions and corrections. This introduces the idea; it does not provide calibration instructions or certify equipment performance.\n\nInconsistent transfers can increase ATP replicate spread. A shared bias can shift the transferred volume or amount across affected wells in one direction. The signal or normalized effect then depends on what was transferred and on the assay; it is not guaranteed to be linear or to move every raw reading uniformly. ATP-associated light is a viability-related metabolic signal, not proof of cell death or a mechanism. The simulation’s pipette, quantities, and effects are illustrative, not physically validated or evidence of real competence.");

    public static bool HasSource(IEnumerable<SourceEntry> sources) => sources.Any(source => source.Id == "eppendorf-liquid-handling-sop");
    public static Card? OptionalWhy(RunMode mode, IEnumerable<SourceEntry> sources) => mode == RunMode.GuidedPractice && HasSource(sources) ? SharedCard : null;
    public static Card? DebriefCard(IEnumerable<SourceEntry> sources) => HasSource(sources) ? SharedCard : null;
    public static string? HtmlCard(IEnumerable<SourceEntry> sources) => !HasSource(sources) ? null : $"<h2>{WebUtility.HtmlEncode(SharedCard.Title)}</h2>" + string.Join("", SharedCard.Body.Split("\n\n").Select(paragraph => $"<p>{WebUtility.HtmlEncode(paragraph)}</p>"));
}
