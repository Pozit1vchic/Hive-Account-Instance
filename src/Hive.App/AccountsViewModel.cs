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
    public bool IsFavorite
    {
        get; set;
    }
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
    private readonly IAccountGroupRepository? groupRepository;
    private readonly IBrowserProfileRepository? profileRepository;
    private readonly string ProfileRoot;
    private readonly IMultiInstanceCoordinator? multiInstance;
    private readonly IAccountRepository repository; private readonly ICredentialStore credentials;
    private readonly SessionManager sessions; private readonly IAccountActivityTracker tracker; private readonly IBrowserProvider browser;
    private readonly AccountOnboardingService? onboarding; private readonly ISessionStore? sessionStore;
    private readonly ConcurrentDictionary<string, IBrowserSession> openBrowsers = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly AppPreferences preferences; private readonly ILogger<AccountsViewModel> logger;
    private List<AccountRow> all = []; private AccountRow? selected;
    private string username = "", alias = "", notes = "", search = "", status = "Ready", processId = ""; private bool busy, settingsOpen;
    private Task? activeOperation;
    private CancellationTokenSource? loginCancellation;
    public AccountsViewModel(IAccountRepository repository, ICredentialStore credentials, SessionManager sessions, IAccountActivityTracker tracker, IBrowserProvider browser, AppPreferences preferences, ILogger<AccountsViewModel> logger, AccountOnboardingService? onboarding = null, ISessionStore? sessionStore = null, IInstanceManager? instances = null, IAccountGroupRepository? groupRepository = null, IBrowserProfileRepository? profileRepository = null, IMultiInstanceCoordinator? multiInstance = null, string? profileDirectory = null)
    {
        ProfileRoot = System.IO.Path.GetFullPath(profileDirectory ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hive", "BrowserProfiles"));
        this.profileRepository = profileRepository;
        this.multiInstance = multiInstance;
        this.groupRepository = groupRepository;
        this.instances = instances;
        this.onboarding = onboarding;
        this.sessionStore = sessionStore;
        this.repository = repository;
        this.credentials = credentials;
        this.sessions = sessions;
        this.tracker = tracker;
        this.browser = browser;
        this.preferences = preferences;
        placeId = preferences.PlaceId;
        jobId = preferences.JobId;
        this.logger = logger;
        SaveCommand = Command(SaveAsync);
        DeleteCommand = Command(DeleteAsync);
        NewCommand = Command(() => { Selected = null; Username = ""; Notes = ""; ClearSecrets(); Status = "New account"; return Task.CompletedTask; });
        LaunchCommand = Command(LaunchSelectedAsync);
        AddBrowserCommand = Command(() => AuthenticateAsync(null));
        OpenBrowserCommand = Command(OpenBrowserAsync);
        AllFilterCommand = new AsyncCommand(() => { SelectedGroup = null; FilterMode = "All"; return Task.CompletedTask; });
        RunningFilterCommand = new AsyncCommand(() => { FilterMode = "Running"; return Task.CompletedTask; });
        AttentionFilterCommand = new AsyncCommand(() => { FilterMode = "Attention"; return Task.CompletedTask; });
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
        LastAddedCount = 0;
        IsBusy = true;
        try
        {
            activeOperation = action();
            await activeOperation;
        }
        catch (OperationCanceledException)
        {
            if (LastAddedCount > 0) { SelectedGroup = null; Search = ""; FilterMode = "All"; await LoadAsync(); Status = $"Saved {LastAddedCount} accounts; remaining addition cancelled."; }
            else Status = "Operation cancelled.";
        }
        catch (ProcessAssignmentException ex) { Status = ex.Message; }
        catch (BrowserRateLimitException ex) { Status = ex.Message; }
        catch (RobloxLaunchException ex) { Status = ex.Message; }
        catch (Exception ex) when (HasBrowserConfigurationError(ex)) { Status = "Windows could not start Chromium (14001). Restart Hive to retry its managed browser."; }
        catch (Exception ex) { logger.LogWarning("Account operation failed ({ErrorType})", ex.GetType().Name); Status = ex is System.IO.FileNotFoundException ? "The managed browser is unavailable. Restart Hive to retry setup." : "Could not complete the operation. Check storage access and your connection."; }
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
            Alias = value?.Data.DisplayName ?? "";
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
            if (placeId == value)
                return;
            placeId = value;
            preferences.PlaceId = value;
            Changed();
            SavePreferences();
        }
    }
    public string JobId
    {
        get => jobId; set
        {
            if (jobId == value)
                return;
            jobId = value;
            preferences.JobId = value;
            Changed();
            SavePreferences();
        }
    }
    public string LaunchStatus
    {
        get
        {
            if (Selected is null)
                return "Select an account to launch";
            var state = instances?.Instances.FirstOrDefault(i => i.AccountId == Selected.Id);
            return state is null ? Selected.StateLabel : state.State switch { InstanceState.Backoff => $"Rejoining · attempt {state.RejoinAttempts}", InstanceState.AwaitingProcess => "Waiting for process", InstanceState.Failed => "Launch failed", _ => state.State.ToString() };
        }
    }
    private async Task LaunchSelectedAsync()
    {
        if (Selected is null || instances is null)
        {
            Status = "Select an account to launch.";
            return;
        }
        LaunchRequest request;
        try { request = BuildLaunchRequest(Selected.Id); } catch (FormatException error) { Status = error.Message; return; }
        await instances.EnqueueAsync(request, lifetime.Token);
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
    public string Alias { get => alias; set { alias = value; Changed(); } }
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
            if (preferences.DarkTheme == value)
                return;
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
            if (preferences.Accent == value)
                return;
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
            if (preferences.AnimationsEnabled == value)
                return;
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
            if (preferences.CompactRows == value)
                return;
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
            if (preferences.ShowPasswords == value)
                return;
            preferences.ShowPasswords = value;
            Changed();
            SavePreferences();
            if (!value)
                foreach (var row in all)
                    row.PasswordText = row.PasswordText == "—" ? "—" : "••••••••";
            _ = RefreshPasswordsAsync();
        }
    }
    public LaunchKind LaunchMode
    {
        get => preferences.LaunchKind;
        set { if (preferences.LaunchKind == value) return; preferences.LaunchKind = value; SavePreferences(); Changed(); Changed(nameof(LaunchModeLabel)); Changed(nameof(TargetLabel)); Changed(nameof(ArgumentLabel)); Changed(nameof(LaunchTargetId)); Changed(nameof(LaunchArgument)); Changed(nameof(HasLaunchArgument)); }
    }
    public string LaunchModeLabel => LaunchMode switch { LaunchKind.Server => "Server", LaunchKind.PrivateServer => "Private server", LaunchKind.FollowUser => "Follow user", _ => "Place" };
    public string TargetLabel => LaunchMode == LaunchKind.FollowUser ? "User ID" : "Place ID";
    public string ArgumentLabel => LaunchMode == LaunchKind.PrivateServer ? "Invite URL / access code" : "Job ID (optional)";
    public bool HasLaunchArgument => LaunchMode != LaunchKind.FollowUser;
    public string LaunchTargetId { get => LaunchMode == LaunchKind.FollowUser ? preferences.FollowUserId : PlaceId; set { if (LaunchMode == LaunchKind.FollowUser) { preferences.FollowUserId = value; SavePreferences(); } else PlaceId = value; Changed(); } }
    public string LaunchArgument { get => LaunchMode == LaunchKind.PrivateServer ? preferences.PrivateInvite : JobId; set { if (LaunchMode == LaunchKind.PrivateServer) { preferences.PrivateInvite = value; SavePreferences(); } else JobId = value; Changed(); } }
    private LaunchRequest BuildLaunchRequest(string id) => LaunchTarget.Parse(id, LaunchMode, LaunchTargetId, LaunchArgument);
    public void LoadSavedPlace(SavedPlace saved) { LaunchMode = saved.Kind; LaunchTargetId = saved.PlaceId; LaunchArgument = saved.Kind == LaunchKind.PrivateServer ? Hive.Infrastructure.ProtectedText.Read(saved.ProtectedArgument) : saved.JobId; }
    public int MaxParallelLaunches { get => preferences.MaxParallelLaunches; set { preferences.MaxParallelLaunches = Math.Clamp(value, 1, 4); SavePreferences(); Changed(); } }
    public int ParallelSignIns { get => Math.Clamp(preferences.ParallelSignIns, 1, 4); set { preferences.ParallelSignIns = Math.Clamp(value, 1, 4); SavePreferences(); Changed(); } }
    public double LaunchDelaySeconds { get => preferences.LaunchDelaySeconds; set { preferences.LaunchDelaySeconds = Math.Clamp(value, 0, 30); SavePreferences(); Changed(); } }
    public bool AutoRejoin { get => preferences.AutoRejoin; set { preferences.AutoRejoin = value; if (!value) foreach (var item in Launches.Where(x => x.State == InstanceState.Backoff)) instances?.Cancel(item.AccountId); SavePreferences(); Changed(); } }
    public int MaxRejoinAttempts { get => preferences.MaxRejoinAttempts; set { preferences.MaxRejoinAttempts = Math.Clamp(value, 1, 10); SavePreferences(); Changed(); } }
    public double RejoinDelaySeconds { get => preferences.RejoinDelaySeconds; set { preferences.RejoinDelaySeconds = Math.Clamp(value, 2, 60); SavePreferences(); Changed(); } }
    public bool MultiInstance
    {
        get => preferences.MultiInstance;
        set
        {
            if (preferences.MultiInstance == value) return;
            try { if (value) multiInstance?.Prepare(); else multiInstance?.Dispose(); preferences.MultiInstance = value; SavePreferences(); }
            catch (RobloxLaunchException error) { preferencesStatus = error.Message; Changed(nameof(PreferencesStatus)); }
            Changed();
        }
    }
    public IReadOnlyList<GameInstance> Launches => instances?.Instances ?? [];
    public string AccountName(string id) => all.FirstOrDefault(a => a.Id == id)?.Username ?? "Removed account";
    public void CancelLaunch(string id) => instances?.Cancel(id);
    public int LaunchCount => Launches.Count(x => x.State is InstanceState.Queued or InstanceState.Starting or InstanceState.Backoff);
    public int GameWindowWidth { get => preferences.GameWindowWidth; set { preferences.GameWindowWidth = Math.Clamp(value, 320, 7680); SavePreferences(); Changed(); } }
    public int GameWindowHeight { get => preferences.GameWindowHeight; set { preferences.GameWindowHeight = Math.Clamp(value, 240, 4320); SavePreferences(); Changed(); } }
    public bool CompactGameWindows
    {
        get => preferences.CompactGameWindows;
        set
        {
            if (preferences.CompactGameWindows == value)
                return;
            preferences.CompactGameWindows = value;
            Changed();
            SavePreferences();
        }
    }
    public bool AutoCheckSessions
    {
        get => preferences.AutoCheckSessions; set
        {
            if (preferences.AutoCheckSessions == value)
                return;
            preferences.AutoCheckSessions = value;
            Changed();
            SavePreferences();
        }
    }
    public bool AutoRenewSessions
    {
        get => preferences.AutoRenewSessions; set
        {
            if (preferences.AutoRenewSessions == value)
                return;
            preferences.AutoRenewSessions = value;
            Changed();
            SavePreferences();
        }
    }
    public string BrowserPath
    {
        get => preferences.BrowserPath; set
        {
            if (preferences.BrowserPath == value)
                return;
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
        all = (await repository.GetAllAsync()).Select(a => new AccountRow(a) { IsFavorite = preferences.FavoriteAccounts.Contains(a.Id) }).ToList();
        if (groupRepository is not null)
        {
            Groups.Clear();
            foreach (var group in await groupRepository.GetAllAsync())
                Groups.Add(group);
        }
        await RefreshPasswordsAsync();
        await ReloadProfilesAsync();
        Filter();
        Selected = Accounts.FirstOrDefault(a => a.Id == id) ?? Accounts.FirstOrDefault();
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
    public Task CloseGamesAsync(IReadOnlyList<string> ids) => RunAsync(() =>
    {
        if (ids.Count == 0)
            return Task.CompletedTask;
        if (System.Windows.MessageBox.Show("Close the games linked to the selected accounts?", "Hive", System.Windows.MessageBoxButton.YesNo) != System.Windows.MessageBoxResult.Yes)
            return Task.CompletedTask;
        var closed = 0;
        foreach (var id in ids)
        {
            instances?.Cancel(id);
            if (tracker.GetLiveProcessId(id) is not int pid)
                continue;
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                if (process.CloseMainWindow())
                    closed++;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }
        Status = $"Requested closure of {closed} games.";
        return Task.CompletedTask;
    });
    public ObservableCollection<AccountGroup> Groups { get; } = [];
    private AccountGroup? selectedGroup;
    public AccountGroup? SelectedGroup
    {
        get => selectedGroup; set
        {
            selectedGroup = value;
            Changed();
            Changed(nameof(GroupLabel));
            Filter();
        }
    }
    public string GroupLabel => SelectedGroup?.Name ?? "Groups";
    public IReadOnlyList<SavedPlace> SavedPlaces => preferences.SavedPlaces;
    public void SaveCurrentPlace()
    {
        LaunchRequest request;
        try { request = BuildLaunchRequest("saved-target"); } catch (FormatException error) { Status = error.Message; return; }
        var target = LaunchMode == LaunchKind.FollowUser ? request.FollowUserId!.Value.ToString() : request.PlaceId > 0 ? request.PlaceId.ToString() : LaunchTargetId.Trim();
        var saved = new SavedPlace(target, LaunchMode == LaunchKind.PrivateServer ? "" : LaunchArgument.Trim(), LaunchMode, LaunchMode == LaunchKind.PrivateServer ? preferences.ProtectedPrivateInvite : null);
        if (!preferences.SavedPlaces.Any(p => p.PlaceId == saved.PlaceId && p.Kind == saved.Kind && (saved.Kind == LaunchKind.PrivateServer ? Hive.Infrastructure.ProtectedText.Read(p.ProtectedArgument) == LaunchArgument : p.JobId == saved.JobId)))
            preferences.SavedPlaces.Add(saved);
        SavePreferences();
        Status = "Place saved.";
    }
    public Task CreateGroupAsync(string name) => RunAsync(async () =>
    {
        if (groupRepository is null)
            return;
        if (Groups.Any(g => g.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            Status = "A group with this name already exists.";
            return;
        }
        var group = new AccountGroup(Guid.NewGuid().ToString("N"), name.Trim());
        await groupRepository.SaveAsync(group);
        await LoadAsync();
        SelectedGroup = group;
    });
    public Task MoveToGroupAsync(string? groupId, IReadOnlyList<string> ids) => RunAsync(async () =>
    {
        foreach (var row in all.Where(a => ids.Contains(a.Id)).ToList())
            await repository.SaveAsync(row.Data with
            {
                GroupId = groupId
            });
        await LoadAsync();
        Status = "Group updated.";
    });
    public Task DeleteSelectedGroupAsync() => RunAsync(async () =>
    {
        if (SelectedGroup is null || groupRepository is null)
            return;
        var id = SelectedGroup.Id;
        SelectedGroup = null;
        await groupRepository.DeleteAsync(id);
        await LoadAsync();
    });
    public async Task ToggleFavoriteAsync()
    {
        if (Selected is null)
            return;
        if (!preferences.FavoriteAccounts.Remove(Selected.Id))
            preferences.FavoriteAccounts.Add(Selected.Id);
        SavePreferences();
        await LoadAsync();
    }
    public Task LaunchAccountsAsync(IReadOnlyList<string> ids) => RunAsync(async () =>
    {
        if (instances is null || ids.Count == 0)
        {
            Status = "Select accounts to launch.";
            return;
        }
        try { _ = BuildLaunchRequest(ids[0]); } catch (FormatException error) { Status = error.Message; return; }
        var queued = 0;
        foreach (var id in ids.Distinct())
        {
            try
            {
                await instances.EnqueueAsync(BuildLaunchRequest(id), lifetime.Token);
                queued++;
            }
            catch (RobloxLaunchException) { }
        }
        Status = queued > 0 ? $"Queued {queued} accounts." : "Selected accounts are already running or queued.";
        Changed(nameof(LaunchStatus));
    });
    public int TotalCount => all.Count;
    public IReadOnlyList<ControlAccount> ControlAccounts() => all.Select(row => new ControlAccount(row.Id, row.Username, row.ProcessId, row.IsRunning, row.IsInvalid)).ToArray();
    public int RunningCount => all.Count(a => a.IsRunning);
    public int AttentionCount => all.Count(a => a.IsInvalid);
    private void Filter()
    {
        var matches = all.Where(a => (SelectedGroup is null || a.Data.GroupId == SelectedGroup.Id) && (FilterMode != "Favorites" || a.IsFavorite) && (a.Username.Contains(search, StringComparison.OrdinalIgnoreCase) || a.Data.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) || a.Notes.Contains(search, StringComparison.OrdinalIgnoreCase)) && (FilterMode != "Running" || a.IsRunning) && (FilterMode != "Attention" || a.IsInvalid)).ToList();
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
    private GameInstance? lastReportedLaunch;
    public void RefreshActivity()
    {
        foreach (var row in all)
            row.ProcessId = tracker.GetLiveProcessId(row.Id);
        Filter();
        Changed(nameof(SessionLabel));
        Changed(nameof(LaunchStatus));
        Changed(nameof(LaunchCount));
        var current = instances?.Instances.FirstOrDefault(item => item.AccountId == Selected?.Id);
        if (current != lastReportedLaunch && current is { State: InstanceState.Failed or InstanceState.AwaitingProcess, Detail: not null })
            Status = current.Detail;
        lastReportedLaunch = current;
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
        var account = new Account(Selected?.Id ?? Guid.NewGuid().ToString("N"), Username.Trim(), Alias.Trim(), Selected?.Data.GroupId, Notes, Selected?.Data.SessionState ?? SessionState.Unknown, Selected?.Data.RobloxUserId);
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
        instances?.Cancel(id);
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
                if (manual || (!IsBusy && result.Confirmed))
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
    public int LastAddedCount
    {
        get; private set;
    }
    private readonly SemaphoreSlim onboardingSaveGate = new(1, 1);
    private bool optionalSaveIssue;
    public Task AddCredentialsAsync(IReadOnlyList<LoginCredential> entries) => RunAsync(async () =>
    {
        optionalSaveIssue = false;
        using var batch = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        loginCancellation = batch;
        var concurrency = ParallelSignIns;
        using var limit = new SemaphoreSlim(concurrency, concurrency);
        try
        {
            await Task.WhenAll(entries.Select(async entry =>
            {
                await limit.WaitAsync(batch.Token);
                try { await AuthenticateAsync(null, entry, batchToken: batch.Token); }
                catch { await batch.CancelAsync(); throw; }
                finally { limit.Release(); }
            }));
            if (entries.Count > 1) Status = $"Saved {LastAddedCount} of {entries.Count} accounts." + (optionalSaveIssue ? " Some passwords or profiles could not be saved." : "");
        }
        finally { loginCancellation = null; }
    });
    public Task AddCookiesAsync(IReadOnlyList<string> tokens) => RunAsync(async () =>
    {
        if (onboarding is null)
            return;
        loginCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var processed = 0;
        var rejected = 0;
        var paused = false;
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
                    {
                        processed++;
                        LastAddedCount = processed;
                    }
                }
                catch (System.Net.Http.HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests) { paused = true; break; }
                catch (System.Net.Http.HttpRequestException) { rejected++; }
                catch (System.Net.CookieException) { rejected++; }
                await Task.Delay(500, loginCancellation.Token);
            }
            LastAddedCount = processed;
            SelectedGroup = null;
            Search = "";
            FilterMode = "All";
            await LoadAsync();
            Status = paused ? $"Saved {processed} accounts. Roblox rate limit reached; retry the remaining cookies later." : rejected == 0 ? $"Saved {processed} accounts." : $"Saved {processed}; {rejected} cookies were not verified.";
        }
        finally { loginCancellation.Dispose(); loginCancellation = null; }
    });
    public Task AddCustomAsync(Uri url, string script) => RunAsync(() => AuthenticateAsync(null, startUrl: url, script: script));
    private Task SignInAsync() => AuthenticateAsync(Selected);
    private async Task AuthenticateAsync(AccountRow? expected, LoginCredential? login = null, Uri? startUrl = null, string? script = null, CancellationToken? batchToken = null)
    {
        if (onboarding is null)
        {
            Status = "Browser onboarding is unavailable.";
            return;
        }
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(batchToken ?? lifetime.Token);
        if (batchToken is null) loginCancellation = operation;
        operation.CancelAfter(TimeSpan.FromMinutes(5));
        var ct = operation.Token;
        try
        {
            Status = expected is null ? "Preparing the browser (first launch may download Chrome). Complete Roblox sign-in in the window that opens." : $"Sign in as {expected.Username} in the browser. Use Cancel to stop.";
            var knownProfile = expected is not null ? await ProfileForAsync(expected) : null;
            var profileId = knownProfile?.Id ?? Guid.NewGuid().ToString("N");
            var root = knownProfile?.DirectoryPath ?? System.IO.Path.Combine(ProfileRoot, profileId);
            await using var session = await browser.OpenAsync(new(profileId, "Sign in", root), ct);
            if (login is not null)
            {
                await LoginPageLoader.PrepareAsync(session, login, attempt => Status = $"Reloading the sign-in page (attempt {attempt} of 3)...", ct);
                try { await session.SubmitLoginAsync(ct); Status = "Sign-in submitted. Complete verification in the browser if asked."; }
                catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning("Automatic sign-in submission unavailable ({ErrorType})", ex.GetType().Name); Status = "Credentials filled. Click Log In in the browser."; }
            }
            else
            {
                await session.NavigateAsync(startUrl ?? new Uri("https://www.roblox.com/login"), ct);
                await session.AcceptCookieConsentAsync(ct);
            }
            if (!string.IsNullOrWhiteSpace(script))
                await session.ExecuteScriptAsync(script, ct);
            string? observedToken = null;
            var nextCheck = DateTimeOffset.MinValue;
            while (session.IsOpen)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
                await session.AcceptCookieConsentAsync(ct);
                var cookies = await session.ReadCookiesAsync(ct);
                var currentToken = cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY")?.Value;
                if (currentToken is null || (currentToken == observedToken && DateTimeOffset.UtcNow < nextCheck))
                    continue;
                observedToken = currentToken;
                nextCheck = DateTimeOffset.UtcNow.AddSeconds(30);
                var saved = new AccountSession(cookies, DateTimeOffset.UtcNow);
                if (expected is null)
                {
                    await onboardingSaveGate.WaitAsync(ct);
                    try
                    {
                    Account? added;
                    try
                    {
                        added = await onboarding.AddAsync(saved, ct, login?.Username);
                    }
                    catch (System.Net.Http.HttpRequestException) { Status = "Waiting for Roblox verification. Complete it in the browser."; continue; }
                    catch (TaskCanceledException) when (!ct.IsCancellationRequested) { Status = "Roblox is taking longer. Keep the browser open."; continue; }
                    if (added is null)
                        continue;
                    LastAddedCount++;
                    var optionalSaveFailed = false;
                    try
                    {
                        if (profileRepository is not null && !(await profileRepository.GetAllAsync(ct)).Any(p => p.AccountId == added.Id)) await profileRepository.SaveAsync(new(profileId, added.Username, root, added.Id, DateTimeOffset.UtcNow), ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { optionalSaveFailed = true; logger.LogWarning("Optional browser profile save failed ({ErrorType})", ex.GetType().Name); }
                    if (login is not null)
                    {
                        try
                        {
                            await credentials.SavePasswordAsync(added.Id, login.Password, ct);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException) { optionalSaveFailed = true; logger.LogWarning("Optional password save failed ({ErrorType})", ex.GetType().Name); }
                    }
                    SelectedGroup = null;
                    Search = "";
                    FilterMode = "All";
                    await LoadAsync();
                    Selected = Accounts.FirstOrDefault(a => a.Id == added.Id);
                    optionalSaveIssue |= optionalSaveFailed;
                    Status = optionalSaveFailed ? $"Added {added.Username}, but profile or password could not be saved." : $"Added {added.Username}. Session encrypted locally.";
                    return;
                    }
                    finally { onboardingSaveGate.Release(); }
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
        finally { if (batchToken is null) loginCancellation = null; }
    }
    public ObservableCollection<BrowserProfile> BrowserProfiles { get; } = [];
    private async Task ReloadProfilesAsync()
    {
        if (profileRepository is null) return;
        BrowserProfiles.Clear(); foreach (var profile in await profileRepository.GetAllAsync()) BrowserProfiles.Add(profile);
    }
    private string OwnedProfilePath(BrowserProfile profile)
    {
        var path = System.IO.Path.GetFullPath(profile.DirectoryPath);
        if (!path.StartsWith(System.IO.Path.GetFullPath(ProfileRoot) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new System.IO.IOException("Profile is outside Hive's folder.");
        return path;
    }
    private async Task<BrowserProfile> ProfileForAsync(AccountRow account)
    {
        var found = profileRepository is null ? null : (await profileRepository.GetAllAsync()).FirstOrDefault(p => p.AccountId == account.Id);
        if (found is not null) { _ = OwnedProfilePath(found); return found; }
        var id = Guid.NewGuid().ToString("N");
        var profile = new BrowserProfile(id, account.Username, System.IO.Path.Combine(ProfileRoot, id), account.Id);
        if (profileRepository is not null) await profileRepository.SaveAsync(profile);
        return profile;
    }
    public Task CreateProfileAsync(string name) => RunAsync(async () =>
    {
        if (Selected is null || profileRepository is null) { Status = "Select an account first."; return; }
        var id = Guid.NewGuid().ToString("N"); await profileRepository.SaveAsync(new(id, name.Trim(), System.IO.Path.Combine(ProfileRoot, id), Selected.Id)); await ReloadProfilesAsync(); Status = "Profile created.";
    });
    public Task RenameProfileAsync(BrowserProfile profile, string name) => RunAsync(async () =>
    {
        if (profileRepository is null) return;
        await profileRepository.SaveAsync(profile with { Name = name.Trim() }); await ReloadProfilesAsync(); Status = "Profile renamed.";
    });
    public Task RemoveProfileAsync(BrowserProfile profile, bool resetOnly) => RunAsync(async () =>
    {
        if (profileRepository is null) return;
        if (profile.AccountId is not null && openBrowsers.TryGetValue(profile.AccountId, out var browserSession) && browserSession.IsOpen) { Status = "Close this account's browser first."; return; }
        var path = OwnedProfilePath(profile);
        if (System.IO.Directory.Exists(path))
        {
            var archive = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ProfileRoot)!, "ProfileArchive"); System.IO.Directory.CreateDirectory(archive);
            System.IO.Directory.Move(path, System.IO.Path.Combine(archive, Guid.NewGuid().ToString("N")));
        }
        if (resetOnly) await profileRepository.SaveAsync(profile with { LastUsedAt = null }); else await profileRepository.DeleteAsync(profile.Id);
        await ReloadProfilesAsync(); Status = resetOnly ? "Profile cache reset; saved account session kept." : "Profile removed; its cache was archived.";
    });
    public Task OpenProfileAsync(BrowserProfile profile) => RunAsync(async () =>
    {
        SelectedGroup = null; Search = ""; FilterMode = "All"; Selected = all.FirstOrDefault(a => a.Id == profile.AccountId);
        if (Selected is null) { Status = "This profile has no available account."; return; }
        _ = OwnedProfilePath(profile); await OpenBrowserAtAsync(new Uri("https://www.roblox.com/home"), profile);
    });
    public Task OpenAccountSettingsAsync(string section = "info") => RunAsync(() => OpenBrowserAtAsync(new Uri("https://www.roblox.com/my/account" + (section == "info" ? "" : section is "security" or "privacy" ? "#!/" + section : ""))));
    private Task OpenBrowserAsync() => OpenBrowserAtAsync(new Uri("https://www.roblox.com/home"));
    private async Task OpenBrowserAtAsync(Uri address, BrowserProfile? preferred = null)
    {
        if (Selected is null || sessionStore is null)
        {
            Status = "Select an account with a saved session.";
            return;
        }
        var account = Selected;
        if (openBrowsers.TryGetValue(account.Id, out var active) && active.IsOpen)
        {
            if (preferred is not null) { Status = "Close the current account browser before opening another profile."; return; }
            await active.NavigateAsync(address, lifetime.Token); Status = "Browser page opened."; return;
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
        var profile = preferred ?? await ProfileForAsync(account);
        var session = await browser.OpenAsync(profile, lifetime.Token);
        try
        {
            await session.RestoreCookiesAsync(saved.Cookies, lifetime.Token);
            await session.NavigateAsync(address, lifetime.Token);
        }
        catch { await session.DisposeAsync(); throw; }
        openBrowsers[account.Id] = session;
        _ = WatchBrowserAsync(account, session, saved);
        if (profileRepository is not null)
        {
            try { await profileRepository.SaveAsync(profile with { LastUsedAt = DateTimeOffset.UtcNow }); await ReloadProfilesAsync(); }
            catch (Exception error) { logger.LogWarning("Browser profile history save failed ({ErrorType})", error.GetType().Name); }
        }
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
    public Task ImportRamAsync(Hive.Storage.RamImportService importer, IReadOnlyList<Hive.Storage.RamImportItem> items) => RunAsync(async () => { var summary = await importer.ImportAsync(items, lifetime.Token); SelectedGroup = null; Search = ""; FilterMode = "All"; await LoadAsync(); Status = $"Imported {summary.Added} accounts; skipped {summary.Skipped} existing accounts. Check sessions before use."; });
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
public sealed record ControlAccount(string Id, string Username, int? ProcessId, bool Running, bool NeedsSignIn);
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
