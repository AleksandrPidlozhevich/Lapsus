using System;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Lapsus.Startup;

internal static class StartupRegistrationFiles
{
    public const string LinuxDesktopFileName = "lapsus.desktop";
    public const string MacOsLabel = "com.lapsus.app";
    public const string MacOsPlistFileName = "com.lapsus.app.plist";

    public static string BuildLinuxDesktopContent(string exe)
    {
        return new StringBuilder()
            .AppendLine("[Desktop Entry]")
            .AppendLine("Type=Application")
            .AppendLine("Name=Lapsus")
            .AppendLine("Comment=Fix keyboard layout and typos")
            .AppendLine($"Exec={FormatDesktopExec(exe)}")
            .AppendLine("Terminal=false")
            .AppendLine("StartupNotify=false")
            .AppendLine("Hidden=false")
            .AppendLine("X-GNOME-Autostart-enabled=true")
            .ToString();
    }

    public static bool IsLinuxAutostartEnabled(string desktopFilePath)
    {
        if (!File.Exists(desktopFilePath))
            return false;

        foreach (var line in File.ReadLines(desktopFilePath))
        {
            if (IsDesktopEntryFlag(line, "Hidden", true))
                return false;
            if (IsDesktopEntryFlag(line, "X-GNOME-Autostart-enabled", false))
                return false;
        }

        return true;
    }

    public static string FormatDesktopExec(string exe)
    {
        return exe.Contains(' ') ? $"\"{exe}\"" : exe;
    }

    // Rewrites an existing login item whose stored executable differs from the running one.
    // Returns true only when it wrote the file.
    public static bool RefreshMacOsPlist(string path, string exe)
    {
        if (!File.Exists(path) || string.IsNullOrWhiteSpace(exe))
            return false;

        var wanted = BuildMacOsPlistContent(exe);
        if (File.ReadAllText(path) == wanted)
            return false;

        File.WriteAllText(path, wanted, new UTF8Encoding(true));
        return true;
    }

    public static string BuildMacOsPlistContent(string exe)
    {
        var plist = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("plist",
                new XAttribute("version", "1.0"),
                new XElement("dict",
                    new XElement("key", "Label"),
                    new XElement("string", MacOsLabel),
                    new XElement("key", "ProgramArguments"),
                    new XElement("array",
                        new XElement("string", exe)),
                    new XElement("key", "RunAtLoad"),
                    new XElement("true"),
                    new XElement("key", "ProcessType"),
                    new XElement("string", "Interactive"))));

        var settings = new UTF8Encoding(true);
        using var writer = new StringWriter();
        writer.WriteLine(
            @"<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">");
        plist.Save(writer, SaveOptions.DisableFormatting);
        return writer.ToString();
    }

    private static bool IsDesktopEntryFlag(string line, string key, bool disabledValue)
    {
        var trimmed = line.Trim();
        var prefix = key + "=";
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var value = trimmed[prefix.Length..].Trim();
        return bool.TryParse(value, out var parsed) && parsed == disabledValue;
    }
}
