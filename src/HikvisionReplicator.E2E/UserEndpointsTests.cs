using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace HikvisionReplicator.E2E;

/// <summary>Carries the face picture's fingerprint and never its bytes (USR-37).</summary>
internal sealed record UserResponse(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("externalRef")] string ExternalRef,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("accessCode")] string AccessCode,
    [property: JsonPropertyName("faceContentHash")] string FaceContentHash,
    [property: JsonPropertyName("faceByteSize")] int FaceByteSize,
    [property: JsonPropertyName("faceWidth")] int FaceWidth,
    [property: JsonPropertyName("faceHeight")] int FaceHeight
);

internal sealed record UserPage(
    [property: JsonPropertyName("items")] List<UserResponse> Items,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("hasMore")] bool HasMore
);

/// <summary>
/// An out-of-process confirmation that the four user routes behave over real HTTP against a live
/// API. Depth lives in the integration suite (AD-024); this is the thin end-to-end pass — one
/// happy path and one failure per route, and nothing more. The target is <c>E2E_BASE_URL</c>,
/// defaulting to the local development address.
/// </summary>
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

    /// <summary>
    /// A real photograph's worth of entropy, sent as base64 exactly as A-9 describes. Generated,
    /// not photographed — see <c>tests/assets/PROVENANCE.md</c>.
    /// </summary>
    private static readonly string FacePicture = Convert.ToBase64String(
        File.ReadAllBytes(
            Path.Combine(AppContext.BaseDirectory, "assets", "exif-rotated-portrait.jpg")
        )
    );

    private IAPIRequestContext _api = null!;

    [SetUp]
    public async Task SetUp()
    {
        _api = await Playwright.APIRequest.NewContextAsync(
            new APIRequestNewContextOptions { BaseURL = BaseUrl, IgnoreHTTPSErrors = true }
        );
    }

    [TearDown]
    public async Task TearDown() => await _api.DisposeAsync();

    /// <summary>
    /// The registry outlives a run and both the external reference and the access code are
    /// unique, so every spectator here is a fresh one.
    /// </summary>
    private static string UniqueRef() => $"E2E-{Guid.NewGuid():N}";

    private static string UniqueAccessCode() =>
        Random.Shared.Next(100_000, 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string Route(string externalRef) =>
        $"/api/users/{Uri.EscapeDataString(externalRef)}";

    private Task<IAPIResponse> PutSpectatorAsync(string externalRef, object payload) =>
        _api.PutAsync(Route(externalRef), new APIRequestContextOptions { DataObject = payload });

    private static object Registration(string? name = null, string? accessCode = null) =>
        new
        {
            name = name ?? "E2E Spectator",
            accessCode = accessCode ?? UniqueAccessCode(),
            facePicture = FacePicture,
        };

    private static async Task<UserResponse> ReadUserAsync(IAPIResponse response)
    {
        var user = JsonSerializer.Deserialize<UserResponse>(await response.TextAsync(), JsonOptions);
        Assert.That(user, Is.Not.Null);
        return user!;
    }

    private async Task<UserResponse> GivenARegisteredSpectator(string externalRef)
    {
        var response = await PutSpectatorAsync(externalRef, Registration());
        Assert.That(response.Status, Is.EqualTo(201), "Pre-condition: registration must succeed");
        return await ReadUserAsync(response);
    }

    // ─── Registering ─────────────────────────────────────────────────────

    [Test]
    public async Task New_spectator_is_registered_and_returned()
    {
        var externalRef = UniqueRef();
        var accessCode = UniqueAccessCode();

        var response = await PutSpectatorAsync(
            externalRef,
            Registration(name: "Ada Lovelace", accessCode: accessCode)
        );

        Assert.That(response.Status, Is.EqualTo(201));

        var body = await response.TextAsync();
        var user = JsonSerializer.Deserialize<UserResponse>(body, JsonOptions);

        Assert.That(user, Is.Not.Null);
        Assert.That(user!.Id, Is.GreaterThan(0));
        Assert.That(user.ExternalRef, Is.EqualTo(externalRef));
        Assert.That(user.Name, Is.EqualTo("Ada Lovelace"));
        Assert.That(user.AccessCode, Is.EqualTo(accessCode));
        Assert.That(user.FaceContentHash, Is.Not.Empty);
        Assert.That(user.FaceByteSize, Is.GreaterThan(0));
        Assert.That(
            response.Headers["location"],
            Is.EqualTo($"/api/users/{Uri.EscapeDataString(externalRef)}")
        );
        // USR-09: the derivative is pushed to devices, never served back.
        Assert.That(body, Does.Not.Contain(FacePicture[..64]));
    }

    [Test]
    public async Task Spectator_registered_without_a_face_picture_is_rejected()
    {
        var response = await PutSpectatorAsync(
            UniqueRef(),
            new { name = "Faceless", accessCode = UniqueAccessCode() }
        );

        Assert.That(response.Status, Is.EqualTo(400));
    }

    // ─── Retrieving ──────────────────────────────────────────────────────

    [Test]
    public async Task Registered_spectator_is_retrievable()
    {
        var externalRef = UniqueRef();
        var created = await GivenARegisteredSpectator(externalRef);

        var response = await _api.GetAsync(Route(externalRef));

        Assert.That(response.Status, Is.EqualTo(200));

        var fetched = await ReadUserAsync(response);

        Assert.That(fetched.Id, Is.EqualTo(created.Id));
        Assert.That(fetched.ExternalRef, Is.EqualTo(externalRef));
        Assert.That(fetched.AccessCode, Is.EqualTo(created.AccessCode));
        Assert.That(fetched.FaceContentHash, Is.EqualTo(created.FaceContentHash));
    }

    [Test]
    public async Task Getting_unknown_spectator_returns_not_found()
    {
        var response = await _api.GetAsync(Route(UniqueRef()));

        Assert.That(response.Status, Is.EqualTo(404));
    }

    // ─── Removing ────────────────────────────────────────────────────────

    [Test]
    public async Task Removed_spectator_is_no_longer_retrievable()
    {
        var externalRef = UniqueRef();
        await GivenARegisteredSpectator(externalRef);

        var removal = await _api.DeleteAsync(Route(externalRef));

        Assert.That(removal.Status, Is.EqualTo(204));

        var lookup = await _api.GetAsync(Route(externalRef));

        Assert.That(lookup.Status, Is.EqualTo(404));
    }

    [Test]
    public async Task Removing_unknown_spectator_returns_not_found()
    {
        var response = await _api.DeleteAsync(Route(UniqueRef()));

        Assert.That(response.Status, Is.EqualTo(404));
    }

    // ─── Browsing ────────────────────────────────────────────────────────

    [Test]
    public async Task Registered_spectator_appears_in_the_catalogue()
    {
        var created = await GivenARegisteredSpectator(UniqueRef());

        var response = await _api.GetAsync("/api/users?pageSize=200");

        Assert.That(response.Status, Is.EqualTo(200));

        var page = JsonSerializer.Deserialize<UserPage>(await response.TextAsync(), JsonOptions);

        Assert.That(page, Is.Not.Null);
        Assert.That(page!.PageSize, Is.EqualTo(200));
        Assert.That(page.Page, Is.EqualTo(1));

        // The catalogue outlives the run, so the spectator may be on a later page — walk until
        // it is found or the pages run out.
        var found = page.Items.Any(user => user.Id == created.Id);
        var current = page;
        while (!found && current.HasMore)
        {
            var next = await _api.GetAsync($"/api/users?page={current.Page + 1}&pageSize=200");
            current = JsonSerializer.Deserialize<UserPage>(await next.TextAsync(), JsonOptions)!;
            found = current.Items.Any(user => user.Id == created.Id);
        }

        Assert.That(found, Is.True, "The registered spectator was on no page of the catalogue.");
    }

    [Test]
    public async Task Nonsensical_page_request_is_answered_rather_than_refused()
    {
        var response = await _api.GetAsync("/api/users?page=0&pageSize=0");

        Assert.That(response.Status, Is.EqualTo(200));

        var page = JsonSerializer.Deserialize<UserPage>(await response.TextAsync(), JsonOptions);

        Assert.That(page, Is.Not.Null);
        Assert.That(page!.Page, Is.EqualTo(1));
        Assert.That(page.PageSize, Is.EqualTo(1));
    }
}
