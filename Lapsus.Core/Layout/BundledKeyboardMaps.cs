namespace Lapsus.Core.Layout;

public static partial class BundledKeyboardMaps
{
    // Every static field with an initializer stays in this file: across partial files their order is undefined,
    // and the maps below are built from the tables above.
    private static readonly Dictionary<char, char> JcukenLetters = new()
    {
        ['a'] = 'ф',
        ['b'] = 'и',
        ['c'] = 'с',
        ['d'] = 'в',
        ['e'] = 'у',
        ['f'] = 'а',
        ['g'] = 'п',
        ['h'] = 'р',
        ['i'] = 'ш',
        ['j'] = 'о',
        ['k'] = 'л',
        ['l'] = 'д',
        ['m'] = 'ь',
        ['n'] = 'т',
        ['o'] = 'щ',
        ['p'] = 'з',
        ['q'] = 'й',
        ['r'] = 'к',
        ['s'] = 'ы',
        ['t'] = 'е',
        ['u'] = 'г',
        ['v'] = 'м',
        ['w'] = 'ц',
        ['x'] = 'ч',
        ['y'] = 'н',
        ['z'] = 'я'
    };

    private static readonly Dictionary<char, char> RussianOem = new()
    {
        [';'] = 'ж',
        ['\''] = 'э',
        [','] = 'б',
        ['.'] = 'ю',
        ['['] = 'х',
        [']'] = 'ъ',
        ['`'] = 'ё'
    };

    private static readonly Dictionary<char, char> UkrainianOem = new()
    {
        [';'] = 'ж',
        ['\''] = 'є',
        [','] = 'б',
        ['.'] = 'ю',
        ['['] = 'х',
        [']'] = 'ї',
        ['`'] = '\''
    };

    private static readonly Dictionary<char, char> BelarusianOem = new()
    {
        [';'] = 'ж',
        ['\''] = 'э',
        [','] = 'б',
        ['.'] = 'ю',
        ['['] = 'х',
        [']'] = '\'',
        ['`'] = 'ё'
    };

    public static readonly KeyboardMap En = new(BuildEn(), KeyboardMap.ShiftedSymbolsOnly(BuildEn(), BuildEnShifted()));
    public static readonly KeyboardMap Ru = new(BuildJcuken(JcukenVariant.Russian));

    public static readonly KeyboardMap Uk = new(BuildJcuken(JcukenVariant.UkrainianPc));

    public static readonly KeyboardMap UkApple = new(BuildJcuken(JcukenVariant.UkrainianApple));

    public static readonly KeyboardMap Be = new(BuildJcuken(JcukenVariant.Belarusian));

    public static readonly KeyboardMap Bg = new(BuildBulgarianPhonetic());

    public static readonly KeyboardMap BgPhonetic = new(BuildBulgarianPhonetic2006());

    public static readonly KeyboardMap BgBds = new(BuildBulgarianBds());

    public static readonly KeyboardMap Mk = new(BuildMacedonian());

    public static readonly KeyboardMap El = new(BuildGreek(), null,
    [
        new DeadKey(OemStart, false, '΄'),
        new DeadKey(OemStart, true, '¨'),
        new DeadKey('w' - 'a', true, '΅')
    ]);

    public static readonly KeyboardMap He = new(BuildHebrew(), ShiftedSymbols(('\'', '"')));

    public static readonly KeyboardMap Ar = new(BuildArabic(), BuildArabicShifted(), null,
    [
        new Ligature('b' - 'a', false, "لا"),
        new Ligature('b' - 'a', true, "لآ"),
        new Ligature('g' - 'a', true, "لأ"),
        new Ligature('t' - 'a', true, "لإ")
    ]);

    public static readonly KeyboardMap Ka = new(BuildGeorgian(), BuildGeorgianShifted());

    public static char[] CreateRuSlots()
    {
        return BuildJcuken(JcukenVariant.Russian);
    }

    public static char[] CreateUkSlots()
    {
        return BuildJcuken(JcukenVariant.UkrainianPc);
    }

