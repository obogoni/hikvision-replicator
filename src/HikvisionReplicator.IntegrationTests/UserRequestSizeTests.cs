using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HikvisionReplicator.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// USR-19 at the transport layer. A-11 leaves this endpoint unauthenticated while it decodes
/// attacker-supplied bytes, so the size of the body it will read at all is the only bound on
/// what one caller can make it allocate.
/// <para>
/// The upload cap is configured down to the size of a fixture, which makes that fixture a
/// <em>maximum-size valid upload</em>. It is the case the limit is easiest to get wrong: A-9
/// sends the picture as base64, so it arrives ~4/3 of its own size, and a limit set to the image
/// cap itself would refuse every upload the normalizer would have accepted.
/// </para>
/// <para>
/// Runs on a real socket rather than the in-memory server, which enforces no size limit at all.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UserRequestSizeTests(PostgresFixture fixture) : IAsyncLifetime, IDisposable
{
    private static readonly byte[] MaximumSizedPicture = FaceFixtures.Bytes(FaceFixtures.Portrait);

    private KestrelWebApplicationFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        _factory = new KestrelWebApplicationFactory(
            fixture.ConnectionString,
            builder =>
                builder.UseSetting(
                    "FaceImage:MaxUploadBytes",
                    MaximumSizedPicture.Length.ToString(CultureInfo.InvariantCulture)
                )
        );

        // Touching the container is what builds and starts the live host.
        _ = _factory.Services;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => _factory?.Dispose();

    private static object Body(byte[] facePicture) =>
        new
        {
            name = "Ada Lovelace",
            accessCode = "445566",
            facePicture,
        };

    private async Task<int> CountUsersAsync()
    {
        await using var context = fixture.CreateDbContext();
        return await context.Users.CountAsync();
    }

    // ─── USR-19: base64 inflation is part of the limit ───────────────────

    [Fact]
    public async Task Picture_at_the_accepted_maximum_still_fits_inside_the_request_limit()
    {
        using var client = _factory.CreateLiveClient();

        var response = await client.PutAsJsonAsync(
            "/api/users/largest-accepted",
            Body(MaximumSizedPicture)
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await CountUsersAsync());
    }

    // ─── USR-19: an oversized body is refused by the transport ───────────

    [Fact]
    public async Task Body_larger_than_the_request_limit_is_refused_before_it_is_read()
    {
        using var client = _factory.CreateLiveClient();

        var response = await client.PutAsJsonAsync(
            "/api/users/far-too-large",
            Body(new byte[MaximumSizedPicture.Length * 4])
        );

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Oversized_body_never_reaches_the_normalizer()
    {
        using var client = _factory.CreateLiveClient();

        var response = await client.PutAsJsonAsync(
            "/api/users/far-too-large",
            Body(new byte[MaximumSizedPicture.Length * 4])
        );

        // The normalizer's own cap answers 400 with this message once it has the whole body in
        // memory (USR-19). Seeing it here would mean the body was read after all.
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(
            SkiaFaceImageNormalizer.Errors.UploadTooLarge,
            text,
            StringComparison.Ordinal
        );
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountUsersAsync());
    }

    [Fact]
    public async Task Refused_body_is_answered_as_a_problem_rather_than_an_empty_response()
    {
        using var client = _factory.CreateLiveClient();

        var response = await client.PutAsJsonAsync(
            "/api/users/far-too-large",
            Body(new byte[MaximumSizedPicture.Length * 4])
        );

        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(413, body.RootElement.GetProperty("status").GetInt32());
    }
}
