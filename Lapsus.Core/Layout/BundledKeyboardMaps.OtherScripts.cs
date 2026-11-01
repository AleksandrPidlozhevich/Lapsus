namespace Lapsus.Core.Layout;

// Greek, Hebrew, Arabic and Georgian layouts.
public static partial class BundledKeyboardMaps
{
    private static char[] BuildGreek()
    {
        var slots = BuildEn();
        SetLetters(slots, new Dictionary<char, char>
        {
            ['a'] = 'α',
            ['b'] = 'β',
            ['c'] = 'ψ',
            ['d'] = 'δ',
            ['e'] = 'ε',
            ['f'] = 'φ',
            ['g'] = 'γ',
            ['h'] = 'η',
            ['i'] = 'ι',
            ['j'] = 'ξ',
            ['k'] = 'κ',
            ['l'] = 'λ',
            ['m'] = 'μ',
            ['n'] = 'ν',
            ['o'] = 'ο',
            ['p'] = 'π',
            ['q'] = ';',
            ['r'] = 'ρ',
            ['s'] = 'σ',
            ['t'] = 'τ',
            ['u'] = 'θ',
            ['v'] = 'ω',
            ['w'] = 'ς',
            ['x'] = 'χ',
            ['y'] = 'υ',
            ['z'] = 'ζ'
        });
        return slots;
    }

    private static char[] BuildHebrew()
    {
        var slots = RemapFromEn(new Dictionary<char, char>
        {
            ['q'] = '/',
            ['w'] = '\'',
            ['e'] = 'ק',
            ['r'] = 'ר',
            ['t'] = 'א',
            ['y'] = 'ט',
            ['u'] = 'ו',
            ['i'] = 'ן',
            ['o'] = 'ם',
            ['p'] = 'פ',
            ['['] = ']',
            [']'] = '[',
            ['a'] = 'ש',
            ['s'] = 'ד',
            ['d'] = 'ג',
            ['f'] = 'כ',
            ['g'] = 'ע',
            ['h'] = 'י',
            ['j'] = 'ח',
            ['k'] = 'ל',
            ['l'] = 'ך',
            [';'] = 'ף',
            ['\''] = ',',
            ['z'] = 'ז',
            ['x'] = 'ס',
            ['c'] = 'ב',
            ['v'] = 'ה',
            ['b'] = 'נ',
            ['n'] = 'מ',
            ['m'] = 'צ',
            [','] = 'ת',
            ['.'] = 'ץ',
            ['/'] = '.',
            ['`'] = ';'
        });

        slots[IsoKeySlot] = '\\';
        return slots;
    }

    private static char[] BuildArabic()
    {
        return RemapFromEn(new Dictionary<char, char>
        {
            ['q'] = 'ض',
            ['w'] = 'ص',
            ['e'] = 'ث',
            ['r'] = 'ق',
            ['t'] = 'ف',
            ['y'] = 'غ',
            ['u'] = 'ع',
            ['i'] = 'ه',
            ['o'] = 'خ',
            ['p'] = 'ح',
            ['['] = 'ج',
            [']'] = 'د',
            ['a'] = 'ش',
            ['s'] = 'س',
            ['d'] = 'ي',
            ['f'] = 'ب',
            ['g'] = 'ل',
            ['h'] = 'ا',
            ['j'] = 'ت',
            ['k'] = 'ن',
            ['l'] = 'م',
            [';'] = 'ك',
            ['\''] = 'ط',
            ['z'] = 'ئ',
            ['x'] = 'ء',
            ['c'] = 'ؤ',
            ['v'] = 'ر',
            ['b'] = '\0',
            ['n'] = 'ى',
            ['m'] = 'ة',
            [','] = 'و',
            ['.'] = 'ز',
            ['/'] = 'ظ',
            ['`'] = 'ذ'
        });
    }

    private static char[] BuildArabicShifted()
    {
        var shifted = ShiftedLetters(('y', 'إ'), ('h', 'أ'), ('n', 'آ'));
        var us = BuildEn();
        foreach (var (key, mark) in new[] { ('k', '،'), ('p', '؛'), ('/', '؟') })
            shifted[Array.IndexOf(us, key)] = mark;

        return shifted;
    }

    private static char[] BuildGeorgian()
    {
        var slots = RemapFromEn(new Dictionary<char, char>
        {
            ['q'] = 'ქ',
            ['w'] = 'წ',
            ['e'] = 'ე',
            ['r'] = 'რ',
            ['t'] = 'ტ',
            ['y'] = 'ყ',
            ['u'] = 'უ',
            ['i'] = 'ი',
            ['o'] = 'ო',
            ['p'] = 'პ',
            ['a'] = 'ა',
            ['s'] = 'ს',
            ['d'] = 'დ',
            ['f'] = 'ფ',
            ['g'] = 'გ',
            ['h'] = 'ჰ',
            ['j'] = 'ჯ',
            ['k'] = 'კ',
            ['l'] = 'ლ',
            ['z'] = 'ზ',
            ['x'] = 'ხ',
            ['c'] = 'ც',
            ['v'] = 'ვ',
            ['b'] = 'ბ',
            ['n'] = 'ნ',
            ['m'] = 'მ',
            ['`'] = '„',
            ['\\'] = '~'
        });

        slots[IsoKeySlot] = '\\';
        return slots;
    }

    private static char[] BuildGeorgianShifted()
    {
        return ShiftedLetters(
            ('t', 'თ'), ('s', 'შ'), ('r', 'ღ'), ('c', 'ჩ'), ('j', 'ჟ'), ('z', 'ძ'), ('w', 'ჭ'));
    }
}
