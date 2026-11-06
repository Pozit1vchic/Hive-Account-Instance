using System.Net;
using System.Net.Http;
using Hive.Core;
using Hive.Roblox;

internal static class PublicServerChecks
{
    public static async Task RunAsync()
    {
        var id = Guid.NewGuid().ToString();
        var client = new PublicServerClient(() => new Handler(request =>
        {
            if (request.RequestUri?.Host != "games.roblox.com" || !request.RequestUri.AbsolutePath.Contains("/123/servers/Public") || !request.RequestUri.Query.Contains("cursor=next%2Bpage")) throw new Exception("Server-list request was incorrect");
            return new(HttpStatusCode.OK) { Content = new StringContent($$"""{"data":[{"id":"{{id}}","playing":3,"maxPlayers":12,"fps":59.5,"ping":45},{"id":"invalid","playing":1,"maxPlayers":2}],"nextPageCursor":"following"}""") };
        }));
        var page = await client.GetPageAsync(123, "next+page");
        if (page.Servers.Count != 1 || page.Servers[0].Id != id || page.Servers[0].Playing != 3 || page.NextCursor != "following") throw new Exception("Server list parse failed");
        client = new PublicServerClient(() => new Handler(_ => new(HttpStatusCode.TooManyRequests)));
        try { await client.GetPageAsync(123); throw new Exception("Rate limit accepted"); } catch (RobloxLaunchException) { }
        client = new PublicServerClient(() => new Handler(_ => new(HttpStatusCode.OK) { Content = new StringContent("[]") }));
        try { await client.GetPageAsync(123); throw new Exception("Invalid server response accepted"); } catch (RobloxLaunchException) { }
        Console.WriteLine("PASS: public server pagination, validated server IDs and rate-limit handling.");
    }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(response(request));
    }
}
