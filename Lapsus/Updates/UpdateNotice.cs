using Lapsus.Localization;
using Lapsus.Settings;
using System;

namespace Lapsus.Updates;

internal sealed class UpdateNotice(AppSettings settings, SettingsStore store)
{
    public bool TryClaimAnnouncement(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        if (string.Equals(settings.AnnouncedUpdateVersion, version, StringComparison.OrdinalIgnoreCase))
            return false;

        settings.AnnouncedUpdateVersion = version;
        store.Save(settings);
        return true;
    }

    public static string MenuText(Localizer loc, string? version)
        => string.IsNullOrWhiteSpace(version) ? loc["Tray_UpdateAvailable"] : loc.Format("Tray_InstallUpdate", version);

    public static string TooltipText(Localizer loc, string? version)
        => string.IsNullOrWhiteSpace(version) ? loc["Tray_UpdateAvailable"] : loc.Format("Tray_TooltipUpdateReady", version);

    public static string AnnouncementTitle(Localizer loc, string version)
        => loc.Format("Notify_UpdateReadyTitle", version);

    public static string AnnouncementBody(Localizer loc)
        => loc["Notify_UpdateReadyBody"];
}
