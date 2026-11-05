using System.Net;
using System.Net.Http;
using Hive.Roblox;

internal static class PresenceChecks
{
    public static async Task RunAsync()
    {
        var online = new PresenceClient(() => new Handler(HttpStatusCode.OK, "{\"userPresences\":[{\"userId\":123,\"userPresenceType\":2}]}"));
        if (await online.GetPresenceAsync(123) != "In an experience") throw new Exception("Presence result was not mapped");
        var unrelated = new PresenceClient(() => new Handler(HttpStatusCode.OK, "{\"userPresences\":[{\"userId\":999,\"userPresenceType\":2}]}"));
        if (await unrelated.GetPresenceAsync(123) != "Roblox presence is unavailable.") throw new Exception("Presence matched a different user");
        var limited = new PresenceClient(() => new Handler(HttpStatusCode.TooManyRequests, "{}"));
        if (!(await limited.GetPresenceAsync(123)).Contains("rate limited")) throw new Exception("Presence rate limit was ignored");
        var lookup = new UserLookupClient(() => new Handler(HttpStatusCode.OK, "{\"data\":[{\"name\":\"TestUser\",\"id\":456}]}"));
        if (await lookup.FindIdAsync("testuser") != 456 || await lookup.FindIdAsync("different") is not null) throw new Exception("Username lookup returned the wrong identity");
        var deniedLookup = new UserLookupClient(() => new Handler(HttpStatusCode.TooManyRequests, "{}"));
        try { await deniedLookup.FindIdAsync("testuser"); throw new Exception("User lookup ignored a rate limit"); }
        catch (Hive.Core.RobloxLaunchException) { }
        var malformed = new PresenceClient(() => new Handler(HttpStatusCode.OK, "{\"userPresences\":[null,{\"userId\":\"123\",\"userPresenceType\":2}]}"));
        if (await malformed.GetPresenceAsync(123) != "Roblox presence is unavailable.") throw new Exception("Malformed presence data was accepted");
        Console.WriteLine("PASS: explicit Roblox presence lookup, identity matching and rate-limit reporting.");
    }
    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }
}
