using Mawaqit.WinUI.Data;
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
        AppState.Current.PropertyChanged += (_, e) =>
        {
            bool full = e.PropertyName is nameof(AppState.MosqueLabel) or nameof(AppState.IsConfigured) or null;
            void Go() => Bind(full);
            if (DispatcherQueue.HasThreadAccess) Go();
            else DispatcherQueue.TryEnqueue(Go);
        };
    }

    private void Bind(bool full)
    {
        var st = AppState.Current;
        titleTb.Text = string.IsNullOrEmpty(st.NextHeadline) ? "Mawaqit" : st.NextHeadline;
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
