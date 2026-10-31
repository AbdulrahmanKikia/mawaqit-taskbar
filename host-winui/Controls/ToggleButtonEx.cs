// Adapted from Awqat Salaat (https://github.com/Khiro95/Awqat-Salaat)
// Copyright (c) 2023 Khiro
// Licensed under the MIT License.
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Mawaqit.WinUI.Controls;

internal class ToggleButtonEx : ToggleButton
{
    public ToggleButtonEx()
    {
        DefaultStyleKey = typeof(ToggleButton);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => null!;
}
