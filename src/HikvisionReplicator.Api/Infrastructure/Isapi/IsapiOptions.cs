using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Api.Infrastructure.Isapi;

/// <summary>
/// Every ISAPI constant the bench UAT may correct, held as configuration rather than code
/// (AD-033), so a correction to A-1, A-6, A-8 or the reader's sub-status codes is a settings
/// change.
/// </summary>
public sealed class IsapiOptions
{
    public const string SectionName = "Isapi";

    /// <summary>A-6: the reader's name limit. <i>Unverified</i>; replaced by the UAT measurement (ISA-36).</summary>
    public int NameMaxLength { get; set; } = 32;

    /// <summary>A-1: the face library every spectator's face is written to.</summary>
    public string FaceLibraryId { get; set; } = "1";

    /// <summary>A-1: the face library's type.</summary>
    public string FaceLibraryType { get; set; } = "blackFD";

    /// <summary>A-8: the one door every spectator is granted.</summary>
    public int DoorNo { get; set; } = 1;

    /// <summary>A-8: the plan template that door right runs under.</summary>
    public string PlanTemplateNo { get; set; } = "1";

    /// <summary>A-8: the start of a validity window that never expires in practice. Reader-local time.</summary>
    public DateTime ValidFrom { get; set; } = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

    /// <summary>A-8: the end of that window. Reader-local time.</summary>
    public DateTime ValidTo { get; set; } = new(2037, 12, 31, 23, 59, 59, DateTimeKind.Unspecified);

    /// <summary>ISA-02: the reader's ways of saying the person is already there.</summary>
    public IList<string> AlreadyExistsSubStatusCodes { get; set; } = ["deviceUserAlreadyExist"];

    /// <summary>ISA-11: the reader's ways of saying there is nothing to delete. Unknown until the UAT.</summary>
    public IList<string> AbsentSubStatusCodes { get; set; } = [];

    /// <summary>How long one pooled connection to a reader is reused before it is replaced.</summary>
    public TimeSpan ConnectionLifetime { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>ISA-37: how long refused credentials are held when the reader gives no unlock time.</summary>
    public TimeSpan AuthFailureHold { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// Validates <see cref="IsapiOptions"/>, reporting every failure rather than the first, so one
/// start-up names everything an operator has to fix. Wired with ValidateOnStart().
/// </summary>
public sealed class IsapiOptionsValidator : IValidateOptions<IsapiOptions>
{
    private const string Section = IsapiOptions.SectionName;

    public ValidateOptionsResult Validate(string? name, IsapiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.NameMaxLength <= 0)
            failures.Add($"{Section}:NameMaxLength must be positive.");

        if (string.IsNullOrWhiteSpace(options.FaceLibraryId))
            failures.Add($"{Section}:FaceLibraryId must not be blank.");

        if (string.IsNullOrWhiteSpace(options.FaceLibraryType))
            failures.Add($"{Section}:FaceLibraryType must not be blank.");

        if (options.DoorNo < 1)
            failures.Add($"{Section}:DoorNo must be at least 1.");

        if (string.IsNullOrWhiteSpace(options.PlanTemplateNo))
            failures.Add($"{Section}:PlanTemplateNo must not be blank.");

        if (options.ValidFrom >= options.ValidTo)
            failures.Add($"{Section}:ValidFrom must be earlier than {Section}:ValidTo.");

        if (options.ConnectionLifetime <= TimeSpan.Zero)
            failures.Add($"{Section}:ConnectionLifetime must be positive.");

        if (options.AuthFailureHold <= TimeSpan.Zero)
            failures.Add($"{Section}:AuthFailureHold must be positive.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
