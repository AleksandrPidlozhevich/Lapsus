using Lapsus.Localization;
using Lapsus.Settings;
using System;

namespace Lapsus.Updates;

// What the tray says about an update the silent check has downloaded. The menu item, tooltip and icon dot
// stay until the update is installed; the pop-up notification is claimed once per version and remembered
// in settings, so a second check or a later start does not repeat it.
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
