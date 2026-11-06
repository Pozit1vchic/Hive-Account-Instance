using Hive.Core;
namespace Hive.Roblox;

public static class AccountLaunchTarget
{
    public static LaunchRequest Resolve(string accountId, LaunchKind mode, string target, string argument, IReadOnlyDictionary<string, string> fields)
    {
        if (mode == LaunchKind.FollowUser) return LaunchTarget.Parse(accountId, mode, target, argument);
        if (fields.TryGetValue("SavedPlaceId", out var savedPlace) && !string.IsNullOrWhiteSpace(savedPlace)) target = savedPlace;
        if (fields.TryGetValue("SavedJobId", out var savedJob))
        {
            argument = savedJob;
            if (fields.TryGetValue("SavedTargetKind", out var kind) && Enum.TryParse<LaunchKind>(kind, out var parsed) && parsed is LaunchKind.Place or LaunchKind.Server or LaunchKind.PrivateServer) mode = parsed;
            else mode = string.IsNullOrWhiteSpace(savedJob) ? LaunchKind.Place : Uri.TryCreate(savedJob, UriKind.Absolute, out _) ? LaunchKind.PrivateServer : Guid.TryParse(savedJob, out _) ? LaunchKind.Server : LaunchKind.PrivateServer;
        }
        return LaunchTarget.Parse(accountId, mode, target, argument);
    }
}
