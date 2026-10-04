using System.Runtime.Versioning;
using Lapsus.Input;

namespace Lapsus.Tests;

// Calls the real macOS APIs the capture path depends on, so a wrong signature or a crash shows up in
// the test run instead of on a user's machine. On other systems these tests return immediately.
[SupportedOSPlatform("macos")]
public class MacNativeSmokeTests
{
    [Fact]
    public void The_app_activity_token_can_be_begun_and_ended_repeatedly()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        MacActivity.Begin();
        Assert.True(MacActivity.IsActive);

        MacActivity.Begin();
        Assert.True(MacActivity.IsActive);

        MacActivity.End();
        Assert.False(MacActivity.IsActive);

        MacActivity.End();
        Assert.False(MacActivity.IsActive);
    }

    [Fact]
    public void Display_reconfiguration_can_be_subscribed_and_removed_repeatedly()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        MacDisplayChanges.Subscribe(() => { });
        Assert.True(MacDisplayChanges.IsSubscribed);

        MacDisplayChanges.Subscribe(() => { });
        Assert.True(MacDisplayChanges.IsSubscribed);

        MacDisplayChanges.Unsubscribe();
        Assert.False(MacDisplayChanges.IsSubscribed);

        MacDisplayChanges.Unsubscribe();
        Assert.False(MacDisplayChanges.IsSubscribed);
    }

    [Fact]
    public void The_executable_of_this_process_resolves_through_libproc()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var path = MacProcessNames.ExecutablePath(Environment.ProcessId);

        Assert.False(string.IsNullOrEmpty(path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void An_accessibility_element_can_be_created_for_this_process()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        var app = MacOSNativeMethods.AXUIElementCreateApplication(Environment.ProcessId);
        Assert.NotEqual(IntPtr.Zero, app);
        MacOSNativeMethods.CFRelease(app);
    }

    [Fact]
    public void Caret_sampling_does_not_throw_when_accessibility_is_not_granted()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        _ = MacCaretProbe.Read();
        _ = MacCaretProbe.ForegroundProcessName();
    }

    [Fact]
    public void Installed_apps_are_classified_by_their_real_bundle_layout()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        // Electron (Cursor) must be recognized; a native Apple app (Safari) must not. A missing app
        // is skipped rather than failing, since the test machine decides what is installed.
        const string electron = "/Applications/Cursor.app/Contents/MacOS/Cursor";
        const string native = "/Applications/Safari.app/Contents/MacOS/Safari";

        if (File.Exists(electron))
            Assert.True(ChromiumApps.IsChromiumExecutable(electron));

        if (File.Exists(native))
            Assert.False(ChromiumApps.IsChromiumExecutable(native));
    }
}
