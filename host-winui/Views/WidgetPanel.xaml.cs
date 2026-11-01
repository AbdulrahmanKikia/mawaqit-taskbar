using Mawaqit.WinUI.Data;
using Mawaqit.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mawaqit.WinUI.Views;

public sealed partial class WidgetPanel : UserControl
{
    private bool _listBound;

    public WidgetPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => Bind(full: true);
        LocaleManager.Default.CurrentChanged += (_, _) =>
        {
            if (DispatcherQueue.HasThreadAccess) Bind(true);
            else DispatcherQueue.TryEnqueue(() => Bind(true));
        };
        AppState.Current.PropertyChanged += (_, e) =>
        {
            bool full = e.PropertyName is nameof(AppState.MosqueLabel) or nameof(AppState.IsConfigured)
                or nameof(AppState.ChipName) or nameof(AppState.ChipCountdown) or null;
            void Go() => Bind(full);
            if (DispatcherQueue.HasThreadAccess) Go();
            else DispatcherQueue.TryEnqueue(Go);
        };
    }

    private void Bind(bool full)
    {
        var loc = LocaleManager.Default;
        loc.ApplyFlow(this);
        prayerCol.Text = loc.Get("UI.Prayer");
        adhanCol.Text = loc.Get("UI.Adhan");
        iqamaCol.Text = loc.Get("UI.Iqama");
        mosqueLink.Content = loc.Get("UI.ChangeMosque");
        ToolTipService.SetToolTip(settingsBtn, loc.Get("UI.PreferencesTip"));
        ToolTipService.SetToolTip(refreshBtn, loc.Get("UI.Refresh"));
        var st = AppState.Current;
        titleTb.Text = string.IsNullOrEmpty(st.ChipName) ? loc.Get("Data.AppName") : st.ChipName;
        countdownTb.Text = string.IsNullOrEmpty(st.ChipCountdown) ? "" : st.ChipCountdown;
        if (full || mosqueTb.Text != st.MosqueLabel)
            mosqueTb.Text = st.MosqueLabel;
        if (!_listBound)
        {
            list.ItemsSource = st.Slots;
            _listBound = true;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsWindow.Show();
    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = AppState.Current.RefreshAsync(true);
    private void Mosque_Click(object sender, RoutedEventArgs e) => MapWindow.Show();
}
