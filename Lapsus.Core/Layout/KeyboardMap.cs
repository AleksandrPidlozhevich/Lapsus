namespace Lapsus.Core.Layout;

public enum KeyShift
{
    None,

    Case,

    Layer
}

public sealed class KeyboardMap
{
    private readonly Dictionary<char, int> _charToSlot;
    private readonly char[] _slotToChar;

    private readonly char[]? _slotToShifted;

    private readonly Dictionary<char, int>? _shiftedToSlot;

    private readonly Dictionary<(int Slot, bool Shifted, char Base), char>? _compose;

    private readonly Dictionary<char, (int Slot, bool Shifted, char Base)>? _decompose;

    private readonly HashSet<(int Slot, bool Shifted)>? _deadKeys;

    private readonly Dictionary<(int Slot, bool Shifted), string>? _ligatures;

    public KeyboardMap(
        IReadOnlyList<char> slotChars, IReadOnlyList<char>? shiftedSlotChars = null,
        IReadOnlyList<DeadKey>? deadKeys = null, IReadOnlyList<Ligature>? ligatures = null)
    {
        (_compose, _decompose, _deadKeys) = BuildCompositions(slotChars, deadKeys);

        if (ligatures is { Count: > 0 })
        {
            _ligatures = new Dictionary<(int, bool), string>(ligatures.Count);
            foreach (var ligature in ligatures)
                if (!string.IsNullOrEmpty(ligature.Text))
                    _ligatures[(ligature.Slot, ligature.Shifted)] = ligature.Text;
        }

        _slotToChar = new char[slotChars.Count];
        _charToSlot = new Dictionary<char, int>(slotChars.Count);

        for (var slot = 0; slot < slotChars.Count; slot++)
        {
            var c = slotChars[slot];
            _slotToChar[slot] = c;
            if (c != '\0' && !_charToSlot.ContainsKey(c))
                _charToSlot[c] = slot;
        }

        if (shiftedSlotChars is null)
            return;

        _slotToShifted = new char[_slotToChar.Length];
        _shiftedToSlot = new Dictionary<char, int>();

        for (var slot = 0; slot < _slotToShifted.Length && slot < shiftedSlotChars.Count; slot++)
        {
            var shifted = shiftedSlotChars[slot];
            if (shifted == '\0')
                continue;

            _slotToShifted[slot] = shifted;

            if (!_charToSlot.ContainsKey(char.ToLowerInvariant(shifted)) && !_shiftedToSlot.ContainsKey(shifted))
                _shiftedToSlot[shifted] = slot;
        }
    }

    public static char[] ShiftedSymbolsOnly(IReadOnlyList<char> slotChars, IReadOnlyList<char> shiftedSlotChars)
    {
        var symbols = new char[slotChars.Count];
        for (var slot = 0; slot < symbols.Length && slot < shiftedSlotChars.Count; slot++)
            if (slotChars[slot] != '\0' && !char.IsLetter(slotChars[slot]) && !char.IsLetter(shiftedSlotChars[slot]))
                symbols[slot] = shiftedSlotChars[slot];

        return symbols;
    }

    private static (Dictionary<(int, bool, char), char>?, Dictionary<char, (int, bool, char)>?, HashSet<(int, bool)>?)
        BuildCompositions(IReadOnlyList<char> slotChars, IReadOnlyList<DeadKey>? deadKeys)
    {
        if (deadKeys is null || deadKeys.Count == 0)
            return (null, null, null);

        var compose = new Dictionary<(int, bool, char), char>();
        var decompose = new Dictionary<char, (int, bool, char)>();
        var keys = new HashSet<(int, bool)>();

        foreach (var dead in deadKeys)
        {
            if (DeadKeys.CombiningMarks(dead.Accent) is not { } marks)
                continue;

            keys.Add((dead.Slot, dead.Shifted));
            foreach (var letter in slotChars)
            {
                if (!char.IsLetter(letter) || DeadKeys.Compose(letter, marks) is not { } composed)
                    continue;

                compose[(dead.Slot, dead.Shifted, letter)] = composed;
                decompose.TryAdd(composed, (dead.Slot, dead.Shifted, letter));
            }
        }

        return keys.Count == 0 ? (null, null, null) : (compose, decompose, keys);
    }

