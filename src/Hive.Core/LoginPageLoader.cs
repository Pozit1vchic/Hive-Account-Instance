namespace Hive.Core;

public static class LoginPageLoader
{
    public static async Task PrepareAsync(IBrowserSession session, LoginCredential credential, Action<int>? retrying = null, CancellationToken ct = default)
    {
        for (var attempt = 1; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await session.NavigateAsync(new Uri("https://www.roblox.com/login"), ct);
                await session.AcceptCookieConsentAsync(ct);
                await session.FillLoginAsync(credential.Username, credential.Password, ct);
                return;
            }
            catch (Exception ex) when (attempt < 3 && !ct.IsCancellationRequested && session.IsOpen && ex is BrowserPageLoadException or TimeoutException)
            {
                retrying?.Invoke(attempt + 1);
                await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
            }
        }
    }
}
