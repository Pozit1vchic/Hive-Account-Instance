using System.Net;
using System.Text.RegularExpressions;
using Hive.Core;
namespace Hive.Roblox;

public static class PrivateInviteResolver
{
    public static async Task<LaunchRequest> ResolveAsync(LaunchRequest request, HttpClient client, CancellationToken ct)
    {
        if (request.PrivateInviteUrl is null) return request;
        LaunchTarget.Validate(request);
        var address = new Uri(request.PrivateInviteUrl);
        for (var step = 0; step < 4; step++)
        {
            using var response = await client.GetAsync(address, ct);
            if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new RobloxLaunchException("Roblox limited invite resolution. Try again later.", true);
            if (response.Headers.Location is Uri location)
            {
                var next = location.IsAbsoluteUri ? location : new Uri(address, location);
                if (next.UserInfo.Length > 0 || !next.IsDefaultPort || next.AbsoluteUri.Length > 8192) break;
                var resolved = FromUri(request, next); if (resolved is not null) return resolved;
                if (next.Scheme != "https" || (next.Host != "www.roblox.com" && next.Host != "roblox.com")) break;
                address = next; continue;
            }
            if (!response.IsSuccessStatusCode) break;
            var current = FromUri(request, address); if (current is not null) return current;
            var html = await response.Content.ReadAsStringAsync(ct);
            if (html.Length > 2_000_000) break;
            foreach (Match match in Regex.Matches(html, "(?:roblox://|https://www\\.roblox\\.com/games/)[^\\s\"'<>]+", RegexOptions.IgnoreCase))
            {
                if (Uri.TryCreate(WebUtility.HtmlDecode(match.Value), UriKind.Absolute, out var embedded))
                {
                    var target = FromUri(request, embedded); if (target is not null) return target;
                }
            }
            break;
        }
        throw new RobloxLaunchException("This invite could not be resolved. Check its availability or use the full game invite URL.");
    }
    private static LaunchRequest? FromUri(LaunchRequest original, Uri uri)
    {
        if (uri.Scheme == "https" && uri.Host != "www.roblox.com" && uri.Host != "roblox.com") return null;
        if (uri.Scheme is not ("https" or "roblox")) return null;
        var values = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in uri.Query.TrimStart('?').Split('&')) { var parts = field.Split('=',2); if (parts.Length == 2) values[parts[0]] = WebUtility.UrlDecode(parts[1]); }
        var match = Regex.Match(uri.AbsolutePath,@"^/games/(\d+)(?:/|$)");
        var place = match.Success ? match.Groups[1].Value : values.GetValueOrDefault("placeId");
        var link = values.GetValueOrDefault("privateServerLinkCode") ?? values.GetValueOrDefault("linkCode");
        var access = values.GetValueOrDefault("accessCode");
        if (!long.TryParse(place, out var id) || id <= 0 || (string.IsNullOrWhiteSpace(link) && string.IsNullOrWhiteSpace(access))) return null;
        var result = original with { PlaceId = id, LinkCode = link, AccessCode = access, PrivateInviteUrl = null };
        try { LaunchTarget.Validate(result); } catch (ArgumentException) { return null; }
        return result;
    }
}
