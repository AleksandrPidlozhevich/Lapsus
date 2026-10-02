using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lapsus.Sweep;

internal enum Better
{
    Higher,
    Lower,

    Either
}

internal sealed record Metric(double Value, [property: JsonConverter(typeof(JsonStringEnumConverter))] Better Better);

internal sealed record Baseline(string Lang, int Lines, string Scoring, int TargetWords, Dictionary<string, Metric> Metrics)
{
    private const double Tolerance = 0.005;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string DefaultDirectory()
    {
        // Baselines live beside source; the tool runs from bin/.
        return RepositoryRoot() is { } root
            ? Path.Combine(root, "tools", "Lapsus.Sweep", "baseline")
            : Path.Combine(Environment.CurrentDirectory, "baseline");
    }

    public static string? RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Lapsus.slnx")))
            dir = dir.Parent;

        return dir?.FullName;
    }

    public static string PathFor(string directory, string lang)
    {
        return Path.Combine(directory, $"{lang}.json");
    }

    public void Write(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(PathFor(directory, Lang), JsonSerializer.Serialize(this, Json));
    }

    public static Baseline? Read(string directory, string lang)
    {
        var path = PathFor(directory, lang);
        return File.Exists(path) ? JsonSerializer.Deserialize<Baseline>(File.ReadAllText(path), Json) : null;
    }

    public bool Compare(Baseline current)
    {
        Report.Heading($"Against baseline ({Lines} lines, {Scoring}, {TargetWords} {Lang} words)");

        if (current.Lines != Lines || current.Scoring != Scoring)
        {
            Report.Line("not comparable", $"this run: {current.Lines} lines, {current.Scoring}");
            return true;
        }

        if (current.TargetWords != TargetWords)
            Report.Line("list changed", $"{TargetWords} → {current.TargetWords} words; the numbers below may move for that alone");

        var regressed = 0;
        var moved = 0;
        foreach (var (key, was) in Metrics)
        {
            if (!current.Metrics.TryGetValue(key, out var now))
            {
                Report.Line("gone", key);
                continue;
            }

            var delta = now.Value - was.Value;
            if (Math.Abs(delta) < 0.0005)
                continue;

            moved++;
            var worse = was.Better switch
            {
                Better.Higher => delta < -Tolerance,
                Better.Lower => delta > Tolerance,
                _ => false
            };
            var better = was.Better switch
            {
                Better.Higher => delta > Tolerance,
                Better.Lower => delta < -Tolerance,
                _ => false
            };

            if (worse)
                regressed++;

            var mark = worse ? "REGRESSED" : better ? "improved " : "moved    ";
            Report.Line($"{mark} {key}", $"{was.Value,7:P1} → {now.Value,7:P1}");
        }

        foreach (var key in current.Metrics.Keys.Where(k => !Metrics.ContainsKey(k)))
            Report.Line("new", key);

        if (moved == 0)
            Report.Line("unchanged", "every metric within 0.05 pp");

        return regressed == 0;
    }
}
