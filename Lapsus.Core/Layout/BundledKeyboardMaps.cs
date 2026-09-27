namespace Lapsus.Core.Layout;

public static class BundledKeyboardMaps
{
    private static readonly Dictionary<char, char> JcukenLetters = new()
    {
        ['a'] = 'ф', ['b'] = 'и', ['c'] = 'с', ['d'] = 'в', ['e'] = 'у', ['f'] = 'а', ['g'] = 'п',
        ['h'] = 'р', ['i'] = 'ш', ['j'] = 'о', ['k'] = 'л', ['l'] = 'д', ['m'] = 'ь', ['n'] = 'т',
        ['o'] = 'щ', ['p'] = 'з', ['q'] = 'й', ['r'] = 'к', ['s'] = 'ы', ['t'] = 'е', ['u'] = 'г',
        ['v'] = 'м', ['w'] = 'ц', ['x'] = 'ч', ['y'] = 'н', ['z'] = 'я'
    };

    private static readonly Dictionary<char, char> RussianOem = new()
    {
        [';'] = 'ж', ['\''] = 'э', [','] = 'б', ['.'] = 'ю',
        ['['] = 'х', [']'] = 'ъ', ['`'] = 'ё'
    };

    private static readonly Dictionary<char, char> UkrainianOem = new()
    {
        [';'] = 'ж', ['\''] = 'є', [','] = 'б', ['.'] = 'ю',
        ['['] = 'х', [']'] = 'ї', ['`'] = '\''
    };

