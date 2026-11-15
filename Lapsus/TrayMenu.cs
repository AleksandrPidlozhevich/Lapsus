using Avalonia.Controls;
using Avalonia.Threading;
using Lapsus.Input.Dictation;
using Lapsus.Localization;
using Lapsus.ViewModels;
using System;

namespace Lapsus;

// The tray icon's menu: Settings, a break, the recent dictations, Exit. The update entry is not here: it is
// added to the front when an update is ready, by whoever owns the icon.
internal sealed class TrayMenu
{
    private readonly Localizer _loc = Localizer.Instance;
    private readonly string _exitKey;

    private readonly NativeMenuItem _settings;
    private readonly NativeMenuItem _pause;
    private readonly NativeMenuItem _pause15;
    private readonly NativeMenuItem _pause60;
    private readonly NativeMenuItem _pauseOpen;
    private readonly NativeMenuItem _resume;
    private readonly NativeMenuItem _history;
    private readonly NativeMenuItem _exit;

    public NativeMenu Menu { get; }

    public TrayMenu(
        Action showSettings, Action exit, PauseViewModel? pausing,
        DictationHistory? history, Action<string> copyDictation)
    {
        // Apple requires "Quit <App>" on macOS; Windows keeps "Exit".
        _exitKey = OperatingSystem.IsMacOS() ? "Tray_Quit" : "Tray_Exit";

        _settings = new NativeMenuItem(_loc["Tray_Settings"]);
        _settings.Click += (_, _) => showSettings();

        _exit = new NativeMenuItem(_loc[_exitKey]);
        _exit.Click += (_, _) => exit();

        // A break: for a while, or until asked back.
        _pause15 = new NativeMenuItem(_loc["Tray_Pause15"]);
        _pause15.Click += (_, _) => pausing?.For(TimeSpan.FromMinutes(15));
        _pause60 = new NativeMenuItem(_loc["Tray_Pause60"]);
        _pause60.Click += (_, _) => pausing?.For(TimeSpan.FromHours(1));
        _pauseOpen = new NativeMenuItem(_loc["Tray_PauseUntil"]);
        _pauseOpen.Click += (_, _) => pausing?.For(null);
        _pause = new NativeMenuItem(_loc["Tray_Pause"])
        {
            Menu = new NativeMenu { Items = { _pause15, _pause60, _pauseOpen } }
        };
        _resume = new NativeMenuItem(_loc["Tray_Resume"]) { IsVisible = false };
        _resume.Click += (_, _) => pausing?.Resume();
        if (pausing is not null)
            pausing.Changed += (_, _) =>
            {
                _resume.IsVisible = pausing.IsPaused;
                _pause.IsVisible = !pausing.IsPaused;
            };

        // Shown only while there is something to copy; entries leave the menu as the history forgets them.
        _history = new NativeMenuItem(_loc["Tray_DictationHistory"]) { Menu = new NativeMenu(), IsVisible = false };
        if (history is not null)
        {
            void Show()
            {
                var recent = history.Recent();
                _history.IsVisible = recent.Count > 0;
                _history.Menu!.Items.Clear();
                foreach (var text in recent)
                {
                    var line = text.ReplaceLineEndings(" ");
                    var item = new NativeMenuItem(line.Length > 50 ? line[..50] + "…" : line);
                    item.Click += (_, _) => copyDictation(text);
                    _history.Menu.Items.Add(item);
                }
            }

            history.Changed += Show;
            _ = new DispatcherTimer(TimeSpan.FromMinutes(1), DispatcherPriority.Background, (_, _) => Show());
        }

        Menu = new NativeMenu { Items = { _settings, _pause, _resume, _history, _exit } };
    }

    // The menu texts in the language now chosen.
    public void Relabel()
    {
        _settings.Header = _loc["Tray_Settings"];
        _history.Header = _loc["Tray_DictationHistory"];
        _pause.Header = _loc["Tray_Pause"];
        _pause15.Header = _loc["Tray_Pause15"];
        _pause60.Header = _loc["Tray_Pause60"];
        _pauseOpen.Header = _loc["Tray_PauseUntil"];
        _resume.Header = _loc["Tray_Resume"];
        _exit.Header = _loc[_exitKey];
    }
}
