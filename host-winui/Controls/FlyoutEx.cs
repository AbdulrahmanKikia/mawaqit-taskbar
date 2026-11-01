// Adapted from Awqat Salaat (https://github.com/Khiro95/Awqat-Salaat)
// Copyright (c) 2023 Khiro
// Licensed under the MIT License.
using Mawaqit.WinUI.Helpers;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Mawaqit.WinUI.Controls;

public class FlyoutEx : Flyout
{
    private Control? _presenter;
    private Popup? _popup;
    private bool _first = true;

    public FlyoutEx()
    {
        Opened += OnOpened;
        Opening += (_, _) => ApplyTheme();
        ThemeHelper.ThemeChanged += ApplyTheme;
    }

    public Control? GetPresenter() => _presenter;

    protected override Control CreatePresenter()
    {
        var presenter = base.CreatePresenter();
        try
        {
            var displayArea = DisplayArea.GetFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId, DisplayAreaFallback.Primary);
            double maxH = displayArea.WorkArea.Height / XamlRoot.RasterizationScale - 8;
            double maxW = displayArea.WorkArea.Width / XamlRoot.RasterizationScale - 4;
            if (presenter.MaxHeight > maxH) presenter.MaxHeight = maxH;
            if (presenter.MaxWidth > maxW) presenter.MaxWidth = maxW;
        }
        catch { /* island may not be ready */ }
        _presenter = presenter;
        return presenter;
    }

    private void OnOpened(object sender, object e)
    {
        if (_first)
        {
            _popup = _presenter?.Parent as Popup;
            if (_popup != null)
                _popup.GotFocus += (_, _) => _presenter?.Focus(FocusState.Programmatic);
            _first = false;
        }
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        if (_popup is null) return;
        void Set()
        {
            _popup.RequestedTheme = ThemeHelper.GeneralTheme;
            if (_presenter != null)
            {
                _presenter.RequestedTheme = _popup.ActualTheme;
                LocaleManager.Default.ApplyFlow(_presenter);
            }
        }
        if (DispatcherQueue.HasThreadAccess) Set();
        else DispatcherQueue.TryEnqueue(Set);
    }
}
