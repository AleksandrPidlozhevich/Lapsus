namespace Lapsus.Core.Layout;

internal static class LayoutOemKeys
{
    private static readonly HashSet<char> Keys = Build();

    public static bool IsLatinLayoutOem(char c)
    {
        return Keys.Contains(c);
    }

    private static HashSet<char> Build()
    {
        KeyboardMap[] maps =
        [
            BundledKeyboardMaps.Ru, BundledKeyboardMaps.Uk, BundledKeyboardMaps.UkApple,
            BundledKeyboardMaps.Be, BundledKeyboardMaps.Bg, BundledKeyboardMaps.BgPhonetic,
            BundledKeyboardMaps.BgBds, BundledKeyboardMaps.Mk,
            BundledKeyboardMaps.El, BundledKeyboardMaps.He, BundledKeyboardMaps.Ar,
            BundledKeyboardMaps.Ka
        ];

        var keys = new HashSet<char>();
        var us = BundledKeyboardMaps.En;

        for (var slot = 0; slot < us.SlotCount; slot++)
        {
            var key = us.CharAtSlot(slot);
            if (key == '\0' || char.IsLetterOrDigit(key))
                continue;

            foreach (var map in maps)
                if (Scripts.Of(map.CharAtSlot(slot)) is not null || map.IsDeadKey(slot, KeyShift.None))
                {
                    keys.Add(key);
                    break;
                }

            var shiftedKey = us.DeclaredShiftedChar(slot);
            if (shiftedKey != '\0')
                foreach (var map in maps)
                    if (map.IsDeadKey(slot, KeyShift.Layer))
                    {
                        keys.Add(shiftedKey);
                        break;
                    }
        }

        return keys;
    }
}
