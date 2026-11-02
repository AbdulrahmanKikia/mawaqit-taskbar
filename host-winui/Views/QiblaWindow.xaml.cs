using Mawaqit.WinUI.Data;
using Mawaqit.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Serilog;
using Windows.Devices.Sensors;

namespace Mawaqit.WinUI.Views;

/// <summary>Offline Qibla finder. Everything here is computed locally from the chosen
/// mosque's coordinates; no network is used.</summary>
public sealed partial class QiblaWindow : Window
{
    private static QiblaWindow? _open;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    private Compass? _compass;
    private double _bearing;

    public QiblaWindow()
    {
        InitializeComponent();
        WindowChrome.Apply(this, AppTitleBar, 480, 760);
        _timer.Tick += (_, _) => Render();
        _timer.Start();
        ThemeHelper.ThemeChanged += OnThemeChanged;
        LocaleManager.Default.CurrentChanged += OnLocaleChanged;
        AppState.Current.PropertyChanged += OnStateChanged;
        Closed += (_, _) =>
        {
            _timer.Stop();
            if (_compass != null) _compass.ReadingChanged -= OnCompassReading;
            ThemeHelper.ThemeChanged -= OnThemeChanged;
            LocaleManager.Default.CurrentChanged -= OnLocaleChanged;
            AppState.Current.PropertyChanged -= OnStateChanged;
            if (ReferenceEquals(_open, this)) _open = null;
        };
        StartCompass();
        Render();
    }

    public static void Show()
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new QiblaWindow();
        _open.Activate();
    }

    private void Render()
    {
        var loc = LocaleManager.Default;
        var culture = loc.CurrentCulture;
        loc.ApplyFlow(root);
        DrawTicks(loc);
        Title = loc.Get("Qibla.Title");
        titleBarTb.Text = loc.Get("Qibla.Title");

        var origin = AppState.Current.QiblaOrigin;
        noLocationPanel.Visibility = origin is null ? Visibility.Visible : Visibility.Collapsed;
        readingPanel.Visibility = origin is null ? Visibility.Collapsed : Visibility.Visible;
        if (origin is null)
        {
            noLocationTb.Text = loc.Get("Qibla.NoLocation");
            chooseMosqueBtn.Content = loc.Get("UI.ChangeMosque");
            return;
        }

        var lat = origin.Latitude!.Value;
        var lon = origin.Longitude!.Value;
        var reading = QiblaGuide.Read(lat, lon, DateTimeOffset.Now);
        _bearing = reading.Bearing;
        UpdateRotation(_compass?.GetCurrentReading());

        bearingTb.Text = string.Format(culture, loc.Get("Qibla.Bearing"),
            reading.Bearing.ToString("0", culture), loc.Get($"Compass.{reading.CompassPoint}"));
        fromTb.Text = string.Format(culture, loc.Get("Qibla.FromMosque"), origin.DisplayName);
        distanceTb.Text = string.Format(culture, loc.Get("Qibla.Distance"), reading.DistanceKm.ToString("N0", culture));

        sunTb.Text = reading.TurnFromSun switch
        {
            null => loc.Get("Qibla.SunDown"),
            { } t when Math.Abs(t) < 3 => loc.Get("Qibla.SunAhead"),
            { } t => string.Format(culture, loc.Get(t > 0 ? "Qibla.SunTurnRight" : "Qibla.SunTurnLeft"),
                Math.Abs(t).ToString("0", culture)),
        };

        if (reading.NextShadow is { } shadow)
        {
            var local = shadow.At.ToLocalTime();
            shadowTb.Text = string.Format(culture, loc.Get(shadow.ShadowPointsToQibla ? "Qibla.ShadowToward" : "Qibla.ShadowAway"),
                local.ToString("d MMMM", culture), local.ToString("HH:mm", culture));
        }
        else
        {
            shadowTb.Text = loc.Get("Qibla.NoShadow");
        }

        compassNoteTb.Text = loc.Get("Qibla.CompassNote");
        offlineTb.Text = loc.Get("Qibla.Offline");
        liveTb.Text = loc.Get("Qibla.Live");
    }

    private void StartCompass()
    {
        try
        {
            _compass = Compass.GetDefault();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Compass sensor unavailable");
            _compass = null;
        }
        if (_compass is null) return;
        _compass.ReportInterval = Math.Max(_compass.MinimumReportInterval, 100u);
        _compass.ReadingChanged += OnCompassReading;
        liveTb.Visibility = Visibility.Visible;
    }

    private void OnCompassReading(Compass sender, CompassReadingChangedEventArgs args)
        => DispatcherQueue.TryEnqueue(() => UpdateRotation(args.Reading));

    /// <summary>With a sensor the dial turns so north stays north; without one it is north-up.</summary>
    private void UpdateRotation(CompassReading? reading)
    {
        var heading = reading is null ? 0 : reading.HeadingTrueNorth ?? reading.HeadingMagneticNorth;
        dialRotation.Angle = -heading;
        needleRotation.Angle = _bearing;
    }

    private void DrawTicks(LocaleManager loc)
    {
        ticks.Children.Clear();
        var brush = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        const double c = 130;
        for (var deg = 0; deg < 360; deg += 15)
        {
            var major = deg % 90 == 0;
            var r1 = major ? 112 : 118;
            var a = deg * Math.PI / 180;
            ticks.Children.Add(new Line
            {
                X1 = c + r1 * Math.Sin(a), Y1 = c - r1 * Math.Cos(a),
                X2 = c + 126 * Math.Sin(a), Y2 = c - 126 * Math.Cos(a),
                Stroke = brush,
                StrokeThickness = major ? 2 : 1,
            });
        }
        foreach (var deg in new[] { 0, 90, 180, 270 })
        {
            var text = loc.Get($"Compass.{deg / 22.5:0}");
            var a = deg * Math.PI / 180;
            var tb = new TextBlock { Text = text, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = brush };
            tb.Measure(new Windows.Foundation.Size(100, 100));
            Canvas.SetLeft(tb, c + 98 * Math.Sin(a) - tb.DesiredSize.Width / 2);
            Canvas.SetTop(tb, c - 98 * Math.Cos(a) - tb.DesiredSize.Height / 2);
            ticks.Children.Add(tb);
        }
    }

    private void ChooseMosque_Click(object sender, RoutedEventArgs e) => MapWindow.Show();

    private void OnStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppState.MosqueLabel) or nameof(AppState.IsConfigured))
            DispatcherQueue.TryEnqueue(Render);
    }

    private void OnThemeChanged() => DispatcherQueue.TryEnqueue(() => WindowChrome.ApplyTheme(this));

    private void OnLocaleChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(Render);
}
