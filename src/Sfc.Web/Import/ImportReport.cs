namespace Sfc.Web.Import;

/// <summary>Per-file tallies plus human-readable errors. Import never throws on bad
/// rows: it records them and carries on, so one typo does not hide the other twenty.</summary>
public class ImportReport
{
    private readonly Dictionary<string, int> _created = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _skipped = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _errors = [];

    public IReadOnlyList<string> Errors => _errors;
    public bool HasErrors => _errors.Count > 0;

    public void Created(string file) => Bump(_created, file);
    public void Skipped(string file) => Bump(_skipped, file);
    public void Error(string message) => _errors.Add(message);

    public int CountCreated(string file) => _created.GetValueOrDefault(file);
    public int CountSkipped(string file) => _skipped.GetValueOrDefault(file);

    public string Summary()
    {
        var files = _created.Keys.Union(_skipped.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        var lines = files.Select(f =>
            $"  {f}: {CountCreated(f)} criados, {CountSkipped(f)} saltados");

        return string.Join(Environment.NewLine, lines);
    }

    private static void Bump(Dictionary<string, int> counter, string file)
        => counter[file] = counter.GetValueOrDefault(file) + 1;
}
