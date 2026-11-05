using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hive.Core;

namespace Hive.Roblox;

public sealed class PresenceClient(Func<HttpMessageHandler>? handlerFactory = null, INetworkLog? networkLog = null)
{
    public async Task<string> GetPresenceAsync(long userId, CancellationToken ct = default)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));
        using var client = handlerFactory is null ? new HttpClient() : new HttpClient(handlerFactory());
        client.Timeout = TimeSpan.FromSeconds(15);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        using var response = await client.PostAsJsonAsync("https://presence.roblox.com/v1/presence/users", new { userIds = new[] { userId } }, ct);
        networkLog?.Record(new(DateTimeOffset.UtcNow, "presence.roblox.com", "Presence check", (int)response.StatusCode, (long)System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds));
        if (response.StatusCode == HttpStatusCode.TooManyRequests) return "Roblox rate limited presence checks. Try again later.";
        if (!response.IsSuccessStatusCode) return "Roblox presence is unavailable.";
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("userPresences", out var list) || list.ValueKind != JsonValueKind.Array) return "Roblox presence is unavailable.";
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("userId", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var found) || found != userId || !item.TryGetProperty("userPresenceType", out var type) || type.ValueKind != JsonValueKind.Number || !type.TryGetInt32(out var status)) continue;
            return status switch { 0 => "Offline", 1 => "Online", 2 => "In an experience", 3 => "In Studio", _ => "Unknown" };
        }
        return "Roblox presence is unavailable.";
    }
}
