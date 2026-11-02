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
        Loaded += (_, _) => Bind();
        progressTrack.SizeChanged += (_, _) => UpdateProgress();
        LocaleManager.Default.CurrentChanged += (_, _) =>
        {
            if (DispatcherQueue.HasThreadAccess) Bind();
            else DispatcherQueue.TryEnqueue(Bind);
        };
        AppState.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(AppState.NextProgress))
            {
                if (DispatcherQueue.HasThreadAccess) UpdateProgress();
                else DispatcherQueue.TryEnqueue(UpdateProgress);
                return;
            }
            if (DispatcherQueue.HasThreadAccess) Bind();
            else DispatcherQueue.TryEnqueue(Bind);
        };
    }

    private void Bind()
    {
        var loc = LocaleManager.Default;
        var st = AppState.Current;
        loc.ApplyFlow(this);
        prayerCol.Text = loc.Get("UI.Prayer");
        adhanCol.Text = loc.Get("UI.Adhan");
        iqamaCol.Text = loc.Get("UI.Iqama");
        qiblaBtnTb.Text = loc.Get("UI.QiblaShort");
        mosqueBtnTb.Text = loc.Get("UI.ChangeMosque");
        ToolTipService.SetToolTip(settingsBtn, loc.Get("UI.PreferencesTip"));
        ToolTipService.SetToolTip(refreshBtn, loc.Get("UI.Refresh"));
        ToolTipService.SetToolTip(qiblaBtn, loc.Get("UI.Qibla"));

        nextLabelTb.Text = loc.Get("UI.NextPrayer").ToUpper(loc.CurrentCulture);
        titleTb.Text = string.IsNullOrEmpty(st.ChipName) ? loc.Get("Data.AppName") : st.ChipName;
        countdownTb.Text = st.ChipCountdown;
        atTb.Text = string.IsNullOrEmpty(st.ChipTime) ? "" : string.Format(loc.Get("UI.AtTime"), st.ChipTime);

        mosqueTb.Text = st.MosqueLabel;
        Show(sourceTb, st.SourceDetail);
        Show(freshnessTb, st.FreshnessLabel);
        if (!_listBound)
        {
            list.ItemsSource = st.Slots;
            _listBound = true;
        }
        UpdateProgress();
    }

    private void UpdateProgress()
        => progressFill.Width = Math.Max(0, progressTrack.ActualWidth * Math.Clamp(AppState.Current.NextProgress, 0, 1));

    private static void Show(TextBlock tb, string text)
    {
        tb.Text = text;
        tb.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsWindow.Show();
    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = AppState.Current.RefreshAsync(true);
    private void Mosque_Click(object sender, RoutedEventArgs e) => MapWindow.Show();
    private void Qibla_Click(object sender, RoutedEventArgs e) => QiblaWindow.Show();
}
