using System.Net;
using System.Net.Http.Json;
using Hive.Core;
namespace Hive.Roblox;

public enum DirectLoginStatus { Success, VerificationRequired, Rejected, RateLimited, DirectRequestDenied, Unavailable }
public sealed record DirectLoginResult(DirectLoginStatus Status, AccountSession? Session = null);

public sealed class DirectLoginClient(Func<CookieContainer, HttpMessageHandler>? handlerFactory = null)
{
    private static readonly Uri Endpoint = new("https://auth.roblox.com/v2/login");
    public async Task<DirectLoginResult> LoginAsync(LoginCredential credential, CancellationToken ct = default)
    {
        var cookies = new CookieContainer();
        using var handler = handlerFactory?.Invoke(cookies) ?? new HttpClientHandler { CookieContainer = cookies, AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
        string? csrf = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(new { ctype = "Username", cvalue = credential.Username, password = credential.Password }) };
            if (csrf is not null) request.Headers.TryAddWithoutValidation("x-csrf-token", csrf);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return new(DirectLoginStatus.RateLimited);
            if (response.Headers.Contains("rblx-challenge-id") || response.Headers.Contains("rblx-challenge-type")) return new(DirectLoginStatus.VerificationRequired);
            if (response.StatusCode == HttpStatusCode.Forbidden && attempt == 0 && response.Headers.TryGetValues("x-csrf-token", out var values))
            {
                csrf = values.FirstOrDefault();
                if (!string.IsNullOrEmpty(csrf)) continue;
            }
            if (!response.IsSuccessStatusCode) return new(response.StatusCode == HttpStatusCode.Unauthorized ? DirectLoginStatus.Rejected : response.StatusCode == HttpStatusCode.Forbidden ? DirectLoginStatus.DirectRequestDenied : DirectLoginStatus.Unavailable);
            var cookie = cookies.GetCookies(Endpoint).Cast<Cookie>().FirstOrDefault(c => c.Name == ".ROBLOSECURITY" && !string.IsNullOrWhiteSpace(c.Value));
            if (cookie is null) return new(DirectLoginStatus.VerificationRequired);
            var session = new AccountSession([new(cookie.Name, cookie.Value, cookie.Domain, cookie.Path, cookie.Expires == DateTime.MinValue ? null : new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds(), cookie.Secure, cookie.HttpOnly)], DateTimeOffset.UtcNow);
            return new(DirectLoginStatus.Success, session);
        }
        return new(DirectLoginStatus.Unavailable);
    }
}
