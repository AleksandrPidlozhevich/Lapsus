using Avalonia.Controls;
using Avalonia.Input;
using System;

namespace Lapsus.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        KeyDown += (_, e) =>
        {
            var isMacCloseChord = OperatingSystem.IsMacOS()
                                  && e.Key == Key.W && e.KeyModifiers == KeyModifiers.Meta;
            if (e.Key != Key.Escape && !isMacCloseChord)
                return;

            e.Handled = true;
            Close();
        };
    }
}
