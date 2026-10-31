using Mawaqit.WinUI.Data;

namespace Mawaqit.WinUI.Views;

public enum DisplayMode
{
    Compact,
    Expanded
}

public static class ChipLayout
{
    public const int CompactWidthDip = 70;
    /// <summary>0.1 collapsed strip width: prayer, time, countdown on one line.</summary>
    public const int ExpandedWidthDip = 260;

    public static int WidthDip(ChipSize size)
        => size == ChipSize.Compact ? CompactWidthDip : ExpandedWidthDip;

    public static DisplayMode ToDisplayMode(ChipSize size)
        => size == ChipSize.Compact ? DisplayMode.Compact : DisplayMode.Expanded;
}
