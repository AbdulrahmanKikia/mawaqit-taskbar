using Mawaqit.WinUI.Data;
using Mawaqit.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;

namespace Mawaqit.WinUI.Views;

public sealed partial class WidgetSummary : UserControl
{
    public static readonly DependencyProperty ElementsAlignmentProperty = DependencyProperty.Register(
        nameof(ElementsAlignment),
        typeof(HorizontalAlignment),
        typeof(WidgetSummary),
        new PropertyMetadata(HorizontalAlignment.Center, OnAlignChanged));

    public HorizontalAlignment ElementsAlignment
    {
        get => (HorizontalAlignment)GetValue(ElementsAlignmentProperty);
        set => SetValue(ElementsAlignmentProperty, value);
    }

    public event Action<DisplayMode>? DisplayModeChanged;

    private DisplayMode _mode = DisplayMode.Expanded;
    private bool _holdOpenedMenu;

    #region Construction

    public WidgetSummary()
    {
        InitializeComponent();
        DataContext = AppState.Current;
        AppState.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.ChipName) or nameof(AppState.ChipCountdown)
                or nameof(AppState.ChipTime) or nameof(AppState.ChipLine) or nameof(AppState.IsConfigured)
                or nameof(AppState.IsNear) or nameof(AppState.HasError) or nameof(AppState.TrackingLabel)
                or nameof(AppState.Status))
                UpdateVisuals();
        };
        AppState.Current.DisplayModeChanged += _ => UpdateVisuals();
        Loaded += (_, _) =>
        {
            ApplyChipTheme();
            ThemeHelper.ThemeChanged += OnThemeChanged;
            LocaleManager.Default.CurrentChanged += OnLocaleChanged;
            UpdateVisuals();
            RaiseMode();
        };
        Unloaded += (_, _) =>
        {
            ThemeHelper.ThemeChanged -= OnThemeChanged;
            LocaleManager.Default.CurrentChanged -= OnLocaleChanged;
        };
    }

    #endregion

    #region Theme, locale, and chip visuals

    private void OnThemeChanged()
    {
        if (DispatcherQueue.HasThreadAccess) ApplyChipTheme();
        else DispatcherQueue.TryEnqueue(ApplyChipTheme);
    }

    private void OnLocaleChanged(object? sender, EventArgs e)
    {
        void Go()
        {
            LocaleManager.Default.ApplyFlow(this);
            UpdateVisuals();
        }
        if (DispatcherQueue.HasThreadAccess) Go();
        else DispatcherQueue.TryEnqueue(Go);
    }

    private void ApplyChipTheme()
    {
        RequestedTheme = ThemeHelper.TaskbarTheme;
        LocaleManager.Default.ApplyFlow(this);
        UpdateVisuals();
    }

    private static void OnAlignChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WidgetSummary s)
        {
            var align = (HorizontalAlignment)e.NewValue;
            s.expandedTb.HorizontalAlignment = align;
            s.compactTb.HorizontalAlignment = align;
        }
    }

    private void UpdateVisuals()
    {
        var st = AppState.Current;
        var compact = st.Config.ChipSize == ChipSize.Compact;
        expandedTb.Visibility = !compact && st.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
        compactTb.Visibility = compact && st.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
        setupTb.Visibility = st.IsConfigured ? Visibility.Collapsed : Visibility.Visible;
        expandedTb.Text = st.ChipLine;
        setupTb.Text = LocaleManager.Default.Get("UI.TapToStart");
        compactTb.Text = string.IsNullOrEmpty(st.ChipCountdown) ? st.ChipName : st.ChipCountdown;
        if (st.HasError)
            toggle.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        else if (st.IsNear)
            toggle.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BrandHighlightBrush"];
        else
            toggle.ClearValue(Control.ForegroundProperty);
        RaiseMode();
    }

    private void RaiseMode()
    {
        var mode = ChipLayout.ToDisplayMode(AppState.Current.Config.ChipSize);
        if (mode == _mode) return;
        _mode = mode;
        DisplayModeChanged?.Invoke(mode);
    }

    #endregion

    #region Click, right-click, and touch hold

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (_holdOpenedMenu)
        {
            _holdOpenedMenu = false;
            toggle.IsChecked = false;
            return;
        }
        if (!AppState.Current.IsConfigured)
        {
            MapWindow.Show();
            toggle.IsChecked = false;
            return;
        }
        FlyoutBase.ShowAttachedFlyout(toggle);
    }

    private void Toggle_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        e.Handled = true;
        ChipMenu.Show(toggle, e.GetPosition(toggle));
    }

    private void Toggle_Holding(object sender, HoldingRoutedEventArgs e)
    {
        if (e.HoldingState != Microsoft.UI.Input.HoldingState.Started) return;
        e.Handled = true;
        _holdOpenedMenu = true;
        ChipMenu.Show(toggle, e.GetPosition(toggle));
    }

    private void Flyout_Opened(object sender, object e) => toggle.IsChecked = true;

    private void Flyout_Closed(object sender, object e) => toggle.IsChecked = false;

    #endregion
}
