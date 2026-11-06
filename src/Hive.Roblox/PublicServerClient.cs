using System.Net;
using System.Text.Json;
using Hive.Core;
namespace Hive.Roblox;

public sealed record PublicServer(string Id, int Playing, int Capacity, double? Fps, int? Ping);
public sealed record PublicServerPage(IReadOnlyList<PublicServer> Servers, string? NextCursor);

public sealed class PublicServerClient(Func<HttpMessageHandler>? handlerFactory = null, INetworkLog? networkLog = null)
{
    public async Task<PublicServerPage> GetPageAsync(long placeId, string? cursor = null, CancellationToken ct = default)
    {
        if (placeId <= 0 || cursor?.Length > 1024) throw new ArgumentException("Enter a valid Place ID.");
        var uri = $"https://games.roblox.com/v1/games/{placeId}/servers/Public?sortOrder=Asc&excludeFullGames=true&limit=25" + (string.IsNullOrEmpty(cursor) ? "" : "&cursor=" + Uri.EscapeDataString(cursor));
        using var client = handlerFactory is null ? new HttpClient() : new HttpClient(handlerFactory());
        client.Timeout = TimeSpan.FromSeconds(15);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        using var response = await client.GetAsync(uri, ct);
        networkLog?.Record(new(DateTimeOffset.UtcNow, "games.roblox.com", "Public server list", (int)response.StatusCode, (long)System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds));
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new RobloxLaunchException("Roblox rate limited server browsing. Try again later.");
        if (!response.IsSuccessStatusCode) throw new RobloxLaunchException("Could not load public servers for this place.");
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 100) throw new RobloxLaunchException("Roblox returned an invalid server list.");
        var servers = new List<PublicServer>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), out var guid) || !item.TryGetProperty("playing", out var playing) || playing.ValueKind != JsonValueKind.Number || !playing.TryGetInt32(out var count) || !item.TryGetProperty("maxPlayers", out var max) || max.ValueKind != JsonValueKind.Number || !max.TryGetInt32(out var capacity) || count < 0 || capacity <= 0 || count > capacity) continue;
            double? fps = item.TryGetProperty("fps", out var f) && f.ValueKind == JsonValueKind.Number && f.TryGetDouble(out var rate) && double.IsFinite(rate) ? rate : null;
            int? ping = item.TryGetProperty("ping", out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var value) && value >= 0 ? value : null;
            servers.Add(new(guid.ToString(), count, capacity, fps, ping));
        }
        var next = json.RootElement.TryGetProperty("nextPageCursor", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
        if (next?.Length > 1024) throw new RobloxLaunchException("Roblox returned an invalid server cursor.");
        return new(servers, next);
    }
}
