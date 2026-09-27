using System.Runtime.Versioning;
using System.Text;

namespace Lapsus.Input;

[SupportedOSPlatform("macos")]
internal static class MacProcessNames
{
    public static string? Read(int pid)
    {
        var buffer = new StringBuilder(256);
        return Read(pid, buffer);
    }

    public static string? Read(int pid, StringBuilder buffer)
    {
        if (pid <= 0)
            return null;

        buffer.Clear();
        var written = MacOSNativeMethods.proc_name(pid, buffer, (uint)buffer.Capacity);
        return written > 0 && buffer.Length > 0 ? buffer.ToString() : null;
    }
}
