using Dapper;
using Hive.Core;
namespace Hive.Storage;

public sealed class AccountRepository(HiveDatabase database) : IAccountRepository
{
    public async Task<IReadOnlyList<Account>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var c = await database.OpenAsync(cancellationToken);
        var rows = await c.QueryAsync<Row>(new CommandDefinition("SELECT * FROM Accounts ORDER BY Username", cancellationToken: cancellationToken));
        return rows.Select(r => new Account(r.Id, r.Username, r.DisplayName, r.GroupId, r.Notes, (SessionState)r.SessionState, r.RobloxUserId)).ToList();
    }
    public async Task SaveAsync(Account account, CancellationToken cancellationToken = default)
    {
        await using var c = await database.OpenAsync(cancellationToken);
        await c.ExecuteAsync(new CommandDefinition("""
  INSERT INTO Accounts(Id,Username,DisplayName,GroupId,Notes,SessionState,RobloxUserId) VALUES(@Id,@Username,@DisplayName,@GroupId,@Notes,@SessionState,@RobloxUserId)
  ON CONFLICT(Id) DO UPDATE SET Username=excluded.Username,DisplayName=excluded.DisplayName,GroupId=excluded.GroupId,Notes=excluded.Notes,RobloxUserId=COALESCE(excluded.RobloxUserId,Accounts.RobloxUserId);
  """, account, cancellationToken: cancellationToken));
    }
    public async Task SetSessionStateAsync(string id, SessionState state, CancellationToken cancellationToken = default)
    {
        await using var c = await database.OpenAsync(cancellationToken);
        await c.ExecuteAsync(new CommandDefinition("UPDATE Accounts SET SessionState=@state WHERE Id=@id", new
        {
            id,
            state
        }, cancellationToken: cancellationToken));
    }
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await using var c = await database.OpenAsync(cancellationToken);
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM Accounts WHERE Id=@id", new
        {
            id
        }, cancellationToken: cancellationToken));
    }
    private sealed class Row
    {
        public string Id { get; set; } = ""; public string Username { get; set; } = ""; public string DisplayName { get; set; } = "";
        public string? GroupId
        {
            get; set;
        }
        public string Notes { get; set; } = ""; public int SessionState
        {
            get; set;
        }
        public long? RobloxUserId
        {
            get; set;
        }
    }
}
