using System.Net;
using System.Text.Json;
using Hive.Core;
namespace Hive.Roblox;

public sealed class SessionManager(ISessionStore sessions, IAccountRepository accounts, Func<CookieContainer, HttpMessageHandler>? handlerFactory = null, INetworkLog? networkLog = null) : ISessionManager, IRobloxLaunchBroker
{
    private DateTimeOffset checksPausedUntil;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> renewalAttempts = [];
    private static readonly Uri RefreshUri = new("https://auth.roblox.com/v2/session/refresh");
    private static readonly Uri IdentityUri = new("https://users.roblox.com/v1/users/authenticated");
    public async Task<SessionState> ValidateAsync(string accountId, CancellationToken cancellationToken = default) => (await CheckAsync(accountId, cancellationToken)).State;
    public async Task<SessionCheck> CheckAsync(string accountId, CancellationToken ct = default, bool renewIfDue = true)
    {
        await gate.WaitAsync(ct);
        try
        {
            var account = (await accounts.GetAllAsync(ct)).FirstOrDefault(a => a.Id == accountId);
            if (account is null)
                return new(SessionState.Unknown, false, "Account removed");
            AccountSession? saved;
            try
            {
                saved = await sessions.ReadAsync(accountId, ct);
            }
            catch (System.Security.Cryptography.CryptographicException) { return await MarkAsync(accountId, SessionState.ReauthenticationRequired, "Could not decrypt this session. Sign in again.", ct); }
            if (saved is null)
                return new(account.SessionState, false, "No saved session. Sign in through the browser.");
            return await ValidateCoreAsync(account, saved, true, ct, renewIfDue);
        }
        catch (HttpRequestException) { return new(SessionState.Unknown, false, "Cannot reach Roblox. Account status has not changed."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return new(SessionState.Unknown, false, "Roblox timed out. Retrying later."); }
        finally { gate.Release(); }
    }
    public async Task<SessionCheck> ImportAsync(Account account, AccountSession session, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            return await ValidateCoreAsync(account, session, false, ct, false);
        }
        finally { gate.Release(); }
    }
    private async Task<SessionCheck> ValidateCoreAsync(Account account, AccountSession session, bool markInvalid, CancellationToken ct, bool renewIfDue)
    {
        if (DateTimeOffset.UtcNow < checksPausedUntil)
            return new(account.SessionState, false, "Roblox limited checks. Retrying later.");
        var token = session.Cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY" && (c.Domain == ".roblox.com" || c.Domain == "roblox.com"));
        if (token is null || (token.Expires is > 0 && token.Expires <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()))
            return markInvalid ? await MarkAsync(account.Id, SessionState.ReauthenticationRequired, "Session expired. Sign in again.", ct) : new(SessionState.Unknown, false, "Waiting for a Roblox session in the browser.");
        var jar = new CookieContainer();
        var authenticationCookie = new Cookie(token.Name, token.Value, "/", ".roblox.com") { Secure = true, HttpOnly = true };
        if (token.Expires is > 0)
            authenticationCookie.Expires = DateTimeOffset.FromUnixTimeSeconds((long)token.Expires.Value).UtcDateTime;
        jar.Add(authenticationCookie);
        using var handler = handlerFactory?.Invoke(jar) ?? new HttpClientHandler { CookieContainer = jar, UseCookies = true, AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        var requestStart = System.Diagnostics.Stopwatch.GetTimestamp();
        using var response = await client.GetAsync(IdentityUri, ct);
        networkLog?.Record(new(DateTimeOffset.UtcNow, IdentityUri.Host, "Session check", (int)response.StatusCode, (long)System.Diagnostics.Stopwatch.GetElapsedTime(requestStart).TotalMilliseconds));
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return markInvalid ? await MarkAsync(account.Id, SessionState.ReauthenticationRequired, "Session is no longer valid. Sign in again.", ct) : new(SessionState.Unknown, false, "Waiting for sign-in to finish.");
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            checksPausedUntil = DateTimeOffset.UtcNow.AddMinutes(5);
        if (!response.IsSuccessStatusCode)
            return new(account.SessionState, false, $"Roblox is temporarily unavailable ({(int)response.StatusCode}). Retrying later.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!document.RootElement.TryGetProperty("name", out var name) || !(account.RobloxUserId.HasValue && document.RootElement.TryGetProperty("id", out var userId) ? userId.GetInt64() == account.RobloxUserId.Value : string.Equals(name.GetString(), account.Username, StringComparison.OrdinalIgnoreCase)))
            return markInvalid ? await MarkAsync(account.Id, SessionState.ReauthenticationRequired, "This session belongs to another account. Sign in as the selected user.", ct) : new(SessionState.Unknown, false, "Sign in as the selected account.");
        // Save a token rotated by the identity response before attempting another network operation.
        var current = MergeCookies(session, jar, session.LastRenewedAt ?? session.SavedAt);
        await sessions.SaveAsync(account.Id, current, ct);
        var now = DateTimeOffset.UtcNow;
        var age = now - (current.LastRenewedAt ?? current.SavedAt);
        var expiring = token.Expires is > 0 && token.Expires < now.AddHours(24).ToUnixTimeSeconds();
        var due = age >= TimeSpan.FromHours(24) || (expiring && age >= TimeSpan.FromHours(1));
        var throttled = renewalAttempts.TryGetValue(account.Id, out var lastAttempt) && now - lastAttempt < TimeSpan.FromHours(1);
        if (renewIfDue && due && !throttled)
        {
            renewalAttempts[account.Id] = now;
            var renewalStart = System.Diagnostics.Stopwatch.GetTimestamp();
            using var refresh = await RefreshAsync(client, ct);
            networkLog?.Record(new(DateTimeOffset.UtcNow, RefreshUri.Host, "Session renewal", (int)refresh.StatusCode, (long)System.Diagnostics.Stopwatch.GetElapsedTime(renewalStart).TotalMilliseconds));
            if (refresh.StatusCode == HttpStatusCode.Unauthorized)
                return await MarkAsync(account.Id, SessionState.ReauthenticationRequired, "Session renewal was rejected. Sign in again.", ct);
            if (!refresh.IsSuccessStatusCode)
            {
                // A challenge, rate limit or outage does not prove that the validated session is lost.
                return await MarkAsync(account.Id, SessionState.Valid, "Session is valid; renewal was deferred. Complete sign-in if Roblox asks.", ct);
            }
            var refreshed = MergeCookies(current, jar, now);
            var previousToken = current.Cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY")?.Value;
            var nextToken = refreshed.Cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY")?.Value;
            if (string.IsNullOrEmpty(nextToken) || nextToken == previousToken)
                return await MarkAsync(account.Id, SessionState.Valid, "Session is valid. Roblox did not issue a new cookie; retrying later.", ct);
            // Refresh invalidates the old session. Persist the replacement immediately, before another request.
            await sessions.SaveAsync(account.Id, refreshed, ct);
            return await MarkAsync(account.Id, SessionState.Valid, "Session renewed automatically and encrypted.", ct);
        }
        return await MarkAsync(account.Id, SessionState.Valid, "Session verified.", ct);
    }
    private static AccountSession MergeCookies(AccountSession session, CookieContainer jar, DateTimeOffset? lastRenewedAt)
    {
        var cookies = session.Cookies.ToList();
        foreach (Cookie cookie in jar.GetCookies(IdentityUri))
        {
            cookies.RemoveAll(c => c.Name == cookie.Name);
            if (!cookie.Expired)
                cookies.Add(new(cookie.Name, cookie.Value, cookie.Domain, cookie.Path, cookie.Expires == DateTime.MinValue ? null : new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds(), cookie.Secure, cookie.HttpOnly));
        }
        return new(cookies, DateTimeOffset.UtcNow, lastRenewedAt);
    }
    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, CancellationToken ct)
    {
        using var first = new HttpRequestMessage(HttpMethod.Post, RefreshUri);
        var response = await client.SendAsync(first, ct);
        if (response.StatusCode != HttpStatusCode.Forbidden || !response.Headers.TryGetValues("x-csrf-token", out var values))
            return response;
        var csrf = values.FirstOrDefault();
        if (string.IsNullOrEmpty(csrf))
            return response;
        response.Dispose();
        using var retry = new HttpRequestMessage(HttpMethod.Post, RefreshUri);
        retry.Headers.TryAddWithoutValidation("x-csrf-token", csrf);
        return await client.SendAsync(retry, ct);
    }
    public async Task<RobloxLaunchSpec> CreateLaunchAsync(LaunchRequest request, CancellationToken ct = default)
    {
        if (request.PlaceId <= 0 || (!string.IsNullOrWhiteSpace(request.JobId) && !Guid.TryParse(request.JobId, out _)))
            throw new RobloxLaunchException("Enter a positive Place ID and an optional valid Job ID.");
        await gate.WaitAsync(ct);
        try
        {
            var account = (await accounts.GetAllAsync(ct)).FirstOrDefault(a => a.Id == request.AccountId) ?? throw new RobloxLaunchException("Account no longer exists.");
            var saved = await sessions.ReadAsync(account.Id, ct) ?? throw new RobloxLaunchException("Sign in before launching this account.");
            var check = await ValidateCoreAsync(account, saved, true, ct, false);
            if (check.State != SessionState.Valid || !check.Confirmed)
                throw new RobloxLaunchException(check.Message);
            saved = (await sessions.ReadAsync(account.Id, ct))!;
            var token = saved.Cookies.First(c => c.Name == ".ROBLOSECURITY");
            var jar = new CookieContainer();
            jar.Add(new Cookie(".ROBLOSECURITY", token.Value, "/", ".roblox.com") { Secure = true, HttpOnly = true });
            using var handler = handlerFactory?.Invoke(jar) ?? new HttpClientHandler { CookieContainer = jar, AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            async Task<HttpResponseMessage> RequestTicket(string? csrf)
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, "https://auth.roblox.com/v1/authentication-ticket/");
                message.Content = new System.Net.Http.StringContent("{}", System.Text.Encoding.UTF8, "application/json");
                message.Headers.Referrer = new Uri($"https://www.roblox.com/games/{request.PlaceId}/");
                message.Headers.TryAddWithoutValidation("RBXAuthenticationNegotiation", "1");
                if (csrf is not null)
                    message.Headers.TryAddWithoutValidation("x-csrf-token", csrf);
                var start = System.Diagnostics.Stopwatch.GetTimestamp();
                var result = await client.SendAsync(message, ct);
                networkLog?.Record(new(DateTimeOffset.UtcNow, "auth.roblox.com", "Launch ticket", (int)result.StatusCode, (long)System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds));
                return result;
            }
            var response = await RequestTicket(null);
            if (response.StatusCode == HttpStatusCode.Forbidden && response.Headers.TryGetValues("x-csrf-token", out var csrfValues))
            {
                var csrf = csrfValues.FirstOrDefault();
                response.Dispose();
                response = await RequestTicket(csrf);
            }
            using (response)
            {
                await sessions.SaveAsync(account.Id, MergeCookies(saved, jar, saved.LastRenewedAt), ct);
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    await accounts.SetSessionStateAsync(account.Id, SessionState.ReauthenticationRequired, ct);
                    throw new RobloxLaunchException("Session expired. Sign in again.");
                }
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    throw new RobloxLaunchException("Roblox is rate limiting launches. Try again later.");
                if (response.StatusCode == HttpStatusCode.Forbidden)
                    throw new RobloxLaunchException("Roblox blocked the launch (403). Open the account browser and complete any verification.");
                if (!response.IsSuccessStatusCode)
                    throw new RobloxLaunchException($"Roblox rejected the launch request (HTTP {(int)response.StatusCode}). Try again later.");
                if (!response.Headers.TryGetValues("rbx-authentication-ticket", out var tickets))
                    throw new RobloxLaunchException("Roblox did not return a launch ticket. Try again or sign in again.");
                var ticket = tickets.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(ticket) || ticket.Any(char.IsWhiteSpace))
                    throw new RobloxLaunchException("Roblox returned an invalid launch ticket.");
                var tracker = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000000, 2147483647).ToString(System.Globalization.CultureInfo.InvariantCulture);
                return LaunchProtocol.Create(request, ticket, tracker, DateTimeOffset.UtcNow);
            }
        }
        finally { gate.Release(); }
    }
    private async Task<SessionCheck> MarkAsync(string id, SessionState state, string message, CancellationToken ct)
    {
        await accounts.SetSessionStateAsync(id, state, ct);
        return new(state, true, message);
    }
}
