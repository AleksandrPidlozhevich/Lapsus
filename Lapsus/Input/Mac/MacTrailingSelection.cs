namespace Lapsus.Input;

// Pure check shared with tests. Writing selected text while the caret is still a point inserts,
// and the characters that should have been replaced stay in front of the correction.
internal static class MacTrailingSelection
{
    public static bool Confirmed(
        nint location, nint length, nint expectedLocation, nint expectedLength, int? selectedTextLength)
    {
        if (expectedLength <= 0)
            return false;

        if (location != expectedLocation || length != expectedLength)
            return false;

        // Null means the field does not expose the selected string, so the range is all we have.
        // Zero means the caret did not move.
        return selectedTextLength is null || selectedTextLength.Value == expectedLength;
    }
}
