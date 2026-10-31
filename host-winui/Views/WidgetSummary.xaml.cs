using Mawaqit.WinUI.Data;
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

    public WidgetSummary()
    {
        InitializeComponent();
        DataContext = AppState.Current;
        AppState.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.ChipName) or nameof(AppState.ChipCountdown)
                or nameof(AppState.ChipTime) or nameof(AppState.ChipLine) or nameof(AppState.IsConfigured)
                or nameof(AppState.IsNear) or nameof(AppState.HasError) or nameof(AppState.TrackingLabel))
                UpdateVisuals();
        };
        AppState.Current.DisplayModeChanged += _ => UpdateVisuals();
        Loaded += (_, _) =>
        {
            UpdateVisuals();
            RaiseMode();
        };
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
        compactTb.Text = string.IsNullOrEmpty(st.ChipCountdown) ? st.ChipName : st.ChipCountdown;
        if (st.IsNear)
            toggle.Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"];
        RaiseMode();
    }

    private void RaiseMode()
    {
        var mode = ChipLayout.ToDisplayMode(AppState.Current.Config.ChipSize);
        if (mode == _mode) return;
        _mode = mode;
        DisplayModeChanged?.Invoke(mode);
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
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
        var cfg = AppState.Current.Config;
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Change mosque…", () => MapWindow.Show()));
        foreach (var fav in cfg.Favorites)
        {
            var sel = fav;
            menu.Items.Add(Item(sel.DisplayName, () => _ = AppState.Current.SwitchFavoriteAsync(sel)));
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Preferences", SettingsWindow.Show));

        var sizeMenu = new MenuFlyoutSubItem { Text = "Chip size" };
        sizeMenu.Items.Add(CheckedItem("Compact", cfg.ChipSize == ChipSize.Compact, () => AppState.Current.ApplyChipSize(ChipSize.Compact)));
        sizeMenu.Items.Add(CheckedItem("Expanded", cfg.ChipSize == ChipSize.Expanded, () => AppState.Current.ApplyChipSize(ChipSize.Expanded)));
        menu.Items.Add(sizeMenu);

        var countMenu = new MenuFlyoutSubItem { Text = "Countdown" };
        countMenu.Items.Add(CheckedItem("Adhan", cfg.Countdown == CountdownTarget.Adhan, () => AppState.Current.ApplyCountdown(CountdownTarget.Adhan)));
        countMenu.Items.Add(CheckedItem("Iqama", cfg.Countdown == CountdownTarget.Iqama, () => AppState.Current.ApplyCountdown(CountdownTarget.Iqama)));
        countMenu.Items.Add(CheckedItem("Adhan, then iqama", cfg.Countdown == CountdownTarget.AdhanThenIqama, () => AppState.Current.ApplyCountdown(CountdownTarget.AdhanThenIqama)));
        menu.Items.Add(countMenu);

        var hostMenu = new MenuFlyoutSubItem { Text = "Placement" };
        hostMenu.Items.Add(CheckedItem("In the taskbar", cfg.Host == HostKind.Injected, () => AppState.Current.ApplyHostKind(HostKind.Injected)));
        hostMenu.Items.Add(CheckedItem("Screen strip", cfg.Host == HostKind.Strip, () => AppState.Current.ApplyHostKind(HostKind.Strip)));
        menu.Items.Add(hostMenu);

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item("Hide", () => TaskBarManager.HideWidget.Execute(null)));
        menu.Items.Add(Item("Quit", () => App.Quit.Execute(null)));
        menu.ShowAt(toggle, e.GetPosition(toggle));
    }

    private static MenuFlyoutItem Item(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private static ToggleMenuFlyoutItem CheckedItem(string text, bool on, Action action)
    {
        var item = new ToggleMenuFlyoutItem { Text = text, IsChecked = on };
        item.Click += (_, _) => action();
        return item;
    }

    private void Flyout_Opened(object sender, object e) => toggle.IsChecked = true;

    private void Flyout_Closed(object sender, object e) => toggle.IsChecked = false;
}
