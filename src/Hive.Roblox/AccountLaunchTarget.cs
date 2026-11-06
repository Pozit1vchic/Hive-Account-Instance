using Hive.Core;
namespace Hive.Roblox;

public static class AccountLaunchTarget
{
    public static LaunchRequest Resolve(string accountId, LaunchKind mode, string target, string argument, IReadOnlyDictionary<string, string> fields)
    {
        if (mode == LaunchKind.FollowUser) return LaunchTarget.Parse(accountId, mode, target, argument);
        if (fields.TryGetValue("SavedPlaceId", out var savedPlace) && !string.IsNullOrWhiteSpace(savedPlace)) target = savedPlace;
        if (fields.TryGetValue("SavedJobId", out var savedJob) && !string.IsNullOrWhiteSpace(savedJob))
        {
            argument = savedJob;
            mode = Uri.TryCreate(savedJob, UriKind.Absolute, out _) ? LaunchKind.PrivateServer : Guid.TryParse(savedJob, out _) ? LaunchKind.Server : LaunchKind.PrivateServer;
        }
        return LaunchTarget.Parse(accountId, mode, target, argument);
    }
}
