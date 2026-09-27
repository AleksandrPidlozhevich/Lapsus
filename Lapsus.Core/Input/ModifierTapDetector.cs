namespace Lapsus.Core.Input;

public sealed class ModifierTapDetector
{
    public const int GapMs = 300;

    public const int MaxHoldMs = 400;

    private TapModifier? _held;
    private long _heldSince;

    private bool _heldSpoiled;

    private bool _heldFollowsTap;

    private TapModifier? _lastTap;
    private long _lastTapAt;

    public TapModifier? Note(TapModifier modifier, bool isDown, long timestampMs)
    {
        if (isDown)
        {
            if (_held == modifier)
                return null;

            if (_held is not null)
            {
                Reset();
                return null;
            }

            _held = modifier;
            _heldSince = timestampMs;
            _heldSpoiled = false;
            _heldFollowsTap = _lastTap == modifier && timestampMs - _lastTapAt <= GapMs;
            return null;
        }

        if (_held != modifier)
        {
            _lastTap = null;
            return null;
        }

        var spoiled = _heldSpoiled || timestampMs - _heldSince > MaxHoldMs;
        var second = _heldFollowsTap;
        _held = null;
        _heldSpoiled = false;
        _heldFollowsTap = false;

        if (spoiled)
        {
            _lastTap = null;
            return null;
        }

        if (second)
        {
            _lastTap = null;
            return modifier;
        }

        _lastTap = modifier;
        _lastTapAt = timestampMs;
        return null;
    }

    public void NoteOtherInput()
    {
        _heldSpoiled = true;
        _heldFollowsTap = false;
        _lastTap = null;
    }

    public void Reset()
    {
        _held = null;
        _heldSpoiled = false;
        _heldFollowsTap = false;
        _lastTap = null;
    }
}
