using HikvisionReplicator.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Tests.Domain;

[Trait("Category", "Unit")]
public class EncryptionServiceTests
{
    private static EncryptionService WithValidKey()
    {
        var key = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)
        );
        return new EncryptionService(Options.Create(new EncryptionOptions { Key = key }));
    }

    private static ValidateOptionsResult Validate(string? key) =>
        new EncryptionOptionsValidator().Validate(null, new EncryptionOptions { Key = key });

    // ─── DEV-07: the stored credential is recoverable but never readable ──

    [Fact]
    public void Password_survives_a_round_trip_unchanged()
    {
        var service = WithValidKey();

        var recovered = service.Decrypt(service.Encrypt("hunter2-admin-password"));

        Assert.Equal("hunter2-admin-password", recovered);
    }

    [Fact]
    public void Password_with_multi_byte_characters_survives_a_round_trip_unchanged()
    {
        var service = WithValidKey();
        const string password = "señha-日本語-Ωμέγα-🔐";

        var recovered = service.Decrypt(service.Encrypt(password));

        Assert.Equal(password, recovered);
    }

    [Fact]
    public void Encrypting_one_password_twice_produces_different_ciphertext()
    {
        var service = WithValidKey();

        var first = service.Encrypt("hunter2-admin-password");
        var second = service.Encrypt("hunter2-admin-password");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Every_ciphertext_of_one_password_decrypts_back_to_it()
    {
        var service = WithValidKey();

        var first = service.Encrypt("hunter2-admin-password");
        var second = service.Encrypt("hunter2-admin-password");

        Assert.Equal("hunter2-admin-password", service.Decrypt(first));
        Assert.Equal("hunter2-admin-password", service.Decrypt(second));
    }

    [Fact]
    public void Ciphertext_never_contains_the_password()
    {
        var service = WithValidKey();
        const string password = "hunter2-admin-password";

        var ciphertext = service.Encrypt(password);

        Assert.DoesNotContain(password, ciphertext, StringComparison.Ordinal);
    }

    // ─── DEV-15: the key configuration rule ───────────────────────────────

    [Fact]
    public void Key_of_the_required_length_is_accepted()
    {
        var result = Validate(Convert.ToBase64String(new byte[32]));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Missing_key_is_rejected_with_a_message_naming_the_setting()
    {
        var result = Validate(null);

        Assert.True(result.Failed);
        Assert.Contains("Encryption:Key", result.FailureMessage);
        Assert.Equal(EncryptionOptionsValidator.MissingKeyMessage, result.FailureMessage);
    }

    [Fact]
    public void Blank_key_is_rejected_with_a_message_naming_the_setting()
    {
        var result = Validate("   ");

        Assert.True(result.Failed);
        Assert.Contains("Encryption:Key", result.FailureMessage);
        Assert.Equal(EncryptionOptionsValidator.MissingKeyMessage, result.FailureMessage);
    }

    [Fact]
    public void Key_that_is_not_base64_is_rejected_with_a_message_naming_the_setting()
    {
        var result = Validate("this is not base64!!");

        Assert.True(result.Failed);
        Assert.Contains("Encryption:Key", result.FailureMessage);
        Assert.Equal(EncryptionOptionsValidator.MalformedKeyMessage, result.FailureMessage);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public void Key_that_does_not_decode_to_thirty_two_bytes_is_rejected(int lengthInBytes)
    {
        var result = Validate(Convert.ToBase64String(new byte[lengthInBytes]));

        Assert.True(result.Failed);
        Assert.Contains("Encryption:Key", result.FailureMessage);
        Assert.Equal(EncryptionOptionsValidator.WrongLengthKeyMessage, result.FailureMessage);
    }
}
