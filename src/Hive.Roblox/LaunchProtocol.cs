using Hive.Core;
namespace Hive.Roblox;

public static class LaunchProtocol
{
    public static RobloxLaunchSpec Create(LaunchRequest request, string ticket, string tracker, DateTimeOffset timestamp)
    {
        if (request.PlaceId <= 0 || (!string.IsNullOrEmpty(request.JobId) && !Guid.TryParse(request.JobId, out _)))
            throw new ArgumentException("Invalid launch target.");
        var job = string.IsNullOrWhiteSpace(request.JobId) ? null : Guid.Parse(request.JobId).ToString();
        var url = $"https://assetgame.roblox.com/game/PlaceLauncher.ashx?request={(job is null ? "RequestGame" : "RequestGameJob")}&browserTrackerId={tracker}&placeId={request.PlaceId}" + (job is null ? "" : "&gameId=" + job) + "&isPlayTogetherGame=false";
        return new($"roblox-player:1+launchmode:play+gameinfo:{Uri.EscapeDataString(ticket)}+launchtime:{timestamp.ToUnixTimeMilliseconds()}+placelauncherurl:{Uri.EscapeDataString(url)}+browsertrackerid:{tracker}+robloxLocale:en_us+gameLocale:en_us+channel:+LaunchExp:InApp", tracker);
    }
}
