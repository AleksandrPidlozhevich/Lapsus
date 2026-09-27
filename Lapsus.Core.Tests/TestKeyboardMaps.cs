using Lapsus.Core.Layout;

namespace Lapsus.Core.Tests;

// Slot order must match BundledKeyboardMaps.CreateEnSlots.
internal static class TestKeyboardMaps
{
    public static KeyboardMap GermanQwertz { get; } = new(UsSlotsWith(new Dictionary<char, char>
    {
        ['y'] = 'z', ['z'] = 'y',
        [';'] = 'ö', ['-'] = 'ß', ['['] = 'ü', ['\''] = 'ä', ['/'] = '-'
    }));

    public static KeyboardMap TurkishQ { get; } = new(UsSlotsWith(new Dictionary<char, char>
    {
        ['i'] = 'ı',
        [';'] = 'ş', ['['] = 'ğ', [']'] = 'ü', ['\''] = 'i', [','] = 'ö', ['.'] = 'ç', ['/'] = '.'
    }));

    private static char[] UsSlotsWith(Dictionary<char, char> overlay)
    {
        var slots = BundledKeyboardMaps.CreateEnSlots();
        for (var i = 0; i < slots.Length; i++)
            if (slots[i] != '\0' && overlay.TryGetValue(slots[i], out var mapped))
                slots[i] = mapped;

        return slots;
    }
}