    private static readonly Dictionary<char, char> BelarusianOem = new()
    {
        [';'] = 'ж', ['\''] = 'э', [','] = 'б', ['.'] = 'ю',
        ['['] = 'х', [']'] = '\'', ['`'] = 'ё'
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

    private static char[] BuildJcuken(JcukenVariant variant)
    {
        var letters = new Dictionary<char, char>(JcukenLetters);
        Dictionary<char, char> oem;

        switch (variant)
        {
            case JcukenVariant.UkrainianPc:
                letters['b'] = 'и';
                letters['s'] = 'і';
                oem = UkrainianOem;
                break;
            case JcukenVariant.UkrainianApple:
                letters['b'] = 'і';
                letters['s'] = 'и';
                oem = UkrainianOem;
                break;
            case JcukenVariant.Belarusian:
                letters['o'] = 'ў';
                letters['b'] = 'і';
                oem = BelarusianOem;
                break;
            default:
                oem = RussianOem;
                break;
        }

        foreach (var (key, localized) in oem)
            letters[key] = localized;

        var slots = RemapFromEn(letters);

        if (variant is JcukenVariant.UkrainianPc or JcukenVariant.UkrainianApple)
            slots[IsoKeySlot] = 'ґ';

        return slots;
    }

    private static char[] BuildBulgarianPhonetic()
    {
        var slots = BuildEn();
        SetLetters(slots, new Dictionary<char, char>
        {
            ['a'] = 'а', ['b'] = 'б', ['c'] = 'ц', ['d'] = 'д', ['e'] = 'е', ['f'] = 'ф', ['g'] = 'г',
            ['h'] = 'х', ['i'] = 'и', ['j'] = 'й', ['k'] = 'к', ['l'] = 'л', ['m'] = 'м', ['n'] = 'н',
            ['o'] = 'о', ['p'] = 'п', ['q'] = 'ч', ['r'] = 'р', ['s'] = 'с', ['t'] = 'т', ['u'] = 'у',
            ['v'] = 'в', ['w'] = 'ш', ['x'] = 'ж', ['y'] = 'ъ', ['z'] = 'з'
        });
        SetMappedChars(slots, new Dictionary<char, char>
        {
            ['`'] = 'ю',
            ['['] = 'я',
            [']'] = 'щ',
            ['\\'] = 'ь'
        });

        slots[IsoKeySlot] = 'ѝ';
        return slots;
    }

    private static char[] BuildBulgarianPhonetic2006()
    {
        var slots = RemapFromEn(new Dictionary<char, char>
        {
            ['a'] = 'а', ['b'] = 'б', ['c'] = 'ц', ['d'] = 'д', ['e'] = 'е', ['f'] = 'ф', ['g'] = 'г',
            ['h'] = 'х', ['i'] = 'и', ['j'] = 'й', ['k'] = 'к', ['l'] = 'л', ['m'] = 'м', ['n'] = 'н',
            ['o'] = 'о', ['p'] = 'п', ['q'] = 'я', ['r'] = 'р', ['s'] = 'с', ['t'] = 'т', ['u'] = 'у',
            ['v'] = 'ж', ['w'] = 'в', ['x'] = 'ь', ['y'] = 'ъ', ['z'] = 'з',
            ['`'] = 'ч', ['['] = 'ш', ['\\'] = 'ю', [']'] = 'щ'
        });

        slots[IsoKeySlot] = 'ю';
        return slots;
    }

    private static char[] BuildBulgarianBds()
    {
        var slots = RemapFromEn(new Dictionary<char, char>
        {
            ['a'] = 'ь', ['b'] = 'ф', ['c'] = 'ъ', ['d'] = 'а', ['e'] = 'е', ['f'] = 'о', ['g'] = 'ж',
            ['h'] = 'г', ['i'] = 'с', ['j'] = 'т', ['k'] = 'н', ['l'] = 'в', ['m'] = 'п', ['n'] = 'х',
            ['o'] = 'д', ['p'] = 'з', ['q'] = ',', ['r'] = 'и', ['s'] = 'я', ['t'] = 'ш', ['u'] = 'к',
            ['v'] = 'э', ['w'] = 'у', ['x'] = 'й', ['y'] = 'щ', ['z'] = 'ю',
            [';'] = 'м', [','] = 'р', ['.'] = 'л', ['/'] = 'б', ['['] = 'ц', ['\''] = 'ч',
            ['`'] = '(', ['='] = '.', ['\\'] = '„', [']'] = ';'
        });

        slots[IsoKeySlot] = 'ѝ';
        return slots;
    }

    private static char[] BuildMacedonian()
    {
        var slots = BuildEn();
        SetLetters(slots, new Dictionary<char, char>
        {
            ['a'] = 'а', ['b'] = 'б', ['c'] = 'ц', ['d'] = 'д', ['e'] = 'е', ['f'] = 'ф', ['g'] = 'г',
            ['h'] = 'х', ['i'] = 'и', ['j'] = 'ј', ['k'] = 'к', ['l'] = 'л', ['m'] = 'м', ['n'] = 'н',
            ['o'] = 'о', ['p'] = 'п', ['q'] = 'љ', ['r'] = 'р', ['s'] = 'с', ['t'] = 'т', ['u'] = 'у',
            ['v'] = 'в', ['w'] = 'њ', ['x'] = 'џ', ['y'] = 'ѕ', ['z'] = 'з'
        });
        SetMappedChars(slots, new Dictionary<char, char>
        {
            ['['] = 'ш',
            [']'] = 'ѓ',
            [';'] = 'ч',
            ['\''] = 'ќ',
            ['\\'] = 'ж'
        });
        return slots;
    }

    private static char[] BuildGreek()
    {
        var slots = BuildEn();
        SetLetters(slots, new Dictionary<char, char>
        {
            ['a'] = 'α', ['b'] = 'β', ['c'] = 'ψ', ['d'] = 'δ', ['e'] = 'ε', ['f'] = 'φ', ['g'] = 'γ',
            ['h'] = 'η', ['i'] = 'ι', ['j'] = 'ξ', ['k'] = 'κ', ['l'] = 'λ', ['m'] = 'μ', ['n'] = 'ν',
            ['o'] = 'ο', ['p'] = 'π', ['q'] = ';', ['r'] = 'ρ', ['s'] = 'σ', ['t'] = 'τ', ['u'] = 'θ',
            ['v'] = 'ω', ['w'] = 'ς', ['x'] = 'χ', ['y'] = 'υ', ['z'] = 'ζ'
        });
        return slots;
    }

    private static char[] BuildHebrew()
    {
        var slots = RemapFromEn(new Dictionary<char, char>
        {
            ['q'] = '/', ['w'] = '\'', ['e'] = 'ק', ['r'] = 'ר', ['t'] = 'א', ['y'] = 'ט',
            ['u'] = 'ו', ['i'] = 'ן', ['o'] = 'ם', ['p'] = 'פ', ['['] = ']', [']'] = '[',
            ['a'] = 'ש', ['s'] = 'ד', ['d'] = 'ג', ['f'] = 'כ', ['g'] = 'ע', ['h'] = 'י',
            ['j'] = 'ח', ['k'] = 'ל', ['l'] = 'ך', [';'] = 'ף', ['\''] = ',',
            ['z'] = 'ז', ['x'] = 'ס', ['c'] = 'ב', ['v'] = 'ה', ['b'] = 'נ', ['n'] = 'מ',
            ['m'] = 'צ', [','] = 'ת', ['.'] = 'ץ', ['/'] = '.',
            ['`'] = ';'
        });

        slots[IsoKeySlot] = '\\';
        return slots;
    }

    private static char[] BuildArabic()
    {
        return RemapFromEn(new Dictionary<char, char>
        {
            ['q'] = 'ض', ['w'] = 'ص', ['e'] = 'ث', ['r'] = 'ق', ['t'] = 'ف', ['y'] = 'غ',
            ['u'] = 'ع', ['i'] = 'ه', ['o'] = 'خ', ['p'] = 'ح', ['['] = 'ج', [']'] = 'د',
            ['a'] = 'ش', ['s'] = 'س', ['d'] = 'ي', ['f'] = 'ب', ['g'] = 'ل', ['h'] = 'ا',
            ['j'] = 'ت', ['k'] = 'ن', ['l'] = 'م', [';'] = 'ك', ['\''] = 'ط',
            ['z'] = 'ئ', ['x'] = 'ء', ['c'] = 'ؤ', ['v'] = 'ر', ['b'] = '\0', ['n'] = 'ى',
            ['m'] = 'ة', [','] = 'و', ['.'] = 'ز', ['/'] = 'ظ',
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
            ['q'] = 'ქ', ['w'] = 'წ', ['e'] = 'ე', ['r'] = 'რ', ['t'] = 'ტ', ['y'] = 'ყ',
            ['u'] = 'უ', ['i'] = 'ი', ['o'] = 'ო', ['p'] = 'პ',
            ['a'] = 'ა', ['s'] = 'ს', ['d'] = 'დ', ['f'] = 'ფ', ['g'] = 'გ', ['h'] = 'ჰ',
            ['j'] = 'ჯ', ['k'] = 'კ', ['l'] = 'ლ',
            ['z'] = 'ზ', ['x'] = 'ხ', ['c'] = 'ც', ['v'] = 'ვ', ['b'] = 'ბ', ['n'] = 'ნ',
            ['m'] = 'მ',
            ['`'] = '„', ['\\'] = '~'
        });

        slots[IsoKeySlot] = '\\';
        return slots;
    }

    private static char[] BuildGeorgianShifted()
    {
        return ShiftedLetters(
            ('t', 'თ'), ('s', 'შ'), ('r', 'ღ'), ('c', 'ჩ'), ('j', 'ჟ'), ('z', 'ძ'), ('w', 'ჭ'));
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
