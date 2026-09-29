using CancerLabTrainer.Transformation;

namespace CancerLabTrainer.Ui;

/// <summary>Formats report-backed claim text without disclosing diagnostic support before debrief.</summary>
public static class Level2ClaimDisplay
{
    public static string Sidebar(TransformationReport report)
    {
        var text = "[b]Evidence and limits[/b]\n" + MainClaims(report, richText: true);
        if (report.DiagnosticCase is not null && report.Complete) text += "\n\n" + CaseClaims(report, richText: true);
        else if (report.CaseObservations.Count > 0) text += "\n\n[b]Active case — observed record only[/b]\nReview the viewed case observations and evidence cards. Case claim support, reasons, and fictional cause are held for debrief.";
        return text + "\n\n[b]Source register[/b]\n" + string.Join("\n", report.Sources.Select(source => "* " + source.Label));
    }

    public static string Debrief(TransformationReport report)
    {
        var text = MainClaims(report, richText: false);
        return report.DiagnosticCase is null ? text : text + "\n\n" + CaseClaims(report, richText: false);
    }

    private static string MainClaims(TransformationReport report, bool richText) => Heading("Main experiment — selection support: " + report.MainSelectionClaimSupported, richText) + "\n" + report.ClaimReasons.GetValueOrDefault("main-selection", "No main selection reason is available.") + "\n\n" + Heading("Main experiment — expression support: " + report.MainExpressionClaimSupported, richText) + "\n" + report.ClaimReasons.GetValueOrDefault("main-expression", "No main expression reason is available.");

    private static string CaseClaims(TransformationReport report, bool richText)
    {
        var diagnostic = report.DiagnosticCase!;
        return Heading("Case debrief — selection support: " + diagnostic.SelectionClaimSupported, richText) + "\n" + diagnostic.SelectionClaimReason + "\n\n" + Heading("Case debrief — expression support: " + diagnostic.ExpressionClaimSupported, richText) + "\n" + diagnostic.ExpressionClaimReason;
    }

    private static string Heading(string text, bool richText) => richText ? "[b]" + text + "[/b]" : text;
}
