using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Api.Infrastructure;

public sealed class EncryptionOptions
{
    public const string SectionName = "Encryption";
    public const int RequiredKeyLengthInBytes = 32;

    public string? Key { get; set; }
}

/// <summary>
/// Validates the encryption key. Wired with ValidateOnStart() so a missing or
/// malformed key aborts startup instead of failing on the first registration (DEV-15).
/// </summary>
public sealed class EncryptionOptionsValidator : IValidateOptions<EncryptionOptions>
{
    public const string KeyPath = $"{EncryptionOptions.SectionName}:Key";

    public const string MissingKeyMessage =
        $"{KeyPath} is not configured. Set it to a Base64-encoded 32-byte key.";

    public const string MalformedKeyMessage =
        $"{KeyPath} is not valid Base64. Set it to a Base64-encoded 32-byte key.";

    public const string WrongLengthKeyMessage =
        $"{KeyPath} must decode to exactly 32 bytes for AES-256.";

    public ValidateOptionsResult Validate(string? name, EncryptionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Key))
            return ValidateOptionsResult.Fail(MissingKeyMessage);

        byte[] key;
        try
        {
            key = Convert.FromBase64String(options.Key);
        }
        catch (FormatException)
        {
            return ValidateOptionsResult.Fail(MalformedKeyMessage);
        }

        return key.Length != EncryptionOptions.RequiredKeyLengthInBytes
            ? ValidateOptionsResult.Fail(WrongLengthKeyMessage)
            : ValidateOptionsResult.Success;
    }
}
