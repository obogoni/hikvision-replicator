using System.Security.Cryptography;
using System.Text;
using HikvisionReplicator.Api.Shared;
using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// AES-256-CBC with a fresh IV per call. Ciphertext format: base64(IV):base64(ciphertext).
/// </summary>
public sealed class EncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    public EncryptionService(IOptions<EncryptionOptions> options)
    {
        // The key is validated at startup (EncryptionOptionsValidator + ValidateOnStart).
        _key = Convert.FromBase64String(options.Value.Key!);
    }

    public string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertextBytes = encryptor.TransformFinalBlock(
            plaintextBytes,
            0,
            plaintextBytes.Length
        );

        return $"{Convert.ToBase64String(aes.IV)}:{Convert.ToBase64String(ciphertextBytes)}";
    }

    public string Decrypt(string ciphertext)
    {
        var parts = ciphertext.Split(':', 2);
        if (parts.Length != 2)
            throw new FormatException("Invalid ciphertext format.");

        var iv = Convert.FromBase64String(parts[0]);
        var ciphertextBytes = Convert.FromBase64String(parts[1]);

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plaintextBytes = decryptor.TransformFinalBlock(
            ciphertextBytes,
            0,
            ciphertextBytes.Length
        );
        return Encoding.UTF8.GetString(plaintextBytes);
    }
}
