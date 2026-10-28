using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Hive.Core;
using Hive.Roblox;
using Microsoft.Extensions.Logging;
namespace Hive.App;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}
public sealed class AccountRow(Account data) : ObservableObject
{
    public Account Data { get; private set; } = data;
    public string Id => Data.Id; public string Username => Data.Username; public string Notes => Data.Notes;
    private string passwordText = "—"; public string PasswordText
    {
        get => passwordText; set
        {
            passwordText = value;
            Changed();
        }
    }
    private int? processId; public int? ProcessId
    {
        get => processId; set
        {
            processId = value;
            Changed();
            Changed(nameof(IsRunning));
            Changed(nameof(StateLabel));
        }
    }
    public bool IsRunning => ProcessId.HasValue;
    public bool IsInvalid => Data.SessionState == SessionState.ReauthenticationRequired;
    public string StateLabel => IsInvalid ? "Sign-in required" : IsRunning ? $"Running · PID {ProcessId}" : Data.SessionState == SessionState.Valid ? "Session valid · not running" : "Not running · session not checked";
    public void UpdateState(SessionState state)
    {
        Data = Data with
        {
            SessionState = state
        };
        Changed(nameof(IsInvalid));
        Changed(nameof(StateLabel));
    }
}
public sealed class AccountsViewModel : ObservableObject
{
    private readonly IInstanceManager? instances;
    private readonly IAccountRepository repository; private readonly ICredentialStore credentials;
    private readonly SessionManager sessions; private readonly IAccountActivityTracker tracker; private readonly IBrowserProvider browser;
    private readonly AccountOnboardingService? onboarding; private readonly ISessionStore? sessionStore;
    private readonly ConcurrentDictionary<string, IBrowserSession> openBrowsers = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly AppPreferences preferences; private readonly ILogger<AccountsViewModel> logger;
    private List<AccountRow> all = []; private AccountRow? selected;
    private string username = "", notes = "", search = "", status = "Ready", processId = ""; private bool busy, settingsOpen;
    private Task? activeOperation;
    private CancellationTokenSource? loginCancellation;
    public AccountsViewModel(IAccountRepository repository, ICredentialStore credentials, SessionManager sessions, IAccountActivityTracker tracker, IBrowserProvider browser, AppPreferences preferences, ILogger<AccountsViewModel> logger, AccountOnboardingService? onboarding = null, ISessionStore? sessionStore = null, IInstanceManager? instances = null)
    {
        this.instances = instances;
        this.onboarding = onboarding;
        this.sessionStore = sessionStore;
        this.repository = repository;
        this.credentials = credentials;
        this.sessions = sessions;
        this.tracker = tracker;
        this.browser = browser;
        this.preferences = preferences;
        this.logger = logger;
        SaveCommand = Command(SaveAsync);
        DeleteCommand = Command(DeleteAsync);
        NewCommand = Command(() => { Selected = null; Username = ""; Notes = ""; ClearSecrets(); Status = "New account"; return Task.CompletedTask; });
        LaunchCommand = Command(LaunchSelectedAsync);
        AddBrowserCommand = Command(() => AuthenticateAsync(null));
        OpenBrowserCommand = Command(OpenBrowserAsync);
        AllFilterCommand = Command(() => { FilterMode = "All"; return Task.CompletedTask; });
        RunningFilterCommand = Command(() => { FilterMode = "Running"; return Task.CompletedTask; });
        AttentionFilterCommand = Command(() => { FilterMode = "Attention"; return Task.CompletedTask; });
        CheckCommand = Command(() => CheckSessionsAsync(true));
        SignInCommand = Command(SignInAsync);
        ImportCookieCommand = Command(ImportCookieAsync);
        AttachCommand = Command(() => { if (Selected is null) return Task.CompletedTask; if (!int.TryParse(ProcessId, out var pid)) { Status = "Enter a RobloxPlayerBeta PID from Task Manager."; return Task.CompletedTask; } tracker.Attach(Selected.Id, pid); RefreshActivity(); Status = "Process linked to the selected account."; return Task.CompletedTask; });
        DetachCommand = Command(() => { if (Selected is not null) tracker.Detach(Selected.Id); RefreshActivity(); return Task.CompletedTask; });
        CancelLoginCommand = new AsyncCommand(() => { loginCancellation?.Cancel(); return Task.CompletedTask; });
    }
    private AsyncCommand Command(Func<Task> action) => new(() => RunAsync(action));
    private async Task RunAsync(Func<Task> action)
    {
        if (IsBusy)
            return;
        IsBusy = true;
        try
        {
            activeOperation = action();
            await activeOperation;
        }
        catch (OperationCanceledException) { Status = "Operation cancelled."; }
        catch (ProcessAssignmentException ex) { Status = ex.Message; }
        catch (RobloxLaunchException ex) { Status = ex.Message; }
        catch (Exception ex) when (HasBrowserConfigurationError(ex)) { Status = "Windows could not start Chromium (14001). Choose another installed Chrome or Edge in Settings > Browser."; }
        catch (Exception ex) { logger.LogWarning("Account operation failed ({ErrorType})", ex.GetType().Name); Status = ex is System.IO.FileNotFoundException ? "Choose a Chrome or Edge executable in Settings > Browser." : "Could not complete the operation. Check storage access and your connection."; }
        finally { activeOperation = null; IsBusy = false; }
    }
    private static bool HasBrowserConfigurationError(Exception error)
    {
        for (Exception? current = error; current is not null; current = current.InnerException)
            if (current is System.ComponentModel.Win32Exception { NativeErrorCode: 14001 })
                return true;
        return false;
    }
    public ObservableCollection<AccountRow> Accounts { get; } = [];
    public ICommand LaunchCommand
    {
        get;
    }
    public ICommand AddBrowserCommand
    {
        get;
    }
    public ICommand OpenBrowserCommand
    {
        get;
    }
    public ICommand AllFilterCommand
    {
        get;
    }
    public ICommand RunningFilterCommand
    {
        get;
    }
    public ICommand AttentionFilterCommand
    {
        get;
    }
    public ICommand SaveCommand
    {
        get;
    }
    public ICommand DeleteCommand
    {
        get;
    }
    public ICommand NewCommand
    {
        get;
    }
    public ICommand CheckCommand
    {
        get;
    }
    public ICommand SignInCommand
    {
        get;
    }
    public ICommand ImportCookieCommand
    {
        get;
    }
    public ICommand AttachCommand
    {
        get;
    }
    public ICommand DetachCommand
    {
        get;
    }
    public ICommand CancelLoginCommand
    {
        get;
    }
    public event Action? ClearSensitiveInputs;
    public string PasswordDraft { private get; set; } = "";
    public string CookieDraft { private get; set; } = "";
    private void ClearSecrets()
    {
        PasswordDraft = "";
        CookieDraft = "";
        ClearSensitiveInputs?.Invoke();
    }
    public AccountRow? Selected
    {
        get => selected; set
        {
            selected = value;
            Username = value?.Username ?? "";
            Notes = value?.Notes ?? "";
            ProcessId = value?.ProcessId?.ToString() ?? "";
            ClearSecrets();
            Changed();
            Changed(nameof(HasSelection));
            Changed(nameof(EditorTitle));
            Changed(nameof(SessionLabel));
            Changed(nameof(LaunchStatus));
        }
    }
    public bool HasSelection => Selected is not null;
    public string EditorTitle => Selected is null ? "New account" : Selected.Username;
    private string placeId = "", jobId = "";
    public string PlaceId
    {
        get => placeId; set
        {
            placeId = value;
            Changed();
        }
    }
    public string JobId
    {
        get => jobId; set
        {
            jobId = value;
            Changed();
        }
    }
    public string LaunchStatus
    {
        get
        {
            if (Selected is null)
                return "Select an account to launch";
            var state = instances?.Instances.FirstOrDefault(i => i.AccountId == Selected.Id);
            return state?.Detail ?? state?.State.ToString() ?? Selected.StateLabel;
        }
    }
    private async Task LaunchSelectedAsync()
    {
        if (Selected is null || instances is null)
        {
            Status = "Select an account to launch.";
            return;
        }
        if (!long.TryParse(PlaceId, out var place) || place <= 0 || (!string.IsNullOrWhiteSpace(JobId) && !Guid.TryParse(JobId, out _)))
        {
            Status = "Enter a positive Place ID and an optional valid Job ID.";
            return;
        }
        await instances.EnqueueAsync(new(Selected.Id, place, string.IsNullOrWhiteSpace(JobId) ? null : JobId.Trim()), lifetime.Token);
        Status = "Launch queued. Session and process identity will be verified.";
        Changed(nameof(LaunchStatus));
    }
    public string SessionLabel => Selected?.StateLabel ?? "Save a local record, or add an account through the browser.";
    public string Username
    {
        get => username; set
        {
            username = value;
            Changed();
        }
    }
    public string Notes
    {
        get => notes; set
        {
            notes = value;
            Changed();
        }
    }
    public string ProcessId
    {
        get => processId; set
        {
            processId = value;
            Changed();
        }
    }
    public string Search
    {
        get => search; set
        {
            search = value;
            Changed();
            Filter();
        }
    }
    public string Status
    {
        get => status; set
        {
            status = value;
            Changed();
        }
    }
    public bool IsBusy
    {
        get => busy; private set
        {
            busy = value;
            Changed();
            Changed(nameof(CanEdit));
        }
    }
    public bool CanEdit => !IsBusy;
    public bool DarkTheme
    {
        get => preferences.DarkTheme; set
        {
            preferences.DarkTheme = value;
            Changed();
            SavePreferences();
            ThemeManager.Apply(preferences);
        }
    }
    public string Accent
    {
        get => preferences.Accent; set
        {
            preferences.Accent = value;
            Changed();
            SavePreferences();
            ThemeManager.Apply(preferences);
        }
    }
    public bool AnimationsEnabled
    {
        get => preferences.AnimationsEnabled; set
        {
            preferences.AnimationsEnabled = value;
            Changed();
            Changed(nameof(EffectiveMotion));
            SavePreferences();
        }
    }
    public bool EffectiveMotion => AnimationsEnabled && System.Windows.SystemParameters.ClientAreaAnimation;
    public bool CompactRows
    {
        get => preferences.CompactRows; set
        {
            preferences.CompactRows = value;
            Changed();
            Changed(nameof(TableRowHeight));
            SavePreferences();
        }
    }
    public double TableRowHeight => CompactRows ? 32 : 42;
    public bool SettingsOpen
    {
        get => settingsOpen; set
        {
            settingsOpen = value;
            Changed();
        }
    }
    public bool ShowPasswords
    {
        get => preferences.ShowPasswords; set
        {
            preferences.ShowPasswords = value;
            Changed();
            SavePreferences();
            if (!value)
                foreach (var row in all)
                    row.PasswordText = row.PasswordText == "—" ? "—" : "••••••••";
            _ = RefreshPasswordsAsync();
        }
    }
    public bool AutoCheckSessions
    {
        get => preferences.AutoCheckSessions; set
        {
            preferences.AutoCheckSessions = value;
            Changed();
            SavePreferences();
        }
    }
    public bool AutoRenewSessions
    {
        get => preferences.AutoRenewSessions; set
        {
            preferences.AutoRenewSessions = value;
            Changed();
            SavePreferences();
        }
    }
    public string BrowserPath
    {
        get => preferences.BrowserPath; set
        {
            preferences.BrowserPath = value;
            Changed();
            SavePreferences();
        }
    }
    private string preferencesStatus = "Saved on this device";
    public string PreferencesStatus => preferencesStatus;
    private void SavePreferences()
    {
        try
        {
            preferences.Save();
            preferencesStatus = "Saved on this device";
            Changed(nameof(PreferencesStatus));
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Status = "Could not save preferences."; preferencesStatus = "Could not save"; Changed(nameof(PreferencesStatus)); }
    }
    public int Count => Accounts.Count;
    public async Task LoadAsync()
    {
        var id = Selected?.Id;
        all = (await repository.GetAllAsync()).Select(a => new AccountRow(a)).ToList();
        await RefreshPasswordsAsync();
        Filter();
        Selected = Accounts.FirstOrDefault(a => a.Id == id);
        RefreshActivity();
    }
    public async Task RefreshPasswordsAsync()
    {
        try
        {
            foreach (var row in all.ToList())
            {
                var has = await credentials.HasPasswordAsync(row.Id);
                var clear = ShowPasswords && has ? await credentials.ReadPasswordAsync(row.Id) : null;
                row.PasswordText = !has ? "—" : ShowPasswords ? clear ?? "—" : "••••••••";
            }
        }
        catch (System.Security.Cryptography.CryptographicException) { Status = "Passwords cannot be decrypted by this Windows user."; }
        catch (Exception ex) { logger.LogWarning("Credential display failed ({ErrorType})", ex.GetType().Name); Status = "Could not read saved passwords."; }
    }
    private string filterMode = "All";
    public string FilterMode
    {
        get => filterMode; set
        {
            filterMode = value;
            Changed();
            Filter();
        }
    }
    public int TotalCount => all.Count;
    public int RunningCount => all.Count(a => a.IsRunning);
    public int AttentionCount => all.Count(a => a.IsInvalid);
    private void Filter()
    {
        var matches = all.Where(a => (a.Username.Contains(search, StringComparison.OrdinalIgnoreCase) || a.Notes.Contains(search, StringComparison.OrdinalIgnoreCase)) && (FilterMode != "Running" || a.IsRunning) && (FilterMode != "Attention" || a.IsInvalid)).ToList();
        if (!Accounts.SequenceEqual(matches))
        {
            Accounts.Clear();
            foreach (var row in matches)
                Accounts.Add(row);
            if (Selected is not null && !matches.Contains(Selected))
                Selected = null;
        }
        Changed(nameof(Count));
        Changed(nameof(TotalCount));
        Changed(nameof(RunningCount));
        Changed(nameof(AttentionCount));
    }
    public void RefreshActivity()
    {
        foreach (var row in all)
            row.ProcessId = tracker.GetLiveProcessId(row.Id);
        Filter();
        Changed(nameof(SessionLabel));
        Changed(nameof(LaunchStatus));
    }
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || Username.Trim().Length > 64)
        {
            Status = "Enter a Roblox username (up to 64 characters).";
            return;
        }
        if (Selected is not null && !string.Equals(Username.Trim(), Selected.Username, StringComparison.OrdinalIgnoreCase))
        {
            Status = "Use Add account to sign in as a different Roblox user.";
            return;
        }
        var account = new Account(Selected?.Id ?? Guid.NewGuid().ToString("N"), Username.Trim(), Selected?.Data.DisplayName ?? "", Selected?.Data.GroupId, Notes, Selected?.Data.SessionState ?? SessionState.Unknown, Selected?.Data.RobloxUserId);
        await repository.SaveAsync(account);
        if (PasswordDraft.Length > 0)
            await credentials.SavePasswordAsync(account.Id, PasswordDraft);
        ClearSecrets();
        await LoadAsync();
        Selected = Accounts.FirstOrDefault(a => a.Id == account.Id);
        Status = "Account saved locally.";
    }
    private async Task DeleteAsync()
    {
        if (Selected is null)
            return;
        if (System.Windows.MessageBox.Show("Delete this account, its password and saved session?", "Hive", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes)
            return;
        var id = Selected.Id;
        await repository.DeleteAsync(id);
        tracker.Detach(id);
        Selected = null;
        await LoadAsync();
        Status = "Account removed.";
    }
    private bool checking;
    public async Task CheckSessionsAsync(bool manual = false, CancellationToken ct = default)
    {
        if (checking || (!manual && !AutoCheckSessions))
            return;
        checking = true;
        try
        {
            foreach (var row in all.ToList())
            {
                ct.ThrowIfCancellationRequested();
                var result = await sessions.CheckAsync(row.Id, ct, renewIfDue: AutoRenewSessions);
                if (result.Confirmed)
                    row.UpdateState(result.State);
                if (manual || result.Confirmed)
                    Status = $"{row.Username}: {result.Message}";
            }
            Filter();
            Changed(nameof(SessionLabel));
        }
        finally { checking = false; }
    }
    private async Task ImportCookieAsync()
    {
        if (Selected is null)
        {
            Status = "Save an account first, or use Add account.";
            return;
        }
        if (string.IsNullOrWhiteSpace(CookieDraft))
        {
            Status = "Paste a session cookie, or sign in through the browser.";
            return;
        }
        var value = CookieDraft.Trim();
        CookieDraft = "";
        ClearSensitiveInputs?.Invoke();
        var account = Selected;
        var result = await sessions.ImportAsync(account.Data, new([new(".ROBLOSECURITY", value, ".roblox.com", "/", null, true, true)], DateTimeOffset.UtcNow));
        if (result.Confirmed)
            account.UpdateState(result.State);
        Status = result.Message;
        Changed(nameof(SessionLabel));
    }
    public Task AddCredentialsAsync(IReadOnlyList<LoginCredential> entries) => RunAsync(async () =>
    {
        foreach (var entry in entries)
        {
            lifetime.Token.ThrowIfCancellationRequested();
            await AuthenticateAsync(null, entry);
        }
    });
    public Task AddCookiesAsync(IReadOnlyList<string> tokens) => RunAsync(async () =>
    {
        if (onboarding is null)
            return;
        loginCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var processed = 0;
        var rejected = 0;
        try
        {
            foreach (var token in tokens)
            {
                loginCancellation.Token.ThrowIfCancellationRequested();
                try
                {
                    var added = await onboarding.AddAsync(new([new(".ROBLOSECURITY", token, ".roblox.com", "/", null, true, true)], DateTimeOffset.UtcNow), loginCancellation.Token);
                    if (added is null)
                        rejected++;
                    else
                        processed++;
                }
                catch (System.Net.Http.HttpRequestException) { rejected++; }
                catch (System.Net.CookieException) { rejected++; }
            }
            Search = "";
            FilterMode = "All";
            await LoadAsync();
            Status = $"Saved {processed} verified sessions; {rejected} cookies could not be verified.";
        }
        finally { loginCancellation.Dispose(); loginCancellation = null; }
    });
    public Task AddCustomAsync(Uri url, string script) => RunAsync(() => AuthenticateAsync(null, startUrl: url, script: script));
    private Task SignInAsync() => AuthenticateAsync(Selected);
    private async Task AuthenticateAsync(AccountRow? expected, LoginCredential? login = null, Uri? startUrl = null, string? script = null)
    {
        if (onboarding is null)
        {
            Status = "Browser onboarding is unavailable.";
            return;
        }
        loginCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        loginCancellation.CancelAfter(TimeSpan.FromMinutes(5));
        var ct = loginCancellation.Token;
        try
        {
            Status = expected is null ? "Opening a private sign-in window. Complete Roblox sign-in to add your account." : $"Sign in as {expected.Username} in the browser. Use Cancel to stop.";
            var profileId = expected?.Id ?? Guid.NewGuid().ToString("N");
            var root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hive", "BrowserProfiles", profileId);
            await using var session = await browser.OpenAsync(new(profileId, "Sign in", root), ct);
            await session.NavigateAsync(startUrl ?? new Uri("https://www.roblox.com/login"), ct);
            if (login is not null)
                await session.FillLoginAsync(login.Username, login.Password, ct);
            if (!string.IsNullOrWhiteSpace(script))
                await session.ExecuteScriptAsync(script, ct);
            while (session.IsOpen)
            {
                await Task.Delay(TimeSpan.FromSeconds(4), ct);
                var cookies = await session.ReadCookiesAsync(ct);
                if (!cookies.Any(c => c.Name == ".ROBLOSECURITY"))
                    continue;
                var saved = new AccountSession(cookies, DateTimeOffset.UtcNow);
                if (expected is null)
                {
                    var added = await onboarding.AddAsync(saved, ct, login?.Username);
                    if (added is null)
                        continue;
                    if (login is not null)
                        await credentials.SavePasswordAsync(added.Id, login.Password, ct);
                    Search = "";
                    FilterMode = "All";
                    await LoadAsync();
                    Selected = Accounts.FirstOrDefault(a => a.Id == added.Id);
                    Status = $"Added {added.Username}. Session encrypted locally.";
                    return;
                }
                var result = await sessions.ImportAsync(expected.Data, saved, ct);
                if (result.Confirmed && result.State == SessionState.Valid)
                {
                    expected.UpdateState(result.State);
                    Status = "Signed in. Your session is encrypted and saved.";
                    Filter();
                    Changed(nameof(SessionLabel));
                    return;
                }
                Status = result.Message;
            }
            Status = "Sign-in window closed.";
        }
        finally { loginCancellation.Dispose(); loginCancellation = null; }
    }
    private async Task OpenBrowserAsync()
    {
        if (Selected is null || sessionStore is null)
        {
            Status = "Select an account with a saved session.";
            return;
        }
        var account = Selected;
        if (openBrowsers.TryGetValue(account.Id, out var active) && active.IsOpen)
        {
            Status = "This account already has an open browser window.";
            return;
        }
        var check = await sessions.CheckAsync(account.Id, lifetime.Token, renewIfDue: false);
        if (check.Confirmed)
            account.UpdateState(check.State);
        if (check.State != SessionState.Valid)
        {
            Status = check.Message;
            return;
        }
        var saved = await sessionStore.ReadAsync(account.Id, lifetime.Token);
        if (saved is null)
        {
            Status = "Sign in to save a session first.";
            return;
        }
        var root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hive", "BrowserProfiles", account.Id + "-web");
        var session = await browser.OpenAsync(new(account.Id, account.Username, root), lifetime.Token);
        try
        {
            await session.RestoreCookiesAsync(saved.Cookies, lifetime.Token);
            await session.NavigateAsync(new Uri("https://www.roblox.com/home"), lifetime.Token);
        }
        catch { await session.DisposeAsync(); throw; }
        openBrowsers[account.Id] = session;
        _ = WatchBrowserAsync(account, session, saved);
        Status = $"Opened {account.Username} in a private browser.";
    }
    private async Task WatchBrowserAsync(AccountRow account, IBrowserSession session, AccountSession saved)
    {
        var token = saved.Cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY")?.Value;
        try
        {
            while (session.IsOpen && !lifetime.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), lifetime.Token);
                var cookies = await session.ReadCookiesAsync(lifetime.Token);
                var updated = cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY")?.Value;
                if (updated is null || updated == token)
                    continue;
                var result = await sessions.ImportAsync(account.Data, new(cookies, DateTimeOffset.UtcNow), lifetime.Token);
                if (result.Confirmed)
                {
                    account.UpdateState(result.State);
                    token = updated;
                    Changed(nameof(SessionLabel));
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { logger.LogWarning("Browser monitoring stopped ({ErrorType})", ex.GetType().Name); }
        finally { if (openBrowsers.TryGetValue(account.Id, out var current) && ReferenceEquals(current, session)) openBrowsers.TryRemove(account.Id, out _); try { await session.DisposeAsync(); } catch (Exception ex) { logger.LogWarning("Browser close failed ({ErrorType})", ex.GetType().Name); } }
    }
    public Task ImportRamAsync(Hive.Storage.RamImportService importer, IReadOnlyList<Hive.Storage.RamImportItem> items) => RunAsync(async () => { var summary = await importer.ImportAsync(items, lifetime.Token); Search = ""; FilterMode = "All"; await LoadAsync(); Status = $"Imported {summary.Added} accounts; skipped {summary.Skipped} existing accounts. Check sessions before use."; });
    public void CancelPending()
    {
        loginCancellation?.Cancel();
        lifetime.Cancel();
    }
    public async Task ShutdownAsync()
    {
        CancelPending();
        if (activeOperation is not null)
        {
            try
            {
                await activeOperation.WaitAsync(TimeSpan.FromSeconds(20));
            }
            catch (Exception) { }
        }
        foreach (var session in openBrowsers.Values.ToList())
        {
            try
            {
                await session.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (Exception) { }
        }
        openBrowsers.Clear();
        if (instances is IAsyncDisposable disposable)
            await disposable.DisposeAsync();
    }

}
public sealed class AsyncCommand(Func<Task> execute) : ICommand
{
    private bool running; public bool CanExecute(object? parameter) => !running;
    public async void Execute(object? parameter) => await ExecuteAsync();
    public async Task ExecuteAsync()
    {
        if (running)
            return;
        running = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try
        {
            await execute();
        }
        finally { running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? CanExecuteChanged;
}
