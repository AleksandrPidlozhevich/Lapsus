using Lapsus.Core.Input;
using Lapsus.Core.Text;
using System;

namespace Lapsus.Input;

// The hotkey bound to each selection action (0 = none).
internal sealed class SelectionTriggers
{
    private readonly int[] _triggers = new int[Enum.GetValues<SelectionAction>().Length];

    public void Set(SelectionAction action, int trigger)
    {
        _triggers[(int)action] = trigger;
    }

    public SelectionAction? For(int trigger)
    {
        if (trigger == 0)
            return null;

        for (var i = 0; i < _triggers.Length; i++)
            if (_triggers[i] == trigger)
                return (SelectionAction)i;

        return null;
    }

    public SelectionAction? ForKey(int key, HotkeyModifiers held, bool plainAllowed)
    {
        for (var i = 0; i < _triggers.Length; i++)
            if (Matches(_triggers[i], key, held, plainAllowed))
                return (SelectionAction)i;

        return null;
    }

    // A plain key fires only where the platform allows it (no shortcut modifier held); a combo needs its exact modifiers.
    public static bool Matches(int trigger, int key, HotkeyModifiers held, bool plainAllowed)
    {
        if (trigger == 0)
            return false;

        if (HotkeyCombo.IsCombo(trigger))
            return HotkeyCombo.Triggers(trigger, key, held);

        return plainAllowed && trigger == key;
    }
}