    public bool HasDeadKeys => _deadKeys is not null;

    public bool HasLigatures => _ligatures is not null;

    public string? LigatureAtSlot(int slot, KeyShift shift)
    {
        return _ligatures is not null && _ligatures.TryGetValue((slot, shift != KeyShift.None), out var text)
            ? text
            : null;
    }

    // The key whose ligature starts at text[at], if one does; the longest wins.
    public bool TryMatchLigature(string text, int at, out int slot, out KeyShift shift, out int length)
    {
        slot = 0;
        shift = KeyShift.None;
        length = 0;
        if (_ligatures is null)
            return false;

        foreach (var ((keySlot, shifted), ligature) in _ligatures)
            if (ligature.Length > length && string.CompareOrdinal(text, at, ligature, 0, ligature.Length) == 0)
            {
                slot = keySlot;
                shift = shifted ? KeyShift.Case : KeyShift.None;
                length = ligature.Length;
            }

        return length > 0;
    }

    public bool IsDeadKey(int slot, KeyShift shift)
    {
        return _deadKeys is not null && _deadKeys.Contains((slot, shift != KeyShift.None));
    }

    public bool TryCompose(int deadSlot, KeyShift deadShift, char letter, out char composed)
    {
        composed = '\0';
        if (_compose is null)
            return false;

        var lower = char.ToLowerInvariant(letter);
        if (!_compose.TryGetValue((deadSlot, deadShift != KeyShift.None, lower), out var made))
            return false;

        composed = letter == lower ? made : char.ToUpperInvariant(made);
        return true;
    }

    public bool TryDecompose(char ch, out int deadSlot, out KeyShift deadShift, out char letter)
    {
        deadSlot = 0;
        deadShift = KeyShift.None;
        letter = '\0';
        if (_decompose is null)
            return false;

        var lower = char.ToLowerInvariant(ch);
        if (!_decompose.TryGetValue(lower, out var parts))
            return false;

        deadSlot = parts.Item1;
        deadShift = parts.Item2 ? KeyShift.Layer : KeyShift.None;
        letter = ch == lower ? parts.Item3 : char.ToUpperInvariant(parts.Item3);
        return true;
    }

    public int SlotCount => _slotToChar.Length;

    public bool TryGetSlot(char lowerChar, out int slot)
    {
        return _charToSlot.TryGetValue(lowerChar, out slot);
    }

    public bool TryGetKey(char ch, out int slot, out KeyShift shift)
    {
        var lower = char.ToLowerInvariant(ch);
        if (_charToSlot.TryGetValue(lower, out slot))
        {
            shift = ch == lower ? KeyShift.None : KeyShift.Case;
            return true;
        }

        if (_shiftedToSlot is not null && _shiftedToSlot.TryGetValue(ch, out slot))
        {
            shift = KeyShift.Layer;
            return true;
        }

        shift = KeyShift.None;
        return false;
    }

    public char DeclaredShiftedChar(int slot)
    {
        return slot >= 0 && _slotToShifted is not null && slot < _slotToShifted.Length
            ? _slotToShifted[slot]
            : '\0';
    }

    public char CharAtSlot(int slot)
    {
        return slot >= 0 && slot < _slotToChar.Length ? _slotToChar[slot] : '\0';
    }

    public char CharAtSlot(int slot, KeyShift shift)
    {
        if (shift == KeyShift.None)
            return CharAtSlot(slot);

        var declared = DeclaredShiftedChar(slot);
        if (declared != '\0')
            return declared;

        var baseChar = CharAtSlot(slot);
        var upper = char.ToUpperInvariant(baseChar);
        if (shift == KeyShift.Case)
            return upper;

        // Undeclared Layer only if the letter has case — else "!" becomes "1".
        return upper != baseChar ? upper : '\0';
    }
}
