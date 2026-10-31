using Mawaqit.WinUI.Data;
using Mawaqit.WinUI.Helpers;
using System;

namespace Mawaqit.WinUI;

[Flags]
internal enum WidgetHostCapabilities
{
    None = 0,
    ClippedByExplorer = 1,
    FollowsAutoHide = 2,
    SupportsManualPlace = 4,
}

internal interface IWidgetHost : IDisposable
{
    IntPtr Handle { get; }
    string DisplayPath { get; }
    WidgetHostCapabilities Capabilities { get; }
    event EventHandler Destroying;

    void Initialize();
    void Show();
    void Destroy();
    void UpdatePosition(bool force = false);
    void StartDragging();
    void ApplyChipSize(ChipSize size);
    void InvalidateElementsAlignment(bool autoAlign);
}

internal static class WidgetHostFactory
{
    public static IWidgetHost Create(DisplayEntity display, HostKind kind)
        => kind == HostKind.Strip ? new AppBarWidget(display) : new TaskBarWidget(display);
}
