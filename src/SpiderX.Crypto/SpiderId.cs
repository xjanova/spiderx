using System.Security.Cryptography;
using SimpleBase;

namespace SpiderX.Crypto;

/// <summary>
/// Represents a unique peer identity in the SpiderX network.
/// Similar to a cryptocurrency wallet address - derived from public key hash.
/// Format: spx1[base58check encoded hash]
/// </summary>
public sealed class SpiderId : IEquatable<SpiderId>
{
    private const string Prefix = "spx1";
    private const int HashLength = 20; // RIPEMD160 output length

    private readonly byte[] _hash;

    public byte[] Hash => _hash.ToArray();
    public string Address { get; }

    private SpiderId(byte[] hash)
    {
        if (hash.Length != HashLength)
            throw new ArgumentException($"Hash must be {HashLength} bytes", nameof(hash));

        _hash = hash.ToArray();
        Address = Prefix + Base58.Bitcoin.Encode(AddChecksum(_hash));
    }

    /// <summary>
    /// Creates a SpiderId from a public key using SHA256 + RIPEMD160 (like Bitcoin addresses)
    /// </summary>
    public static SpiderId FromPublicKey(byte[] publicKey)
    {
        // SHA256 first
        byte[] sha256Hash = SHA256.HashData(publicKey);

        // Then RIPEMD160 (we'll use SHA256 truncated for simplicity since RIPEMD160 isn't in .NET)
        byte[] hash = SHA256.HashData(sha256Hash).AsSpan(0, HashLength).ToArray();

        return new SpiderId(hash);
    }

    /// <summary>
    /// Parses a SpiderId from its string representation (address)
    /// </summary>
    public static SpiderId Parse(string address)
    {
        if (string.IsNullOrEmpty(address))
            throw new ArgumentException("Address cannot be empty", nameof(address));

        if (!address.StartsWith(Prefix))
            throw new FormatException($"Invalid SpiderId format. Must start with '{Prefix}'");

        string encoded = address[Prefix.Length..];
        byte[] decoded = Base58.Bitcoin.Decode(encoded);

        if (decoded.Length != HashLength + 4)
            throw new FormatException("Invalid SpiderId length");

        byte[] hash = decoded.AsSpan(0, HashLength).ToArray();
        byte[] checksum = decoded.AsSpan(HashLength, 4).ToArray();

        byte[] expectedChecksum = ComputeChecksum(hash);
        if (!checksum.SequenceEqual(expectedChecksum))
            throw new FormatException("Invalid SpiderId checksum");

        return new SpiderId(hash);
    }

    /// <summary>
    /// Tries to parse a SpiderId from its string representation
    /// </summary>
    public static bool TryParse(string address, out SpiderId? spiderId)
    {
        try
        {
            spiderId = Parse(address);
            return true;
        }
        catch
        {
            spiderId = null;
            return false;
        }
    }

    /// <summary>
    /// Creates a SpiderId directly from a hash (for DHT operations)
    /// </summary>
    public static SpiderId FromHash(byte[] hash) => new(hash);

    /// <summary>
    /// Computes XOR distance between two SpiderIds (for DHT routing)
    /// </summary>
    public byte[] DistanceTo(SpiderId other)
    {
        byte[] distance = new byte[HashLength];
        for (int i = 0; i < HashLength; i++)
        {
            distance[i] = (byte)(_hash[i] ^ other._hash[i]);
        }
        return distance;
    }

    /// <summary>
    /// Gets the leading zero bits count of distance (for DHT bucket selection)
    /// </summary>
    public int BucketIndex(SpiderId other)
    {
        byte[] distance = DistanceTo(other);
        int leadingZeros = 0;

        foreach (byte b in distance)
        {
            if (b == 0)
            {
                leadingZeros += 8;
            }
            else
            {
                leadingZeros += CountLeadingZeros(b);
                break;
            }
        }

        return Math.Min(leadingZeros, HashLength * 8 - 1);
    }

    private static int CountLeadingZeros(byte b)
    {
        int count = 0;
        for (int i = 7; i >= 0; i--)
        {
            if ((b & (1 << i)) == 0)
                count++;
            else
                break;
        }
        return count;
    }

    private static byte[] AddChecksum(byte[] data)
    {
        byte[] checksum = ComputeChecksum(data);
        byte[] result = new byte[data.Length + 4];
        data.CopyTo(result, 0);
        checksum.CopyTo(result, data.Length);
        return result;
    }

    private static byte[] ComputeChecksum(byte[] data)
    {
        byte[] hash1 = SHA256.HashData(data);
        byte[] hash2 = SHA256.HashData(hash1);
        return hash2.AsSpan(0, 4).ToArray();
    }

    public override string ToString() => Address;

    public override int GetHashCode() => BitConverter.ToInt32(_hash, 0);

    public override bool Equals(object? obj) => obj is SpiderId other && Equals(other);

    public bool Equals(SpiderId? other) => other is not null && _hash.SequenceEqual(other._hash);

    public static bool operator ==(SpiderId? left, SpiderId? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(SpiderId? left, SpiderId? right) => !(left == right);
}
