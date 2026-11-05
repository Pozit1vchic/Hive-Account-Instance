using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hive.Core;

namespace Hive.Roblox;

public sealed class UserLookupClient(Func<HttpMessageHandler>? handlerFactory = null, INetworkLog? networkLog = null)
{
    public async Task<long?> FindIdAsync(string username, CancellationToken ct = default)
    {
        username = username.Trim();
        if (username.Length is < 1 or > 64 || username.Any(char.IsControl)) throw new ArgumentException("Enter a valid username.", nameof(username));
        using var client = handlerFactory is null ? new HttpClient() : new HttpClient(handlerFactory());
        client.Timeout = TimeSpan.FromSeconds(15);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        using var response = await client.PostAsJsonAsync("https://users.roblox.com/v1/usernames/users", new { usernames = new[] { username }, excludeBannedUsers = false }, ct);
        networkLog?.Record(new(DateTimeOffset.UtcNow, "users.roblox.com", "User lookup", (int)response.StatusCode, (long)System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds));
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new RobloxLaunchException("Roblox limited user lookups. Try again later.");
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (json.RootElement.ValueKind != JsonValueKind.Object || !json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return null;
        foreach (var item in data.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && string.Equals(name.GetString(), username, StringComparison.OrdinalIgnoreCase) && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var result) && result > 0) return result;
        return null;
    }
}
