using System.Net;
using System.Text.RegularExpressions;
using Hive.Core;
namespace Hive.Roblox;

public static class LaunchTarget
{
    public static LaunchRequest Parse(string accountId, LaunchKind kind, string target, string argument)
    {
        var access = (string?)null; var link = (string?)null;
        if (Uri.TryCreate(argument, UriKind.Absolute, out var invite))
        {
            if (invite.UserInfo.Length > 0 || !invite.IsDefaultPort || argument.Length > 8192) throw new FormatException("Invalid private-server invite URL.");
            if (kind != LaunchKind.PrivateServer || invite.Scheme != "https" || (invite.Host != "www.roblox.com" && invite.Host != "roblox.com")) throw new FormatException("Use a Roblox private-server invite URL.");
            if (invite.AbsolutePath.TrimEnd('/') == "/share") return new(accountId, long.TryParse(target, out var fallback) ? fallback : 0, null, kind, PrivateInviteUrl: invite.AbsoluteUri);
            var match = Regex.Match(invite.AbsolutePath, @"^/games/(\d+)(?:/|$)");
            if (!match.Success) throw new FormatException("Use the full game invite URL, with its Place ID.");
            target = match.Groups[1].Value;
            foreach (var item in invite.Query.TrimStart('?').Split('&'))
            {
                var pair = item.Split('=', 2); if (pair.Length != 2) continue;
                if (pair[0] is "privateServerLinkCode" or "linkCode") link = WebUtility.UrlDecode(pair[1]);
                if (pair[0] == "accessCode") access = WebUtility.UrlDecode(pair[1]);
            }
        }
        else if (kind == LaunchKind.PrivateServer) access = argument.Trim();
        if (!long.TryParse(target, out var id) || id <= 0) throw new FormatException(kind == LaunchKind.FollowUser ? "Enter a positive Roblox user ID." : "Enter a positive Place ID.");
        var job = string.IsNullOrWhiteSpace(argument) ? null : argument.Trim();
        if (kind == LaunchKind.Server || (kind == LaunchKind.Place && job is not null))
        {
            if (!Guid.TryParse(job, out var parsed)) throw new FormatException("Enter a valid server Job ID.");
            job = parsed.ToString();
        }
        else job = null;
        if (kind == LaunchKind.PrivateServer && (string.IsNullOrWhiteSpace(access) && string.IsNullOrWhiteSpace(link))) throw new FormatException("Enter an access code or full private-server invite URL.");
        if ((access?.Length ?? 0) > 4096 || (link?.Length ?? 0) > 4096 || access?.Any(char.IsControl) == true || link?.Any(char.IsControl) == true) throw new FormatException("Invalid private-server code.");
        return new(accountId, kind == LaunchKind.FollowUser ? 0 : id, job, kind, access, link, kind == LaunchKind.FollowUser ? id : null);
    }
    public static void Validate(LaunchRequest request)
    {
        if (request.PrivateInviteUrl is not null)
        {
            if (request.Kind != LaunchKind.PrivateServer || request.PrivateInviteUrl.Length > 8192 || !Uri.TryCreate(request.PrivateInviteUrl, UriKind.Absolute, out var invite) || invite.UserInfo.Length > 0 || !invite.IsDefaultPort || invite.Scheme != "https" || (invite.Host != "www.roblox.com" && invite.Host != "roblox.com") || invite.AbsolutePath.TrimEnd('/') != "/share") throw new ArgumentException("Invalid private invite URL.");
            return;
        }
        if (request.Kind == LaunchKind.FollowUser ? request.FollowUserId is null or <= 0 : request.PlaceId <= 0) throw new ArgumentException("Invalid launch target.");
        if (request.JobId is not null && !Guid.TryParse(request.JobId, out _)) throw new ArgumentException("Invalid Job ID.");
        if (request.Kind == LaunchKind.PrivateServer && string.IsNullOrWhiteSpace(request.AccessCode) && string.IsNullOrWhiteSpace(request.LinkCode)) throw new ArgumentException("Missing private-server code.");
        if ((request.AccessCode?.Length ?? 0) > 4096 || (request.LinkCode?.Length ?? 0) > 4096 || request.AccessCode?.Any(char.IsControl) == true || request.LinkCode?.Any(char.IsControl) == true) throw new ArgumentException("Invalid private-server code.");
    }
}
