namespace Lapsus.Core.Layout;

public static partial class BundledKeyboardMaps
{
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
            ['a'] = 'а',
            ['b'] = 'б',
            ['c'] = 'ц',
            ['d'] = 'д',
            ['e'] = 'е',
            ['f'] = 'ф',
            ['g'] = 'г',
            ['h'] = 'х',
            ['i'] = 'и',
            ['j'] = 'й',
            ['k'] = 'к',
            ['l'] = 'л',
            ['m'] = 'м',
            ['n'] = 'н',
            ['o'] = 'о',
            ['p'] = 'п',
            ['q'] = 'ч',
            ['r'] = 'р',
            ['s'] = 'с',
            ['t'] = 'т',
            ['u'] = 'у',
            ['v'] = 'в',
            ['w'] = 'ш',
            ['x'] = 'ж',
            ['y'] = 'ъ',
            ['z'] = 'з'
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
            ['a'] = 'а',
            ['b'] = 'б',
            ['c'] = 'ц',
            ['d'] = 'д',
            ['e'] = 'е',
            ['f'] = 'ф',
            ['g'] = 'г',
            ['h'] = 'х',
            ['i'] = 'и',
            ['j'] = 'й',
            ['k'] = 'к',
            ['l'] = 'л',
            ['m'] = 'м',
            ['n'] = 'н',
            ['o'] = 'о',
            ['p'] = 'п',
            ['q'] = 'я',
            ['r'] = 'р',
            ['s'] = 'с',
            ['t'] = 'т',
            ['u'] = 'у',
            ['v'] = 'ж',
            ['w'] = 'в',
            ['x'] = 'ь',
            ['y'] = 'ъ',
            ['z'] = 'з',
            ['`'] = 'ч',
            ['['] = 'ш',
            ['\\'] = 'ю',
            [']'] = 'щ'
        });

        slots[IsoKeySlot] = 'ю';
        return slots;
    }

    private static char[] BuildBulgarianBds()
    {
        var slots = RemapFromEn(new Dictionary<char, char>
        {
            ['a'] = 'ь',
            ['b'] = 'ф',
            ['c'] = 'ъ',
            ['d'] = 'а',
            ['e'] = 'е',
            ['f'] = 'о',
            ['g'] = 'ж',
            ['h'] = 'г',
            ['i'] = 'с',
            ['j'] = 'т',
            ['k'] = 'н',
            ['l'] = 'в',
            ['m'] = 'п',
            ['n'] = 'х',
            ['o'] = 'д',
            ['p'] = 'з',
            ['q'] = ',',
            ['r'] = 'и',
            ['s'] = 'я',
            ['t'] = 'ш',
            ['u'] = 'к',
            ['v'] = 'э',
            ['w'] = 'у',
            ['x'] = 'й',
            ['y'] = 'щ',
            ['z'] = 'ю',
            [';'] = 'м',
            [','] = 'р',
            ['.'] = 'л',
            ['/'] = 'б',
            ['['] = 'ц',
            ['\''] = 'ч',
            ['`'] = '(',
            ['='] = '.',
            ['\\'] = '„',
            [']'] = ';'
        });

        slots[IsoKeySlot] = 'ѝ';
        return slots;
    }

    private static char[] BuildMacedonian()
    {
        var slots = BuildEn();
        SetLetters(slots, new Dictionary<char, char>
        {
            ['a'] = 'а',
            ['b'] = 'б',
            ['c'] = 'ц',
            ['d'] = 'д',
            ['e'] = 'е',
            ['f'] = 'ф',
            ['g'] = 'г',
            ['h'] = 'х',
            ['i'] = 'и',
            ['j'] = 'ј',
            ['k'] = 'к',
            ['l'] = 'л',
            ['m'] = 'м',
            ['n'] = 'н',
            ['o'] = 'о',
            ['p'] = 'п',
            ['q'] = 'љ',
            ['r'] = 'р',
            ['s'] = 'с',
            ['t'] = 'т',
            ['u'] = 'у',
            ['v'] = 'в',
            ['w'] = 'њ',
            ['x'] = 'џ',
            ['y'] = 'ѕ',
            ['z'] = 'з'
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
}
