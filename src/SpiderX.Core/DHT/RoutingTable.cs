using SpiderX.Crypto;

namespace SpiderX.Core.DHT;

/// <summary>
/// Kademlia-style routing table for distributed peer discovery.
/// Organizes peers by XOR distance from local node.
/// </summary>
public class RoutingTable
{
    private readonly KBucket[] _buckets;
    private readonly SpiderId _localId;
    private readonly object _lock = new();

    /// <summary>
    /// Number of bits in the ID (160 bits like SHA1/RIPEMD160)
    /// </summary>
    public const int IdBits = 160;

    /// <summary>
    /// Total number of nodes in the routing table
    /// </summary>
    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _buckets.Sum(b => b.Count);
            }
        }
    }

    /// <summary>
    /// Creates a new routing table for the local node
    /// </summary>
    public RoutingTable(SpiderId localId)
    {
        _localId = localId;
        _buckets = new KBucket[IdBits];

        for (int i = 0; i < IdBits; i++)
        {
            _buckets[i] = new KBucket(i);
        }
    }

    /// <summary>
    /// Adds or updates a node in the routing table
    /// </summary>
    public bool AddNode(DhtNode node)
    {
        if (node.Id == _localId)
            return false; // Don't add ourselves

        int bucketIndex = GetBucketIndex(node.Id);

        lock (_lock)
        {
            return _buckets[bucketIndex].TryAdd(node);
        }
    }

    /// <summary>
    /// Removes a node from the routing table
    /// </summary>
    public bool RemoveNode(SpiderId id)
    {
        if (id == _localId)
            return false;

        int bucketIndex = GetBucketIndex(id);

        lock (_lock)
        {
            return _buckets[bucketIndex].Remove(id);
        }
    }

    /// <summary>
    /// Gets the K closest nodes to a target ID
    /// </summary>
    public IReadOnlyList<DhtNode> GetClosestNodes(SpiderId targetId, int count = KBucket.K)
    {
        var result = new List<DhtNode>();
        int targetBucket = targetId == _localId ? 0 : GetBucketIndex(targetId);

        lock (_lock)
        {
            // Start from target bucket and expand outward
            for (int distance = 0; distance < IdBits && result.Count < count; distance++)
            {
                // Check bucket at targetBucket + distance
                int index = targetBucket + distance;
                if (index < IdBits)
                {
                    result.AddRange(_buckets[index].GetNodes());
                }

                // Check bucket at targetBucket - distance (if different)
                if (distance > 0)
                {
                    index = targetBucket - distance;
                    if (index >= 0)
                    {
                        result.AddRange(_buckets[index].GetNodes());
                    }
                }
            }
        }

        // Sort by XOR distance to target and take closest
        return result
            .OrderBy(n => GetDistance(n.Id, targetId))
            .Take(count)
            .ToList();
    }

    /// <summary>
    /// Gets all nodes in the routing table
    /// </summary>
    public IReadOnlyList<DhtNode> GetAllNodes()
    {
        lock (_lock)
        {
            return _buckets.SelectMany(b => b.GetNodes()).ToList();
        }
    }

    /// <summary>
    /// Gets buckets that need refreshing (haven't been updated recently)
    /// </summary>
    public IReadOnlyList<int> GetStaleBuckets(TimeSpan maxAge)
    {
        var cutoff = DateTime.UtcNow - maxAge;

        lock (_lock)
        {
            return _buckets
                .Where(b => b.Count > 0 && b.LastUpdated < cutoff)
                .Select(b => b.Index)
                .ToList();
        }
    }

    /// <summary>
    /// Gets the bucket index for a node ID based on XOR distance
    /// </summary>
    private int GetBucketIndex(SpiderId id)
    {
        return _localId.BucketIndex(id);
    }

    /// <summary>
    /// Calculates XOR distance between two IDs
    /// </summary>
    private static byte[] GetDistance(SpiderId a, SpiderId b)
    {
        return a.DistanceTo(b);
    }

    /// <summary>
    /// Generates a random ID in a specific bucket (for refresh)
    /// </summary>
    public SpiderId GenerateIdInBucket(int bucketIndex)
    {
        var hash = new byte[20];
        Random.Shared.NextBytes(hash);

        // Set the appropriate prefix bits to fall into the desired bucket
        var localHash = _localId.Hash;

        // Copy local hash bits up to bucketIndex
        int fullBytes = bucketIndex / 8;
        int remainingBits = bucketIndex % 8;

        for (int i = 0; i < fullBytes; i++)
        {
            hash[i] = localHash[i];
        }

        if (fullBytes < hash.Length)
        {
            // Keep first remainingBits the same, flip the next one
            byte mask = (byte)(0xFF << (8 - remainingBits));
            hash[fullBytes] = (byte)((localHash[fullBytes] & mask) | ((~localHash[fullBytes]) & ~mask));

            // Ensure the bit at bucketIndex is different
            byte bitMask = (byte)(0x80 >> remainingBits);
            hash[fullBytes] ^= bitMask;
        }

        return SpiderId.FromHash(hash);
    }
}
