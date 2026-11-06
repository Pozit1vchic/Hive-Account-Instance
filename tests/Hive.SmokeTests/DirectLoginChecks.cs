using System.Net;
using System.Net.Http;
using System.Text.Json;
using Hive.Core;
using Hive.Roblox;

internal static class DirectLoginChecks
{
    public static async Task RunAsync()
    {
        var calls = 0;
        var client = new DirectLoginClient(jar => new Handler(async (request, ct) =>
        {
            calls++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            if (body.RootElement.GetProperty("ctype").GetString() != "Username" || body.RootElement.GetProperty("cvalue").GetString() != "fixture-user" || body.RootElement.GetProperty("password").GetString() != "fixture-password") throw new Exception("Direct login body mismatch");
            if (calls == 1) { var csrf = new HttpResponseMessage(HttpStatusCode.Forbidden); csrf.Headers.Add("x-csrf-token", "fixture-csrf"); return csrf; }
            if (!request.Headers.Contains("x-csrf-token")) throw new Exception("Missing CSRF retry");
            jar.Add(new Cookie(".ROBLOSECURITY", "fixture-session", "/", ".roblox.com") { Secure = true, HttpOnly = true });
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var result = await client.LoginAsync(new("fixture-user", "fixture-password"));
        if (calls != 2 || result.Status != DirectLoginStatus.Success || result.Session?.Cookies.Single().Value != "fixture-session") throw new Exception("Direct login or session capture failed");
        calls = 0;
        client = new DirectLoginClient(_ => new Handler((_, _) => { calls++; var challenge = new HttpResponseMessage(HttpStatusCode.Forbidden); challenge.Headers.Add("rblx-challenge-id", "fixture"); challenge.Headers.Add("x-csrf-token", "fixture"); return Task.FromResult(challenge); }));
        if ((await client.LoginAsync(new("fixture-user", "fixture-password"))).Status != DirectLoginStatus.VerificationRequired || calls != 1) throw new Exception("Challenge was retried instead of reported");
        client = new DirectLoginClient(_ => new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden))));
        if ((await client.LoginAsync(new("fixture-user", "fixture-password"))).Status != DirectLoginStatus.DirectRequestDenied) throw new Exception("Unexplained HTTP 403 was incorrectly classified as a verification challenge");
        client = new DirectLoginClient(_ => new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests))));
        if ((await client.LoginAsync(new("fixture-user", "fixture-password"))).Status != DirectLoginStatus.RateLimited) throw new Exception("Rate limit was not reported");
        client = new DirectLoginClient(_ => new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))));
        if ((await client.LoginAsync(new("fixture-user", "fixture-password"))).Status != DirectLoginStatus.VerificationRequired) throw new Exception("Missing session was accepted");
        Console.WriteLine("PASS: direct login CSRF, cookie capture, challenge and rate-limit handling (simulated responses).");
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handle(request, ct);
    }
}
