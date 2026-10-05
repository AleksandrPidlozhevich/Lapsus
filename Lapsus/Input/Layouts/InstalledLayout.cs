using Lapsus.Core.Layout;
using System;

namespace Lapsus.Input;

public sealed record InstalledLayout(
    string LayoutId,
    IntPtr Hkl,
    Script? Script,
    string? LanguageCode,
    KeyboardLayout? Target,
    KeyboardMap Map);
