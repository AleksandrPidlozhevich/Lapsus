using System.Collections.Generic;
using System.IO;
using Lapsus.Core.Correction;
using Lapsus.Core.Layout;
using Lapsus.Core.Spelling;

namespace Lapsus.Core.Tests.Languages;

internal sealed class ScriptHarness : IDisposable
{
    private readonly List<string> _paths = [];
    private readonly List<DictionarySource> _sources = [];

    public ScriptHarness With(string code, Script script, params string[] entries)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lapsus-{code}-{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, entries);
        _paths.Add(path);
        _sources.Add(new DictionarySource(code, path, script));
        return this;
    }

    public LayoutCorrector Corrector()
    {
        return new LayoutCorrector(new SpellChecker(_sources));
    }

    public void Dispose()
    {
        foreach (var path in _paths)
            File.Delete(path);
    }
}

internal static class Layouts
{
    public static KeyboardMap Map(KeyboardLayout layout)
    {
        return ScriptLayouts.MapFor(layout);
    }

    public static LayoutCandidate To(KeyboardLayout layout, string code)
    {
        return new LayoutCandidate(ScriptLayouts.ScriptOf(layout), layout, Map(layout), code, code + "-id");
    }

    public static List<LayoutCandidate> ToOnly(KeyboardLayout layout, string code)
    {
        return [To(layout, code)];
    }

    public static List<LayoutCandidate> ToEnglish => ToOnly(KeyboardLayout.En, "en");
}
