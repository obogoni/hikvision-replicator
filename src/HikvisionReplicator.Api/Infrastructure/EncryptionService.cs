using System.Security.Cryptography;
using System.Text;

namespace HikvisionReplicator.Api.Infrastructure;

public interface IEncryptionService
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}

public class EncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    public EncryptionService(IConfiguration configuration)
    {
        var keyBase64 = configuration["Encryption:Key"]
            ?? throw new InvalidOperationException("Encryption:Key is not configured.");
        _key = Convert.FromBase64String(keyBase64);
        if (_key.Length != 32)
            throw new InvalidOperationException("Encryption:Key must be a 32-byte Base64 string.");
    }

    public string Encrypt(string plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertextBytes = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

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
        var plaintextBytes = decryptor.TransformFinalBlock(ciphertextBytes, 0, ciphertextBytes.Length);
        return Encoding.UTF8.GetString(plaintextBytes);
    }
}
