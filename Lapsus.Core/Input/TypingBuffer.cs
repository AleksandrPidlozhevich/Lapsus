using System.Text;

namespace Lapsus.Core.Input;

public sealed class TypingBuffer
{
    private readonly StringBuilder _segment = new();
    private long _lastChangeTicks = Environment.TickCount64;

    public string Segment => _segment.ToString();

    public int Length => _segment.Length;

    public string CurrentChunk
    {
        get
        {
            var start = _segment.Length;
            while (start > 0 && !char.IsWhiteSpace(_segment[start - 1]))
                start--;

            return _segment.ToString(start, _segment.Length - start);
        }
    }

    public bool EndsInsideChunk =>
        _segment.Length > 0 && !char.IsWhiteSpace(_segment[_segment.Length - 1]);

    public void Append(char c)
    {
        _lastChangeTicks = Environment.TickCount64;

        if (c is '\n' or '\r')
        {
            Reset();
            return;
        }

        _segment.Append(c);
    }

    public void Backspace()
    {
        _lastChangeTicks = Environment.TickCount64;
        if (_segment.Length > 0)
            _segment.Remove(_segment.Length - 1, 1);
    }

    public bool DropIfIdle(TimeSpan idle)
    {
        if (_segment.Length == 0 || Environment.TickCount64 - _lastChangeTicks < idle.TotalMilliseconds)
            return false;

        Reset();
        return true;
    }

    public bool TryReplaceTrailing(string expected, string replacement)
    {
        if (_segment.Length < expected.Length ||
            !_segment.ToString(_segment.Length - expected.Length, expected.Length)
                .Equals(expected, StringComparison.Ordinal))
            return false;

        _segment.Remove(_segment.Length - expected.Length, expected.Length);
        _segment.Append(replacement);
        _lastChangeTicks = Environment.TickCount64;
        return true;
    }

    public void Reset()
    {
        // Overwrite before drop: the segment may have held a password no API flagged.
        for (var i = 0; i < _segment.Length; i++)
            _segment[i] = '\0';

        _segment.Clear();
        _lastChangeTicks = Environment.TickCount64;
    }
}
