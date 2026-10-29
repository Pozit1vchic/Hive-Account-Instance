using System.IO;
using System.Windows;
using Hive.Core;
using Hive.Storage;
using Hive.Infrastructure;
using Hive.Browser;
using Hive.Roblox;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Serilog;
namespace Hive.App;

public partial class App : Application
{
    private IHost? host;
    private readonly bool initializeServices;
    public App() : this(true) { }
    public App(bool initializeServices) => this.initializeServices = initializeServices;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (!initializeServices)
            return;
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hive");
        Directory.CreateDirectory(root);
        host = Host.CreateDefaultBuilder().ConfigureAppConfiguration(c => c.SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.json", optional: true))
         .UseSerilog((_, logger) => logger.MinimumLevel.Information().WriteTo.File(Path.Combine(root, "Logs", "hive-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7))
         .ConfigureServices((context, services) =>
         {
             services.AddSingleton(new HiveDatabase(Path.Combine(root, "hive.db")));
             services.AddSingleton<ISecretProtector, DpapiSecretProtector>();
             services.AddSingleton<IAccountRepository, AccountRepository>();
             services.AddSingleton<IAccountGroupRepository, AccountGroupRepository>();
             services.AddSingleton<IBrowserProfileRepository, BrowserProfileRepository>();
             services.AddSingleton<ISessionStore, SessionStore>();
             services.AddSingleton<AccountOnboardingService>();
             services.AddSingleton<RamImportService>();
             services.AddSingleton<ICredentialStore, CredentialStore>();
             services.AddSingleton<INetworkLog, NetworkLog>();
             services.AddSingleton<SessionManager>();
             services.AddSingleton<ISessionManager>(sp => sp.GetRequiredService<SessionManager>());
             services.AddSingleton<IAccountActivityTracker, AccountActivityTracker>();
             services.AddSingleton<IRobloxLaunchBroker>(sp => sp.GetRequiredService<SessionManager>());
             services.AddSingleton<ILaunchDispatcher, RobloxProcessLauncher>();
             services.AddSingleton<IInstanceManager, InstanceManager>();
             services.AddSingleton(AppPreferences.Load());
             services.AddSingleton<IGameWindowPreferences>(sp => sp.GetRequiredService<AppPreferences>());
             services.AddSingleton<ILaunchPolicy>(sp => sp.GetRequiredService<AppPreferences>());
             services.AddSingleton<IMultiInstanceCoordinator, MultiInstanceCoordinator>();
             services.AddSingleton<IBrowserProvider>(sp => new ConfiguredBrowserProvider(sp.GetRequiredService<AppPreferences>(), context.Configuration["Browser:ExecutablePath"] ?? ""));
             services.AddSingleton<AccountsViewModel>();
             services.AddSingleton<MainWindow>();
         }).Build();
        try
        {
            await host.StartAsync();
            await host.Services.GetRequiredService<HiveDatabase>().InitializeAsync();
            var vm = host.Services.GetRequiredService<AccountsViewModel>();
            await vm.LoadAsync();
            if (host.Services.GetRequiredService<AppPreferences>().MultiInstance)
            {
                try { host.Services.GetRequiredService<IMultiInstanceCoordinator>().Prepare(); }
                catch (RobloxLaunchException error) { vm.Status = error.Message; }
            }
            ThemeManager.Apply(host.Services.GetRequiredService<AppPreferences>());
            host.Services.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception ex) { Log.Error("Hive startup failed ({ErrorType})", ex.GetType().Name); MessageBox.Show(@"Hive could not open local storage. Check access to %LOCALAPPDATA%\Hive.", "Hive"); Shutdown(1); }
    }
    private void OnSwitchLoaded(object sender, RoutedEventArgs e) => PositionSwitch(sender, false);
    private void OnSwitchChanged(object sender, RoutedEventArgs e) => PositionSwitch(sender, true);
    private static void PositionSwitch(object sender, bool animate)
    {
        if (sender is not System.Windows.Controls.CheckBox toggle || !toggle.IsLoaded)
            return;
        if (toggle.Template.FindName("Thumb", toggle) is not FrameworkElement thumb)
            return;
        var target = toggle.IsChecked == true ? 14d : 0d;
        var previous = thumb.RenderTransform is System.Windows.Media.TranslateTransform shift ? shift.X : 0d;
        var transform = new System.Windows.Media.TranslateTransform(target, 0);
        thumb.RenderTransform = transform;
        if (animate && Motion.GetEnabled(toggle) && SystemParameters.ClientAreaAnimation)
        {
            transform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
                new System.Windows.Media.Animation.DoubleAnimation(previous, target, TimeSpan.FromMilliseconds(160))
                {
                    EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
                });
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
