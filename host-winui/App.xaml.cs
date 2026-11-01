using Mawaqit.WinUI.Data;
using Mawaqit.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Serilog;
using System.Windows.Input;

namespace Mawaqit.WinUI;

public partial class App : Application
{
    private static Mutex? _mutex;

    public static event Action? Quitting;
    public static ICommand Quit { get; }
    public static IntPtr MainHandle
    {
        get
        {
            try { return TaskBarManager.CurrentWidgetHandle; }
            catch { return IntPtr.Zero; }
        }
    }

    static App()
    {
        Quit = new RelayCommand(_ => QuitExecute());
    }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            Log.Fatal(e.Exception, e.Exception.Message);
            MessageBox.Error(e.Exception.ToString());
            e.Handled = true;
        };
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        InitLogger();
        if (!Helpers.SystemInfos.IsWindows11_OrLater)
        {
            MessageBox.Error(string.Format(LocaleManager.Default.Get("UI.RequiresWin11"), AppState.Version));
            Exit();
            return;
        }

        const string mutexId = @"Local\MawaqitShellWidget";
        _mutex = new Mutex(true, mutexId, out bool created);
        if (!created)
        {
            MessageBox.Info(LocaleManager.Default.Get("UI.AlreadyRunning"));
            Exit();
            return;
        }

        AppState.Current.Load();
        Autostart.Apply(AppState.Current.Config.StartWithWindows);

        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        ThemeHelper.Initialize(dispatcher);
        try
        {
            TaskBarManager.Initialize(dispatcher);
        }
        catch (WidgetNotInjectedException ex)
        {
            Log.Warning(ex, ex.Message);
            var result = MessageBox.Error(
                string.Format(LocaleManager.Default.Get("UI.InjectRetry"), ex.Message),
                Interop.MessageBoxButtons.MB_RETRYCANCEL);
            if (result == Interop.MessageBoxResult.IDRETRY)
            {
                try { TaskBarManager.Initialize(dispatcher); }
                catch (Exception retryEx)
                {
                    Log.Warning(retryEx, "inject retry failed");
                    MessageBox.Warning(LocaleManager.Default.Get("UI.InjectFailed"));
                }
            }
            else
            {
                MessageBox.Info(LocaleManager.Default.Get("UI.ChipHidden"));
            }
        }

        NotificationService.Init();
        AppState.Current.Start();
    }

    public static void QuitExecute()
    {
        Log.Information("Quitting");
        Quitting?.Invoke();
        Current.Exit();
        Log.CloseAndFlush();
    }

    private static void InitLogger()
    {
        var dir = Path.Combine(AppConfigStore.RootDir, "logs");
        Directory.CreateDirectory(dir);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(Path.Combine(dir, "widget-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
            .CreateLogger();
        Log.Information("Mawaqit Widget {Version} starting", AppState.Version);
    }
}
