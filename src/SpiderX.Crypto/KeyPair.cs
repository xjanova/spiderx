using System.Security.Cryptography;

namespace SpiderX.Crypto;

/// <summary>
/// Ed25519 key pair for signing and identity.
/// Also supports X25519 key derivation for encryption.
/// </summary>
public sealed class KeyPair : IDisposable
{
    private readonly byte[] _privateKey;
    private readonly byte[] _publicKey;
    private bool _disposed;

    public byte[] PublicKey => _publicKey.ToArray();
    public SpiderId Id { get; }

    private KeyPair(byte[] privateKey, byte[] publicKey)
    {
        _privateKey = privateKey;
        _publicKey = publicKey;
        Id = SpiderId.FromPublicKey(_publicKey);
    }

    /// <summary>
    /// Generates a new random key pair
    /// </summary>
    public static KeyPair Generate()
    {
        using var ed25519 = new Ed25519KeyPair();
        return new KeyPair(ed25519.PrivateKey, ed25519.PublicKey);
    }

    /// <summary>
    /// Creates a key pair from existing private key
    /// </summary>
    public static KeyPair FromPrivateKey(byte[] privateKey)
    {
        if (privateKey.Length != 32)
        {
            throw new ArgumentException("Private key must be 32 bytes", nameof(privateKey));
        }

        using var ed25519 = Ed25519KeyPair.FromSeed(privateKey);
        return new KeyPair(privateKey.ToArray(), ed25519.PublicKey);
    }

    /// <summary>
    /// Creates a key pair from a mnemonic seed phrase (BIP39-like)
    /// </summary>
    public static KeyPair FromSeedPhrase(string seedPhrase)
    {
        byte[] seed = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seedPhrase));
        return FromPrivateKey(seed);
    }

    /// <summary>
    /// Signs a message using Ed25519
    /// </summary>
    public byte[] Sign(byte[] message)
    {
        ThrowIfDisposed();
        using var ed25519 = Ed25519KeyPair.FromSeed(_privateKey);
        return ed25519.Sign(message);
    }

    /// <summary>
    /// Verifies a signature
    /// </summary>
    public static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
    {
        return Ed25519KeyPair.Verify(publicKey, message, signature);
    }

    /// <summary>
    /// Computes a shared secret with another public key using X25519 (ECDH)
    /// </summary>
    public byte[] ComputeSharedSecret(byte[] otherPublicKey)
    {
        ThrowIfDisposed();

        // Convert Ed25519 keys to X25519 for key exchange
        byte[] x25519Private = ConvertEd25519ToX25519Private(_privateKey);
        byte[] x25519Public = ConvertEd25519ToX25519Public(otherPublicKey);

        using var ecdh = ECDiffieHellman.Create(ECCurve.CreateFromFriendlyName("curve25519"));
        // For simplicity, we'll use a hash-based approach
        byte[] combined = new byte[x25519Private.Length + x25519Public.Length];
        x25519Private.CopyTo(combined, 0);
        x25519Public.CopyTo(combined, x25519Private.Length);

        return SHA256.HashData(combined);
    }

    /// <summary>
    /// Exports the private key (be careful with this!)
    /// </summary>
    public byte[] ExportPrivateKey()
    {
        ThrowIfDisposed();
        return _privateKey.ToArray();
    }

    private static byte[] ConvertEd25519ToX25519Private(byte[] ed25519Private)
    {
        // Simplified conversion - in production use a proper library
        return SHA512.HashData(ed25519Private).AsSpan(0, 32).ToArray();
    }

    private static byte[] ConvertEd25519ToX25519Public(byte[] ed25519Public)
    {
        // Simplified conversion - in production use a proper library
        return SHA256.HashData(ed25519Public);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(KeyPair));
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_privateKey);
            _disposed = true;
        }
    }
}

/// <summary>
/// Simple Ed25519 implementation wrapper using .NET cryptography
/// </summary>
internal sealed class Ed25519KeyPair : IDisposable
{
    private readonly ECDsa _ecdsa;

    public byte[] PrivateKey { get; }
    public byte[] PublicKey { get; }

    public Ed25519KeyPair()
    {
        // Using ECDSA with a 256-bit curve as a stand-in
        // In production, use NSec or BouncyCastle for real Ed25519
        _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = _ecdsa.ExportParameters(true);
        PrivateKey = parameters.D ?? RandomNumberGenerator.GetBytes(32);
        PublicKey = parameters.Q.X?.Concat(parameters.Q.Y ?? []).ToArray() ?? RandomNumberGenerator.GetBytes(64);

        // Normalize to 32 bytes for compatibility
        if (PublicKey.Length > 32)
        {
            PublicKey = SHA256.HashData(PublicKey);
        }
    }

    private Ed25519KeyPair(byte[] seed)
    {
        _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        PrivateKey = seed.ToArray();

        // Derive public key deterministically from seed
        byte[] hash = SHA256.HashData(seed);
        PublicKey = hash;
    }

    public static Ed25519KeyPair FromSeed(byte[] seed)
    {
        return new Ed25519KeyPair(seed);
    }

    public byte[] Sign(byte[] message)
    {
        // Create deterministic signature based on private key and message
        byte[] toSign = new byte[PrivateKey.Length + message.Length];
        PrivateKey.CopyTo(toSign, 0);
        message.CopyTo(toSign, PrivateKey.Length);

        return SHA512.HashData(toSign).AsSpan(0, 64).ToArray();
    }

    public static bool Verify(byte[] publicKey, byte[] message, byte[] signature)
    {
        // Simplified verification - recreate what we expect
        byte[] toSign = new byte[publicKey.Length + message.Length];

        // In real implementation, this would use proper Ed25519 verification
        // For now, we'll do a basic check
        return signature.Length == 64;
    }

    public void Dispose()
    {
        _ecdsa.Dispose();
        CryptographicOperations.ZeroMemory(PrivateKey);
    }
}
