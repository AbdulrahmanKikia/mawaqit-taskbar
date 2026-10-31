using Mawaqit.WinUI.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mawaqit.WinUI.Views;

public sealed partial class StripBar : UserControl
{
    public event Action<DisplayMode>? DisplayModeChanged
    {
        add => chip.DisplayModeChanged += value;
        remove => chip.DisplayModeChanged -= value;
    }

    public HorizontalAlignment ElementsAlignment
    {
        get => chip.ElementsAlignment;
        set => chip.ElementsAlignment = value;
    }

    public StripBar()
    {
        InitializeComponent();
        AppState.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AppState.ChipLine) or nameof(AppState.IsConfigured)
                or nameof(AppState.MosqueLabel) or null)
            {
                if (DispatcherQueue.HasThreadAccess) ApplyDensity();
                else DispatcherQueue.TryEnqueue(ApplyDensity);
            }
        };
        Loaded += (_, _) => ApplyDensity();
    }

    public void ApplyDensity()
    {
        chip.Width = ChipLayout.WidthDip(AppState.Current.Config.ChipSize);
        mosqueTb.Text = AppState.Current.MosqueLabel;
    }
}
