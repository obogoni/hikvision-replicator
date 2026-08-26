using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// What every user-endpoint test class needs: the running application, a clean registry, and the
/// few readings that have to come from the database rather than from the API — because a promise
/// about what is stored cannot be proved by asking the thing that stores it.
/// </summary>
public abstract class UserApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    protected const string DefaultName = "Ada Lovelace";
    protected const string DefaultAccessCode = "123456";

    protected PostgresFixture Fixture { get; } = fixture;

    protected HttpClient Client { get; } = fixture.Factory.CreateClient();

    public Task InitializeAsync() => Fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    protected static string Route(string externalRef) =>
        $"/api/users/{Uri.EscapeDataString(externalRef)}";

    protected static object ValidUpsert(
        string name = DefaultName,
        string accessCode = DefaultAccessCode,
        string fixture = FaceFixtures.Portrait
    ) =>
        new
        {
            name,
            accessCode,
            facePicture = FaceFixtures.Bytes(fixture),
        };

    protected Task<HttpResponseMessage> UpsertAsync(string externalRef, object body) =>
        Client.PutAsJsonAsync(Route(externalRef), body);

    protected static Task<HttpResponseMessage> UpsertAsync(
        HttpClient client,
        string externalRef,
        object body
    ) => client.PutAsJsonAsync(Route(externalRef), body);

    protected Task<HttpResponseMessage> ReadAsync(string externalRef) =>
        Client.GetAsync(Route(externalRef));

    protected Task<HttpResponseMessage> RemoveAsync(string externalRef) =>
        Client.DeleteAsync(Route(externalRef));

    protected static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>Asserts a 400 problem body whose validation errors name the given field.</summary>
    protected static async Task AssertRejectedFieldAsync(
        HttpResponseMessage response,
        string expectedField
    )
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadBodyAsync(response);
        var errors = body.GetProperty("errors");
        Assert.True(
            errors.TryGetProperty(expectedField, out var messages),
            $"Expected the problem body to name '{expectedField}'. Body: {body}"
        );
        Assert.NotEmpty(messages.EnumerateArray());
    }

    /// <summary>
    /// Asserts a 409 whose problem body names <em>which</em> key collided. The status alone does
    /// not discriminate: `AD-022` translates two different unique-index violations into two
    /// different messages, and swapping them would still answer 409 while telling the caller to
    /// change a key that is already free.
    /// </summary>
    protected static async Task AssertConflictAsync(
        HttpResponseMessage response,
        string expectedDetail
    )
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal(expectedDetail, body.GetProperty("detail").GetString());
    }

    protected async Task<int> CountUsersAsync()
    {
        await using var context = Fixture.CreateDbContext();
        return await context.Users.CountAsync();
    }

    /// <summary>The stored row itself, including the tombstone the API refuses to show.</summary>
    protected async Task<User?> StoredUserAsync(string externalRef)
    {
        await using var context = Fixture.CreateDbContext();
        var reference = ExternalRef.Create(externalRef).AsT0;
        return await context.Users.SingleOrDefaultAsync(user => user.ExternalRef == reference);
    }

    /// <summary>
    /// The face picture read straight out of its own table. Asking the API whether it hides the
    /// bytes would pass just as happily against a soft delete.
    /// </summary>
    protected async Task<FacePicture?> StoredPictureAsync(int userId)
    {
        await using var context = Fixture.CreateDbContext();
        return await context
            .Set<FacePicture>()
            .SingleOrDefaultAsync(picture => picture.UserId == userId);
    }

    protected async Task ExecuteSqlAsync(string sql)
    {
        await using var context = Fixture.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    /// <summary>
    /// A client whose application reads the clock the test controls, so USR-11's promise that
    /// timestamps come from the injected <see cref="TimeProvider"/> can be asserted against an
    /// exact instant instead of a tolerance.
    /// </summary>
    protected WebApplicationFactory<Program> WithClock(TimeProvider clock) =>
        Fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton(clock))
        );
}
