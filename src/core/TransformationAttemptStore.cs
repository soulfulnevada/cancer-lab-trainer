using System.Text;
using System.Text.Json;

namespace CancerLabTrainer.Transformation;

/// <summary>Owns only the isolated level2-transformation directory; it cannot enumerate or delete Level 1 records.</summary>
public sealed class TransformationAttemptStore
{
    private readonly string _root;
    private readonly string _attempts;
    private readonly string _exports;
    public List<string> Diagnostics { get; } = [];
    public TransformationAttemptStore(string? dataRoot = null)
    {
        var baseRoot = dataRoot ?? Environment.GetEnvironmentVariable("LAB_TRAINER_DATA_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot", "app_userdata", "Cancer Lab Trainer");
        _root = Path.Combine(Path.GetFullPath(baseRoot), "level2-transformation"); _attempts = Path.Combine(_root, "attempts"); _exports = Path.Combine(_root, "exports"); Directory.CreateDirectory(_attempts); Directory.CreateDirectory(_exports);
    }
    public void Save(TransformationSimulation simulation)
    {
        var id = simulation.Snapshot().AttemptId; var path = AttemptPath(id); var temp = path + ".tmp-" + Guid.NewGuid().ToString("N"); File.WriteAllText(temp, simulation.Save(), Encoding.UTF8); File.Move(temp, path, true);
    }
    public TransformationSimulation Load(string id) => TransformationSimulation.Restore(File.ReadAllText(AttemptPath(id), Encoding.UTF8));
    public IReadOnlyList<TransformationStoredAttempt> ListAttempts() { Diagnostics.Clear(); return Directory.EnumerateFiles(_attempts, "*.json").Select(path => TrySummary(path)).Where(x => x is not null).Cast<TransformationStoredAttempt>().OrderByDescending(x => x.UpdatedUtc).ToList(); }
    public void DeleteAttempt(string id) { var path = AttemptPath(id); if (File.Exists(path)) File.Delete(path); }
    public TransformationExportPaths Export(TransformationReport report)
    {
        var id = SafeId(report.AttemptId); var generation = id + "-" + Guid.NewGuid().ToString("N"); var stage = Path.Combine(_exports, ".partial-" + generation); var final = Path.Combine(_exports, generation); Directory.CreateDirectory(stage);
        var json = Path.Combine(stage, "report.json"); var csv = Path.Combine(stage, "report.csv"); var html = Path.Combine(stage, "report.html");
        File.WriteAllText(json, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8); File.WriteAllText(csv, TransformationSimulation.BuildCsv(report), Encoding.UTF8); File.WriteAllText(html, TransformationSimulation.BuildHtml(report), Encoding.UTF8);
        File.WriteAllText(Path.Combine(stage, "completion-manifest.json"), JsonSerializer.Serialize(new { report.AttemptId, report.ModelVersion, Json = "report.json", Csv = "report.csv", Html = "report.html" }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        Directory.Move(stage, final); return new TransformationExportPaths(Path.Combine(final, "report.json"), Path.Combine(final, "report.csv"), Path.Combine(final, "report.html"));
    }
    private TransformationStoredAttempt? TrySummary(string path) { try { var sim = TransformationSimulation.Restore(File.ReadAllText(path, Encoding.UTF8)); var s = sim.Snapshot(); return new TransformationStoredAttempt(s.AttemptId, s.Mode, s.Phase, File.GetLastWriteTimeUtc(path)); } catch (Exception error) { Diagnostics.Add(Path.GetFileName(path) + ": " + error.Message); return null; } }
    private string AttemptPath(string id) => Path.Combine(_attempts, SafeId(id) + ".json");
    private static string SafeId(string id) => Guid.TryParseExact(id, "N", out _) ? id : throw new ArgumentException("Attempt ID is invalid.");
}
public sealed record TransformationStoredAttempt(string Id, TransformationRunMode Mode, TransformationPhase Phase, DateTime UpdatedUtc);
