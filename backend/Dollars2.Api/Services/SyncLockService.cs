using System.Collections.Concurrent;

namespace Dollars2.Api.Services;

public class SyncLockService
{
    private readonly ConcurrentDictionary<int, byte> _active = new();

    public bool TryAcquire(int userId) => _active.TryAdd(userId, 0);

    public void Release(int userId) => _active.TryRemove(userId, out _);
}
