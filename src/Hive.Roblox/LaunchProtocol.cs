using Hive.Core;
namespace Hive.Roblox;

public static class LaunchProtocol
{
    public static RobloxLaunchSpec Create(LaunchRequest request, string ticket, string tracker, DateTimeOffset timestamp)
    {
        LaunchTarget.Validate(request);
        if (request.PrivateInviteUrl is not null) throw new ArgumentException("Resolve the private invite before creating the launch protocol.");
        var job = string.IsNullOrWhiteSpace(request.JobId) ? null : Guid.Parse(request.JobId).ToString();
        var query = request.Kind switch
        {
            LaunchKind.FollowUser => $"request=RequestFollowUser&userId={request.FollowUserId}",
            LaunchKind.PrivateServer => $"request=RequestPrivateGame&placeId={request.PlaceId}" + (string.IsNullOrEmpty(request.AccessCode) ? "" : "&accessCode=" + Uri.EscapeDataString(request.AccessCode)) + (string.IsNullOrEmpty(request.LinkCode) ? "" : "&linkCode=" + Uri.EscapeDataString(request.LinkCode)),
            _ => $"request={(job is null ? "RequestGame" : "RequestGameJob")}&placeId={request.PlaceId}" + (job is null ? "" : "&gameId=" + job)
        };
        var url = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?{query}&browserTrackerId={tracker}&isPlayTogetherGame=false";
        return new($"roblox-player:1+launchmode:play+gameinfo:{Uri.EscapeDataString(ticket)}+launchtime:{timestamp.ToUnixTimeMilliseconds()}+placelauncherurl:{Uri.EscapeDataString(url)}+browsertrackerid:{tracker}+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp", tracker);
    }
}
