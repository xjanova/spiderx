using SpiderX.Crypto;

namespace SpiderX.Core.DHT;

/// <summary>
/// K-Bucket for Kademlia DHT routing table.
/// Each bucket stores up to K peers at a specific distance from the local node.
/// </summary>
public class KBucket
{
    public const int K = 20; // Standard Kademlia K value
    private readonly List<DhtNode> _nodes = [];
    private readonly List<DhtNode> _replacementCache = [];
    private readonly object _lock = new();

    /// <summary>
    /// Gets the index of this bucket (0 = closest, 159 = farthest for 160-bit IDs).
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Gets the number of nodes in this bucket.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _nodes.Count;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether this bucket is full.
    /// </summary>
    public bool IsFull => Count >= K;

    /// <summary>
    /// Gets the last time this bucket was updated.
    /// </summary>
    public DateTime LastUpdated { get; private set; } = DateTime.UtcNow;

    public KBucket(int index)
    {
        if (index < 0 || index >= 160)
            throw new ArgumentOutOfRangeException(nameof(index), "Bucket index must be between 0 and 159");

        Index = index;
    }

    /// <summary>
    /// Attempts to add a node to the bucket
    /// </summary>
    /// <returns>True if the node was added or updated, false if bucket is full</returns>
    public bool TryAdd(DhtNode node)
    {
        lock (_lock)
        {
            // Check if node already exists
            var existingIndex = _nodes.FindIndex(n => n.Id == node.Id);
            if (existingIndex >= 0)
            {
                // Move to end (most recently seen)
                var existing = _nodes[existingIndex];
                _nodes.RemoveAt(existingIndex);
                existing.LastSeen = DateTime.UtcNow;
                _nodes.Add(existing);
                LastUpdated = DateTime.UtcNow;
                return true;
            }

            // If bucket is not full, add the node
            if (_nodes.Count < K)
            {
                _nodes.Add(node);
                LastUpdated = DateTime.UtcNow;
                return true;
            }

            // Bucket is full - add to replacement cache
            if (_replacementCache.Count < K)
            {
                _replacementCache.Add(node);
            }

            return false;
        }
    }

    /// <summary>
    /// Removes a node from the bucket
    /// </summary>
    public bool Remove(SpiderId id)
    {
        lock (_lock)
        {
            var removed = _nodes.RemoveAll(n => n.Id == id) > 0;

            // If removed and there's a replacement, use it
            if (removed && _replacementCache.Count > 0)
            {
                var replacement = _replacementCache[0];
                _replacementCache.RemoveAt(0);
                _nodes.Add(replacement);
            }

            return removed;
        }
    }

    /// <summary>
    /// Gets all nodes in this bucket
    /// </summary>
    public IReadOnlyList<DhtNode> GetNodes()
    {
        lock (_lock)
        {
            return _nodes.ToList();
        }
    }

    /// <summary>
    /// Gets the oldest node (least recently seen)
    /// </summary>
    public DhtNode? GetOldestNode()
    {
        lock (_lock)
        {
            return _nodes.FirstOrDefault();
        }
    }

    /// <summary>
    /// Gets the closest N nodes to a target ID
    /// </summary>
    public IReadOnlyList<DhtNode> GetClosest(SpiderId targetId, int count)
    {
        lock (_lock)
        {
            return _nodes
                .OrderBy(n => CompareDistance(n.Id, targetId))
                .Take(count)
                .ToList();
        }
    }

    private static int CompareDistance(SpiderId a, SpiderId b)
    {
        var distA = a.Hash;
        var distB = b.Hash;

        for (int i = 0; i < distA.Length; i++)
        {
            if (distA[i] < distB[i]) return -1;
            if (distA[i] > distB[i]) return 1;
        }

        return 0;
    }
}

/// <summary>
/// Represents a node in the DHT
/// </summary>
public class DhtNode
{
    /// <summary>
    /// Gets the unique identifier of this node.
    /// </summary>
    public required SpiderId Id { get; init; }

    /// <summary>
    /// Gets the IP address of this node.
    /// </summary>
    public required string Address { get; init; }

    /// <summary>
    /// Gets the port number of this node.
    /// </summary>
    public required int Port { get; init; }

    /// <summary>
    /// Gets or sets the last time this node was seen.
    /// </summary>
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Gets or sets the number of consecutive failures.
    /// </summary>
    public int FailCount { get; set; }

    /// <summary>
    /// Gets a value indicating whether this node is considered stale (not responding).
    /// </summary>
    public bool IsStale => FailCount > 2 || (DateTime.UtcNow - LastSeen).TotalMinutes > 15;
}
