namespace Hive.Core;

public enum SessionState
{
    Unknown, Valid, ReauthenticationRequired
}
public enum InstanceState
{
    Queued, Starting, AwaitingProcess, Running, Exited, Failed, Backoff
}
public sealed record Account(string Id, string Username, string DisplayName, string? GroupId, string Notes, SessionState SessionState, long? RobloxUserId = null);
public sealed record AccountGroup(string Id, string Name);
public sealed record BrowserProfile(string Id, string Name, string DirectoryPath, string? AccountId = null, DateTimeOffset? LastUsedAt = null);
public sealed record GameInstance(string AccountId, int? ProcessId, InstanceState State, int RejoinAttempts, string? Detail = null);
public enum LaunchKind { Place, Server, PrivateServer, FollowUser }
public sealed record LaunchRequest(string AccountId, long PlaceId, string? JobId, LaunchKind Kind = LaunchKind.Place, string? AccessCode = null, string? LinkCode = null, long? FollowUserId = null, string? PrivateInviteUrl = null)
{
    public override string ToString() => $"LaunchRequest ({Kind})";
}
public sealed record SessionCookie(string Name, string Value, string Domain, string Path, double? Expires, bool Secure, bool HttpOnly);
public sealed record AccountSession(IReadOnlyList<SessionCookie> Cookies, DateTimeOffset SavedAt, DateTimeOffset? LastRenewedAt = null);

public sealed record RobloxIdentity(long Id, string Username, string DisplayName);

public sealed class RobloxLaunchSpec(string protocolUri, string browserTrackerId)
{
    public string ProtocolUri { get; } = protocolUri; public string BrowserTrackerId { get; } = browserTrackerId;
    public override string ToString() => nameof(RobloxLaunchSpec);
}
public sealed class RobloxLaunchException(string message, bool retryable = false) : Exception(message)
{
    public bool Retryable { get; } = retryable;
}
public interface IRobloxLaunchBroker
{
    Task<RobloxLaunchSpec> CreateLaunchAsync(LaunchRequest request, CancellationToken ct = default);
}
public interface ILaunchDispatcher
{
    Task<int?> DispatchAsync(LaunchRequest request, CancellationToken ct = default);
}
