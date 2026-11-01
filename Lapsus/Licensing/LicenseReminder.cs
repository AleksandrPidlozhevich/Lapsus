using Avalonia.Controls;
using Avalonia.Threading;
using Lapsus.ViewModels;
using Lapsus.Views;
using System;
using System.Threading.Tasks;

namespace Lapsus.Licensing;

// A reminder only, never a feature gate.
internal sealed class LicenseReminder(LicenseStatus status, LicenseViewModel viewModel)
{
    private static readonly TimeSpan ReminderInterval = TimeSpan.FromDays(1);

    private static readonly TimeSpan ReminderPollInterval = TimeSpan.FromMinutes(30);

    private DispatcherTimer? _timer;
    private Window? _window;
    private DateTimeOffset _shownAt;
    private bool _stopped;

    public async Task CheckAsync()
    {
        var required = await status.RefreshAsync().ConfigureAwait(true);
        viewModel.OnStatusRefreshed();

        StartTimer();

        if (!required)
            return;

        Dispatcher.UIThread.Post(Show, DispatcherPriority.Loaded);
    }

    public void Close()
    {
        _window?.Close();
        _window = null;
    }

    public void Stop()
    {
        _stopped = true;
        _timer?.Stop();
        _timer = null;
    }

    private void StartTimer()
    {
        if (_timer is not null || _stopped)
            return;

        _timer = new DispatcherTimer { Interval = ReminderPollInterval };
        _timer.Tick += (_, _) => RemindIfDue();
        _timer.Start();
    }

    private void RemindIfDue()
    {
        if (_stopped)
            return;

        if (!status.LicenseRequired || status.IsEvaluating)
            return;

        if (DateTimeOffset.UtcNow - _shownAt < ReminderInterval)
            return;

        Show();
    }

    private void Show()
    {
        _shownAt = DateTimeOffset.UtcNow;

        if (_window is not null)
        {
            _window.Activate();
            return;
        }

        _window = new LicenseReminderWindow { DataContext = viewModel };
        _window.Closed += (_, _) => _window = null;
        _window.Show();
    }
}
