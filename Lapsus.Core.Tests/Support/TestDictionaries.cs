using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;
using System.Security.Cryptography;
using System.Text;

namespace Lapsus.Core.Tests.Support;

// The one place tests get dictionaries from: lists written for the test, or the generated lists in
// Languages/Fixtures. Written files share one folder per test run, removed when the run ends; a folder a
// crashed run left behind is removed by the next one. Never the app's own dictionary folder.
internal sealed class TestDictionaries
{
    private const string Prefix = "lapsus-tests-";

    private static readonly string Root = CreateRoot();

    private readonly List<DictionarySource> _sources = [];

    public static string FixtureDir => Path.Combine(AppContext.BaseDirectory, "Languages", "Fixtures");

    public static string Fixture(string code)
    {
        return Path.Combine(FixtureDir, $"{code}.words.txt");
    }

    // A frequency list: one "word count" per line, never to be changed by the test. The same lines get the same
    // file, so SpellChecker's process-wide cache, which keeps every list it loaded until the file is gone, holds
    // one entry per list instead of one per test (and checks every entry on each load).
    public static string List(string tag, params IEnumerable<string> lines)
    {
        var content = string.Join("\n", lines) + "\n";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..16];
        var path = Path.Combine(Root, $"{tag}-{hash}.txt");
        if (File.Exists(path))
            return path;

        // Written aside and moved in, so a test running alongside never reads half a list.
        var partial = NewPath(tag, ".part");
        File.WriteAllText(partial, content, new UTF8Encoding(false));
        try
        {
            File.Move(partial, path);
        }
        catch (IOException) when (File.Exists(path))
        {
            File.Delete(partial);
        }

        return path;
    }

    // Any file exactly as given (UTF-8, no BOM), at a path of its own: Hunspell .aff/.dic, a list with odd lines,
    // a list the test cleans in place.
    public static string Text(string tag, string content, string extension = ".txt")
    {
        var path = NewPath(tag, extension);
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    // A Hunspell pair: the .dic path, with its .aff beside it.
    public static string Hunspell(string tag, string words, string affix)
    {
        var dic = Text(tag, words, ".dic");
        File.WriteAllText(Path.ChangeExtension(dic, ".aff"), affix, new UTF8Encoding(false));
        return dic;
    }

    // A fresh path in the run's folder, not yet written: for a file the test writes itself, or one meant to be missing.
    public static string NewFile(string tag, string extension)
    {
        return NewPath(tag, extension);
    }

    public static string Folder(string tag)
    {
        var path = NewPath(tag, string.Empty);
        Directory.CreateDirectory(path);
        return path;
    }

    public IReadOnlyList<DictionarySource> Sources => _sources;

    public TestDictionaries With(string code, Script script, params IEnumerable<string> entries)
    {
        _sources.Add(new DictionarySource(code, List(code, entries), script));
        return this;
    }

    public TestDictionaries WithFixture(string code, Script script)
    {
        _sources.Add(new DictionarySource(code, Fixture(code), script));
        return this;
    }

    public SpellChecker SpellChecker()
    {
        return new SpellChecker(_sources);
    }

    public LayoutCorrector Corrector()
    {
        return new LayoutCorrector(SpellChecker());
    }

    private static string NewPath(string tag, string extension)
    {
        return Path.Combine(Root, $"{tag}-{Guid.NewGuid():N}{extension}");
    }

    private static string CreateRoot()
    {
        var temp = Path.GetTempPath();
        foreach (var stale in Directory.EnumerateDirectories(temp, Prefix + "*"))
            if (!IsRunning(stale) || Directory.GetLastWriteTimeUtc(stale) < DateTime.UtcNow.AddDays(-1))
                TryDelete(stale);

        var root = Path.Combine(temp, $"{Prefix}{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(root);
        return root;
    }

    // "lapsus-tests-{pid}-…": the run that made it may still be going (another test project, a parallel run).
    private static bool IsRunning(string folder)
    {
        var name = Path.GetFileName(folder)[Prefix.Length..];
        var dash = name.IndexOf('-');
        if (dash < 0 || !int.TryParse(name[..dash], out var pid))
            return false;

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static void TryDelete(string folder)
    {
        try
        {
            Directory.Delete(folder, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Still open elsewhere, or another run's; the next run sweeps it.
        }
    }
}
