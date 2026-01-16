using System.Security.Cryptography;

namespace SpiderX.Crypto;

/// <summary>
/// Provides encryption/decryption using ChaCha20-Poly1305 (via AES-GCM as fallback)
/// for secure peer-to-peer communication.
/// </summary>
public static class Encryption
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    /// <summary>
    /// Encrypts data using AES-256-GCM with the provided shared secret
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] sharedSecret, byte[]? associatedData = null)
    {
        if (sharedSecret.Length != KeySize)
            throw new ArgumentException($"Shared secret must be {KeySize} bytes", nameof(sharedSecret));

        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using var aes = new AesGcm(sharedSecret, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        // Format: nonce (12) + tag (16) + ciphertext
        byte[] result = new byte[NonceSize + TagSize + ciphertext.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSize);
        ciphertext.CopyTo(result, NonceSize + TagSize);

        return result;
    }

    /// <summary>
    /// Decrypts data using AES-256-GCM with the provided shared secret
    /// </summary>
    public static byte[] Decrypt(byte[] encrypted, byte[] sharedSecret, byte[]? associatedData = null)
    {
        if (sharedSecret.Length != KeySize)
            throw new ArgumentException($"Shared secret must be {KeySize} bytes", nameof(sharedSecret));

        if (encrypted.Length < NonceSize + TagSize)
            throw new ArgumentException("Encrypted data is too short", nameof(encrypted));

        byte[] nonce = encrypted.AsSpan(0, NonceSize).ToArray();
        byte[] tag = encrypted.AsSpan(NonceSize, TagSize).ToArray();
        byte[] ciphertext = encrypted.AsSpan(NonceSize + TagSize).ToArray();
        byte[] plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(sharedSecret, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

        return plaintext;
    }

    /// <summary>
    /// Derives a key from a password using Argon2-like approach (PBKDF2 as fallback)
    /// </summary>
    public static byte[] DeriveKey(string password, byte[] salt, int iterations = 100000)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256);

        return pbkdf2.GetBytes(KeySize);
    }

    /// <summary>
    /// Generates a random salt for key derivation
    /// </summary>
    public static byte[] GenerateSalt(int length = 16)
    {
        return RandomNumberGenerator.GetBytes(length);
    }

    /// <summary>
    /// Computes HMAC-SHA256 for message authentication
    /// </summary>
    public static byte[] ComputeHmac(byte[] key, byte[] message)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(message);
    }

    /// <summary>
    /// Verifies HMAC-SHA256
    /// </summary>
    public static bool VerifyHmac(byte[] key, byte[] message, byte[] expectedHmac)
    {
        byte[] actualHmac = ComputeHmac(key, message);
        return CryptographicOperations.FixedTimeEquals(actualHmac, expectedHmac);
    }

    /// <summary>
    /// Encrypts data for a specific peer using their public key
    /// </summary>
    public static EncryptedEnvelope EncryptForPeer(byte[] plaintext, KeyPair sender, byte[] recipientPublicKey)
    {
        // Compute shared secret using ECDH
        byte[] sharedSecret = sender.ComputeSharedSecret(recipientPublicKey);

        // Encrypt the data
        byte[] encrypted = Encrypt(plaintext, sharedSecret);

        // Sign the encrypted data
        byte[] signature = sender.Sign(encrypted);

        return new EncryptedEnvelope
        {
            SenderId = sender.Id,
            SenderPublicKey = sender.PublicKey,
            EncryptedData = encrypted,
            Signature = signature
        };
    }

    /// <summary>
    /// Decrypts data from a peer
    /// </summary>
    public static byte[] DecryptFromPeer(EncryptedEnvelope envelope, KeyPair recipient)
    {
        // Verify signature
        if (!KeyPair.Verify(envelope.SenderPublicKey, envelope.EncryptedData, envelope.Signature))
            throw new CryptographicException("Invalid signature");

        // Compute shared secret
        byte[] sharedSecret = recipient.ComputeSharedSecret(envelope.SenderPublicKey);

        // Decrypt
        return Decrypt(envelope.EncryptedData, sharedSecret);
    }
}

/// <summary>
/// Encrypted message envelope containing metadata for peer decryption
/// </summary>
public class EncryptedEnvelope
{
    public required SpiderId SenderId { get; init; }
    public required byte[] SenderPublicKey { get; init; }
    public required byte[] EncryptedData { get; init; }
    public required byte[] Signature { get; init; }

    public byte[] Serialize()
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        byte[] senderIdBytes = System.Text.Encoding.UTF8.GetBytes(SenderId.Address);
        writer.Write(senderIdBytes.Length);
        writer.Write(senderIdBytes);

        writer.Write(SenderPublicKey.Length);
        writer.Write(SenderPublicKey);

        writer.Write(EncryptedData.Length);
        writer.Write(EncryptedData);

        writer.Write(Signature.Length);
        writer.Write(Signature);

        return ms.ToArray();
    }

    public static EncryptedEnvelope Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var reader = new BinaryReader(ms);

        int senderIdLength = reader.ReadInt32();
        string senderIdAddress = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(senderIdLength));
        SpiderId senderId = SpiderId.Parse(senderIdAddress);

        int publicKeyLength = reader.ReadInt32();
        byte[] senderPublicKey = reader.ReadBytes(publicKeyLength);

        int encryptedDataLength = reader.ReadInt32();
        byte[] encryptedData = reader.ReadBytes(encryptedDataLength);

        int signatureLength = reader.ReadInt32();
        byte[] signature = reader.ReadBytes(signatureLength);

        return new EncryptedEnvelope
        {
            SenderId = senderId,
            SenderPublicKey = senderPublicKey,
            EncryptedData = encryptedData,
            Signature = signature
        };
    }
}
