namespace Hive.Core;

public sealed class LoginCredential(string username, string password)
{
    public string Username { get; } = username; public string Password { get; } = password;
    public override string ToString() => nameof(LoginCredential);
}
public static class AccountInputParser
{
    public static IReadOnlyList<LoginCredential> Credentials(string input)
    {
        var result = new List<LoginCredential>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in input.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
                continue;
            var colon = line.IndexOf(':');
            if (colon < 1)
                throw new FormatException("Use username:password, one account per line.");
            var username = line[..colon].Trim();
            var password = line[(colon + 1)..];
            if (username.Length is < 1 or > 64 || password.Length is < 1 or > 5000)
                throw new FormatException("Check username and password lengths.");
            if (seen.Add(username))
                result.Add(new(username, password));
            if (result.Count > 50)
                throw new FormatException("Add up to 50 accounts per batch.");
        }
        if (result.Count == 0)
            throw new FormatException("Enter at least one account.");
        return result;
    }
    public static IReadOnlyList<string> Cookies(string input)
    {
        var result = input.Split('\n').Select(v => v.Trim()).Where(v => v.Length > 0).Select(v => v.StartsWith(".ROBLOSECURITY=", StringComparison.Ordinal) ? v[".ROBLOSECURITY=".Length..].TrimEnd(';') : v).Distinct(StringComparer.Ordinal).ToList();
        if (result.Count is < 1 or > 50 || result.Any(v => v.Length is < 1 or > 16384 || v.Any(char.IsWhiteSpace)))
            throw new FormatException("Enter up to 50 session cookies, one per line.");
        return result;
    }
}
