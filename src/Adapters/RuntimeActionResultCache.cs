using System;
using System.Collections.Generic;

namespace DominionWars.Adapters
{

public interface IRuntimeActionResultCache
{
    bool TryGet(string matchId, long snapshotRevision, string actionId, out RuntimeActionResult result);
    bool TryStore(string matchId, long snapshotRevision, string actionId, RuntimeActionResult result);
}

public interface IRuntimeActionResultRetentionPolicy
{
    bool ShouldRetain(RuntimeActionResult result, DateTimeOffset createdAt, DateTimeOffset now);
}

public sealed class RetainForeverRuntimeActionResultPolicy : IRuntimeActionResultRetentionPolicy
{
    public bool ShouldRetain(RuntimeActionResult result, DateTimeOffset createdAt, DateTimeOffset now) => true;
}

public sealed class TimeToLiveRuntimeActionResultPolicy : IRuntimeActionResultRetentionPolicy
{
    private readonly TimeSpan _ttl;
    public TimeToLiveRuntimeActionResultPolicy(TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(ttl));
        _ttl = ttl;
    }
    public bool ShouldRetain(RuntimeActionResult result, DateTimeOffset createdAt, DateTimeOffset now) =>
        result is not null && createdAt + _ttl > now;
}

public sealed class InMemoryRuntimeActionResultCache : IRuntimeActionResultCache
{
    private readonly object _gate = new object();
    private readonly IRuntimeActionResultRetentionPolicy _policy;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<RuntimeActionCacheKey, CacheEntry> _results =
        new Dictionary<RuntimeActionCacheKey, CacheEntry>();

    public InMemoryRuntimeActionResultCache() : this(new RetainForeverRuntimeActionResultPolicy(), () => DateTimeOffset.UtcNow) { }

    public InMemoryRuntimeActionResultCache(IRuntimeActionResultRetentionPolicy policy, Func<DateTimeOffset> clock)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public bool TryGet(string matchId, long revision, string actionId, out RuntimeActionResult result)
    {
        if (!Valid(matchId, revision, actionId)) { result = null!; return false; }
        lock (_gate)
        {
            var key = new RuntimeActionCacheKey(matchId, revision, actionId);
            if (!_results.TryGetValue(key, out var entry)) { result = null!; return false; }
            if (!_policy.ShouldRetain(entry.Result, entry.CreatedAt, _clock()))
            { _results.Remove(key); result = null!; return false; }
            result = entry.Result; return true;
        }
    }

    public bool TryStore(string matchId, long revision, string actionId, RuntimeActionResult result)
    {
        if (!Valid(matchId, revision, actionId)) throw new ArgumentException("A non-empty match and action identity are required.", nameof(matchId));
        if (result is null) throw new ArgumentNullException(nameof(result));
        if (result.SnapshotRevision != revision || !string.Equals(result.MatchId, matchId, StringComparison.Ordinal) ||
            !string.Equals(result.ActionId, actionId, StringComparison.Ordinal))
            throw new ArgumentException("The result identity must match the cache key.", nameof(result));
        var key = new RuntimeActionCacheKey(matchId, revision, actionId);
        lock (_gate)
        {
            if (_results.ContainsKey(key)) return false;
            _results.Add(key, new CacheEntry(result, _clock()));
            return true;
        }
    }

    private static bool Valid(string matchId, long revision, string actionId) =>
        !string.IsNullOrWhiteSpace(matchId) && revision >= 0 && !string.IsNullOrWhiteSpace(actionId);

    private sealed class CacheEntry
    {
        public CacheEntry(RuntimeActionResult result, DateTimeOffset createdAt) { Result = result; CreatedAt = createdAt; }
        public RuntimeActionResult Result { get; }
        public DateTimeOffset CreatedAt { get; }
    }

    private readonly struct RuntimeActionCacheKey : IEquatable<RuntimeActionCacheKey>
    {
        public RuntimeActionCacheKey(string matchId, long revision, string actionId) { MatchId = matchId; Revision = revision; ActionId = actionId; }
        private string MatchId { get; }
        private long Revision { get; }
        private string ActionId { get; }
        public bool Equals(RuntimeActionCacheKey other) => Revision == other.Revision &&
            string.Equals(MatchId, other.MatchId, StringComparison.Ordinal) &&
            string.Equals(ActionId, other.ActionId, StringComparison.Ordinal);
        public override bool Equals(object? obj) => obj is RuntimeActionCacheKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(StringComparer.Ordinal.GetHashCode(MatchId), Revision, StringComparer.Ordinal.GetHashCode(ActionId));
    }
}
}
