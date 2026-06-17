using System;
using UnityEngine;

/// <summary>Shared pending-request state for <see cref="ITrackSpawnQueueSource"/> implementations.</summary>
[Serializable]
public class TrackSpawnQueuePendingState
{
    [NonSerialized] private bool _controlled;
    [NonSerialized] private bool _pending;
    [NonSerialized] private TrackSpawnerQueue _owner;

    public bool IsControlled => _controlled;
    public bool HasPending => _pending;

    public void Bind(bool controlled, TrackSpawnerQueue owner)
    {
        _controlled = controlled;
        _owner = controlled ? owner : null;
        if (!controlled)
            _pending = false;
    }

    public bool TrySubmit(ITrackSpawnQueueSource source)
    {
        if (!_controlled || _pending || _owner == null || source == null)
            return false;

        if (!source.IsSpawnQueueReady || !source.HasSpawnQueueCapacity)
            return false;

        if (!_owner.AcceptSpawnRequest(source))
            return false;

        _pending = true;
        return true;
    }

    public bool TryExecute(Func<bool> spawn)
    {
        if (!_pending)
            return false;

        bool spawned = spawn != null && spawn();
        _pending = false;
        return spawned;
    }

    public void Cancel()
    {
        _pending = false;
    }
}
