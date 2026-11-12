using HikvisionReplicator.Api.Infrastructure.Isapi;
using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Tests.Infrastructure.Isapi;

/// <summary>
/// The adapter's configuration (A-6, A-8, ISA-11, ISA-37): the shipped defaults start, and a
/// setting that cannot work stops startup naming itself — every one of them at once, so an
/// operator fixes the file in one pass rather than one restart per mistake.
/// </summary>
public class IsapiOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(Action<IsapiOptions> configure)
    {
        var options = new IsapiOptions();
        configure(options);
        return new IsapiOptionsValidator().Validate(null, options);
    }

    [Fact]
    public void The_shipped_defaults_are_valid()
    {
        var result = Validate(_ => { });

        Assert.True(result.Succeeded, result.FailureMessage);
    }

    [Fact]
    public void The_shipped_defaults_are_the_designed_reader_constants()
    {
        var options = new IsapiOptions();

        Assert.Equal(32, options.NameMaxLength);
        Assert.Equal("1", options.FaceLibraryId);
        Assert.Equal("blackFD", options.FaceLibraryType);
        Assert.Equal(1, options.DoorNo);
        Assert.Equal("1", options.PlanTemplateNo);
        Assert.Equal(new DateTime(2000, 1, 1, 0, 0, 0), options.ValidFrom);
        Assert.Equal(new DateTime(2037, 12, 31, 23, 59, 59), options.ValidTo);
        Assert.Equal(["deviceUserAlreadyExist"], options.AlreadyExistsSubStatusCodes);
        Assert.Empty(options.AbsentSubStatusCodes);
        Assert.Equal(TimeSpan.FromMinutes(5), options.ConnectionLifetime);
        Assert.Equal(TimeSpan.FromMinutes(30), options.AuthFailureHold);
    }

    public static TheoryData<string, Action<IsapiOptions>> InvalidSettings =>
        new()
        {
            { "Isapi:NameMaxLength", options => options.NameMaxLength = 0 },
            { "Isapi:NameMaxLength", options => options.NameMaxLength = -1 },
            { "Isapi:ConnectionLifetime", options => options.ConnectionLifetime = TimeSpan.Zero },
            {
                "Isapi:ConnectionLifetime",
                options => options.ConnectionLifetime = TimeSpan.FromSeconds(-1)
            },
            { "Isapi:AuthFailureHold", options => options.AuthFailureHold = TimeSpan.Zero },
            {
                "Isapi:AuthFailureHold",
                options => options.AuthFailureHold = TimeSpan.FromMinutes(-1)
            },
            { "Isapi:ValidFrom", options => options.ValidFrom = options.ValidTo },
            { "Isapi:ValidFrom", options => options.ValidFrom = options.ValidTo.AddSeconds(1) },
            { "Isapi:FaceLibraryId", options => options.FaceLibraryId = "" },
            { "Isapi:FaceLibraryId", options => options.FaceLibraryId = "  " },
            { "Isapi:FaceLibraryType", options => options.FaceLibraryType = "" },
            { "Isapi:PlanTemplateNo", options => options.PlanTemplateNo = " " },
            { "Isapi:DoorNo", options => options.DoorNo = 0 },
        };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void A_setting_that_cannot_work_fails_naming_itself(
        string setting,
        Action<IsapiOptions> configure
    )
    {
        var result = Validate(configure);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures!);
        Assert.Contains(setting, failure, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_invalid_setting_is_reported_not_only_the_first()
    {
        var result = Validate(options =>
        {
            options.NameMaxLength = 0;
            options.DoorNo = 0;
        });

        Assert.True(result.Failed);
        Assert.Equal(2, result.Failures!.Count());
        Assert.Contains(result.Failures!, failure => failure.Contains("Isapi:NameMaxLength", StringComparison.Ordinal));
        Assert.Contains(result.Failures!, failure => failure.Contains("Isapi:DoorNo", StringComparison.Ordinal));
    }
}
