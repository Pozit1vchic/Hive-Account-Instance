using System.Net;
using System.Text.Json;
using Hive.Core;
namespace Hive.Roblox;

public sealed class AccountOnboardingService(IAccountRepository accounts, ISessionStore sessions, Func<CookieContainer, HttpMessageHandler>? handlerFactory = null)
{
    public async Task<RobloxIdentity?> IdentifyAsync(AccountSession session, CancellationToken ct = default) => (await IdentifyCoreAsync(session, ct))?.Identity;
    private async Task<(RobloxIdentity Identity, AccountSession Session)?> IdentifyCoreAsync(AccountSession session, CancellationToken ct)
    {
        var token = session.Cookies.FirstOrDefault(c => c.Name == ".ROBLOSECURITY" && (c.Domain == ".roblox.com" || c.Domain == "roblox.com"));
        if (token is null)
            return null;
        var jar = new CookieContainer();
        var authenticationCookie = new Cookie(".ROBLOSECURITY", token.Value, "/", ".roblox.com") { Secure = true, HttpOnly = true };
        if (token.Expires is > 0)
            authenticationCookie.Expires = DateTimeOffset.FromUnixTimeSeconds((long)token.Expires.Value).UtcDateTime;
        jar.Add(authenticationCookie);
        using var handler = handlerFactory?.Invoke(jar) ?? new HttpClientHandler { CookieContainer = jar, AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        using var response = await client.GetAsync("https://users.roblox.com/v1/users/authenticated", ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return null;
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var identity = new RobloxIdentity(json.RootElement.GetProperty("id").GetInt64(), json.RootElement.GetProperty("name").GetString()!, json.RootElement.TryGetProperty("displayName", out var name) ? name.GetString() ?? "" : "");
        var updated = session.Cookies.ToList();
        foreach (Cookie cookie in jar.GetCookies(new Uri("https://users.roblox.com")))
        {
            updated.RemoveAll(c => c.Name == cookie.Name);
            updated.Add(new(cookie.Name, cookie.Value, cookie.Domain, cookie.Path, cookie.Expires == DateTime.MinValue ? null : new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds(), cookie.Secure, cookie.HttpOnly));
        }
        return (identity, new AccountSession(updated, DateTimeOffset.UtcNow));
    }
    public async Task<Account?> AddAsync(AccountSession session, CancellationToken ct = default, string? expectedUsername = null)
    {
        var verified = await IdentifyCoreAsync(session, ct);
        if (verified is null)
            return null;
        var identity = verified.Value.Identity;
        if (expectedUsername is not null && !identity.Username.Equals(expectedUsername, StringComparison.OrdinalIgnoreCase))
            return null;
        session = verified.Value.Session;
        var known = await accounts.GetAllAsync(ct);
        var existing = known.FirstOrDefault(a => a.RobloxUserId == identity.Id) ?? known.FirstOrDefault(a => a.RobloxUserId is null && a.Username.Equals(identity.Username, StringComparison.OrdinalIgnoreCase));
        var account = new Account(existing?.Id ?? Guid.NewGuid().ToString("N"), identity.Username, existing?.DisplayName ?? identity.DisplayName, existing?.GroupId, existing?.Notes ?? "", SessionState.Unknown, identity.Id);
        // Persist metadata first, the encrypted session second, and only then declare the account signed in.
        await accounts.SaveAsync(account, ct);
        await sessions.SaveAsync(account.Id, session, ct);
        await accounts.SetSessionStateAsync(account.Id, SessionState.Valid, ct);
        return account with
        {
            SessionState = SessionState.Valid
        };
    }
}