    public static char[] CreateUkAppleSlots()
    {
        return BuildJcuken(JcukenVariant.UkrainianApple);
    }

    public static char[] CreateBeSlots()
    {
        return BuildJcuken(JcukenVariant.Belarusian);
    }

    public static char[] CreateBgSlots()
    {
        return BuildBulgarianPhonetic();
    }

    public static char[] CreateBgPhoneticSlots()
    {
        return BuildBulgarianPhonetic2006();
    }

    public static char[] CreateBgBdsSlots()
    {
        return BuildBulgarianBds();
    }

    public static char[] CreateMkSlots()
    {
        return BuildMacedonian();
    }

    public static char[] CreateElSlots()
    {
        return BuildGreek();
    }

    public static char[] CreateHeSlots()
    {
        return BuildHebrew();
    }

    public static char[] CreateArSlots()
    {
        return BuildArabic();
    }

    public static char[] CreateKaSlots()
    {
        return BuildGeorgian();
    }

    public static char[] CreateKaShiftedSlots()
    {
        return BuildGeorgianShifted();
    }

    public static char[] CreateEnSlots()
    {
        return BuildEn();
    }

    private enum JcukenVariant
    {
        Russian,
        UkrainianPc,
        UkrainianApple,
        Belarusian
    }

    private const int LetterCount = 26;
    private const int DigitCount = 10;

    // OEM keys start here; order must match both backends' enumeration.
    private const int OemStart = LetterCount + DigitCount;

    private const string EnOemChars = ";=,-./`[\\]'";

    private const string EnShiftedDigits = ")!@#$%^&*(";
    private const string EnShiftedOemChars = ":+<_>?~{|}\"";

    private const int IsoKeySlot = OemStart + 11;

    private const int SlotCount = IsoKeySlot + 1;

    private static char[] BuildEn()
    {
        var slots = new char[SlotCount];
        for (var i = 0; i < LetterCount; i++)
            slots[i] = (char)('a' + i);
        for (var i = 0; i < DigitCount; i++)
            slots[LetterCount + i] = (char)('0' + i);
        for (var i = 0; i < EnOemChars.Length; i++)
            slots[OemStart + i] = EnOemChars[i];
        return slots;
    }

    private static char[] BuildEnShifted()
    {
        var slots = new char[SlotCount];
        for (var i = 0; i < LetterCount; i++)
            slots[i] = (char)('A' + i);
        for (var i = 0; i < DigitCount; i++)
            slots[LetterCount + i] = EnShiftedDigits[i];
        for (var i = 0; i < EnShiftedOemChars.Length; i++)
            slots[OemStart + i] = EnShiftedOemChars[i];
        return slots;
    }

    private static char[] ShiftedSymbols(params (char Key, char Symbol)[] symbols)
    {
        var us = BuildEn();
        var shifted = new char[SlotCount];
        foreach (var (key, symbol) in symbols)
            shifted[Array.IndexOf(us, key)] = symbol;

        return shifted;
    }

    private static char[] ShiftedLetters(params (char Key, char Letter)[] letters)
    {
        var shifted = new char[SlotCount];
        foreach (var (key, letter) in letters)
            shifted[key - 'a'] = letter;

        return shifted;
    }

    // One-pass US-key remap so letter/punctuation swaps cannot collide.
    private static char[] RemapFromEn(Dictionary<char, char> mapping)
    {
        var slots = BuildEn();
        for (var i = 0; i < slots.Length; i++)
            if (slots[i] != '\0' && mapping.TryGetValue(slots[i], out var mapped))
                slots[i] = mapped;

        return slots;
    }

    private static void SetLetters(char[] slots, Dictionary<char, char> mapping)
    {
        foreach (var (en, localized) in mapping)
            slots[en - 'a'] = localized;
    }

    private static void SetMappedChars(char[] slots, Dictionary<char, char> mapping)
    {
        foreach (var (key, localized) in mapping)
            for (var i = 0; i < slots.Length; i++)
                if (slots[i] == key)
                    slots[i] = localized;
    }
}
