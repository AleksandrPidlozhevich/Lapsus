namespace Lapsus.Input;

// Electron's focused element is a group. The document web area is often not among the first
// children, so a walk that keeps source order spends its budget on the sidebar.
internal static class CaretSearchOrder
{
    internal static int Rank(string role)
    {
        return role switch
        {
            "AXWebArea" => 0,
            "AXScrollArea" or "AXSplitGroup" => 1,
            "AXTextArea" or "AXTextField" or "AXComboBox" or "AXSearchField" => 2,
            "AXGroup" or "AXWindow" or "" => 3,
            _ => 4
        };
    }

    internal static bool IsText(string role)
    {
        return role is "AXTextArea" or "AXTextField" or "AXComboBox" or "AXSearchField" or "AXWebArea";
    }

    internal static bool IsContainer(string role)
    {
        return role is "AXGroup" or "AXScrollArea" or "AXSplitGroup" or "AXWebArea" or "AXWindow" or "";
    }
}
