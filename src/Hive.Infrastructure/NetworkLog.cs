using Hive.Core;
namespace Hive.Infrastructure;

public sealed class NetworkLog : INetworkLog
{
    private readonly object sync = new();
    private readonly Queue<NetworkEvent> entries = new();
    public void Record(NetworkEvent entry)
    {
        lock (sync)
        {
            entries.Enqueue(entry);
            while (entries.Count > 250)
                entries.Dequeue();
        }
    }
    public IReadOnlyList<NetworkEvent> Snapshot()
    {
        lock (sync)
            return entries.Reverse().ToArray();
    }
}
