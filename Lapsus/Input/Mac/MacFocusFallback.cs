namespace Lapsus.Input;

// Pure choice shared with tests. A focused element that cannot be read is not the user leaving the
// app: the key window's app stands in, with no element identity, so the typed line is kept.
internal static class MacFocusFallback
{
    public static (int Pid, nint ElementHash) Resolve(int focusedPid, nint focusedHash, int frontmostPid)
    {
        if (focusedPid > 0)
            return (focusedPid, focusedHash);

        return frontmostPid > 0 ? (frontmostPid, 0) : (0, 0);
    }
}
