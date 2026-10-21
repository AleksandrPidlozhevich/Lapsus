using Lapsus.Core.Correction;
using Lapsus.Core.Layout;

namespace Lapsus.Core.Tests.Support;

// Candidates over the bundled keyboard maps, named "{code}-id".
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
