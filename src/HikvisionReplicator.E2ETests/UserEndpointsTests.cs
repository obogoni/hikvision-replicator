using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace HikvisionReplicator.E2ETests;

internal sealed record UserResponse(
    [property: JsonPropertyName("id")]          int      Id,
    [property: JsonPropertyName("externalRef")] string   ExternalRef,
    [property: JsonPropertyName("name")]        string   Name,
    [property: JsonPropertyName("accessCode")]  string   AccessCode,
    [property: JsonPropertyName("createdAt")]   DateTime CreatedAt,
    [property: JsonPropertyName("updatedAt")]   DateTime UpdatedAt
);

[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class UserEndpointsTests : PlaywrightTest
{
    private static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5000";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private IAPIRequestContext _api = null!;

    [SetUp]
    public async Task SetUp()
    {
        _api = await Playwright.APIRequest.NewContextAsync(new APIRequestNewContextOptions
        {
            BaseURL           = BaseUrl,
            IgnoreHTTPSErrors = true,
        });
    }

    [TearDown]
    public async Task TearDown()
    {
        await _api.DisposeAsync();
    }

    [Test]
    public async Task Post_ValidUser_Returns201WithExpectedBody()
    {
        var externalRef = $"e2e-{Guid.NewGuid()}";
        var payload = new
        {
            externalRef,
            name       = "E2E User",
            accessCode = "1234",
        };

        var response = await _api.PostAsync("/api/users", new APIRequestContextOptions { DataObject = payload });

        Assert.That(response.Status, Is.EqualTo(201));

        var json = await response.TextAsync();
        var user = JsonSerializer.Deserialize<UserResponse>(json, JsonOptions);

        Assert.That(user,              Is.Not.Null);
        Assert.That(user!.Id,          Is.GreaterThan(0));
        Assert.That(user.Name,         Is.EqualTo("E2E User"));
        Assert.That(user.ExternalRef,  Is.EqualTo(externalRef));
        Assert.That(user.AccessCode,   Is.EqualTo("1234"));
        Assert.That(json,              Does.Not.Contain("facePic"));
    }
}
