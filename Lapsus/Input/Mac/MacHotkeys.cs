namespace Lapsus.Input;

internal static class MacHotkeys
{

    public const int SpaceKeyCode = 0x31;

    // Control-Option-Space. Also the system "next input source" shortcut, so it is not offered
    // as the correction hotkey. Kept so a settings file that still stores it can be recognized.
    public const int CtrlOptionSpace = 0x0100_0031;
}
