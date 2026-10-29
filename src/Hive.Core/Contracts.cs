namespace Hive.Core;

public interface IAccountRepository
{
    Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(Account account, CancellationToken cancellationToken = default);
    Task SetSessionStateAsync(string id, SessionState state, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
public interface ISessionStore
{
    Task SaveAsync(string accountId, AccountSession session, CancellationToken cancellationToken = default);
    Task<AccountSession?> ReadAsync(string accountId, CancellationToken cancellationToken = default);
}
public interface ISecretProtector
{
    byte[] Protect(byte[] data); byte[] Unprotect(byte[] data);
}
public interface IBrowserProvider
{
    Task<IBrowserSession> OpenAsync(BrowserProfile profile, CancellationToken cancellationToken = default);
}
public interface IBrowserSession : IAsyncDisposable
{
    bool IsOpen
    {
        get;
    }
    Task FillLoginAsync(string username, string password, CancellationToken ct = default) => throw new NotSupportedException();
    Task ExecuteScriptAsync(string script, CancellationToken ct = default) => throw new NotSupportedException();
    Task RestoreCookiesAsync(IReadOnlyList<SessionCookie> cookies, CancellationToken cancellationToken = default); Task NavigateAsync(Uri uri, CancellationToken cancellationToken = default); Task<IReadOnlyList<SessionCookie>> ReadCookiesAsync(CancellationToken cancellationToken = default);
}
public interface ISessionManager
{
    Task<SessionState> ValidateAsync(string accountId, CancellationToken cancellationToken = default);
}
public interface IInstanceManager
{
    Task EnqueueAsync(LaunchRequest request, CancellationToken cancellationToken = default); IReadOnlyList<GameInstance> Instances
    {
        get;
    }
}

public interface ICredentialStore
{
    Task SavePasswordAsync(string accountId, string password, CancellationToken ct = default);
    Task<string?> ReadPasswordAsync(string accountId, CancellationToken ct = default);
    Task<bool> HasPasswordAsync(string accountId, CancellationToken ct = default);
}
public interface IAccountActivityTracker
{
    void Attach(string accountId, int processId);
    int? GetLiveProcessId(string accountId);
    void Detach(string accountId);
}
public sealed record SessionCheck(SessionState State, bool Confirmed, string Message);

public sealed class ProcessAssignmentException(string message) : Exception(message);

public interface IGameWindowPreferences
{
    bool CompactGameWindows
    {
        get;
    }
}

public interface IAccountGroupRepository
{
    Task<IReadOnlyList<AccountGroup>> GetAllAsync(CancellationToken ct = default);
    Task SaveAsync(AccountGroup group, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
}

public sealed record NetworkEvent(DateTimeOffset At, string Service, string Operation, int Status, long Milliseconds);
public interface INetworkLog
{
    void Record(NetworkEvent entry);
    IReadOnlyList<NetworkEvent> Snapshot();
}
