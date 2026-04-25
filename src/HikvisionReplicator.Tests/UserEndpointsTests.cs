using System.Net;
using System.Net.Http.Json;
using HikvisionReplicator.Api.Features.Users.UpsertUser;
using Microsoft.AspNetCore.Http;
using GetUserResponse = HikvisionReplicator.Api.Features.Users.GetUser.UserResponse;
using UpsertUserResponse = HikvisionReplicator.Api.Features.Users.UpsertUser.UserResponse;

namespace HikvisionReplicator.Tests;

public class UserEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public UserEndpointsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static UpsertUserRequest ValidRequest(
        string? externalRef = null,
        string name = "John Doe",
        string accessCode = "1234",
        byte[]? facePic = null
    ) => new(externalRef ?? $"ext-{Guid.NewGuid()}", name, accessCode, facePic);

    // ─── Create (first call) ──────────────────────────────────────────────

    [Fact]
    public async Task New_user_is_created_and_returned()
    {
        var request = ValidRequest(externalRef: $"ext-{Guid.NewGuid()}");
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith("/api/users/", response.Headers.Location!.ToString());

        var body = await response.Content.ReadFromJsonAsync<UpsertUserResponse>();
        Assert.NotNull(body);
        Assert.Equal("John Doe", body!.Name);
        Assert.Equal("1234", body.AccessCode);
        Assert.Equal(request.ExternalRef, body.ExternalRef);
        Assert.True(body.Id > 0);
        Assert.Equal("PendingAdd", body.Status);
    }

    [Fact]
    public async Task New_user_with_face_picture_is_created()
    {
        var facePic = new byte[1024]; // 1 KB
        var response = await _client.PostAsJsonAsync("/api/users", ValidRequest(facePic: facePic));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task User_response_never_includes_face_picture()
    {
        var facePic = new byte[1024];
        var response = await _client.PostAsJsonAsync("/api/users", ValidRequest(facePic: facePic));

        var body = await response.Content.ReadFromJsonAsync<UpsertUserResponse>();
        Assert.NotNull(body);
    }

    // ─── Update (subsequent call with same ExternalRef) ───────────────────

    [Fact]
    public async Task Upserting_existing_user_updates_and_returns_them()
    {
        var externalRef = $"ext-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync("/api/users", ValidRequest(externalRef: externalRef, name: "Original"));

        var response = await _client.PostAsJsonAsync("/api/users", ValidRequest(externalRef: externalRef, name: "Updated"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UpsertUserResponse>();
        Assert.Equal("Updated", body!.Name);
        Assert.Equal(externalRef, body.ExternalRef);
        Assert.Equal("PendingAdd", body.Status);
    }

    [Fact]
    public async Task Upserting_existing_user_with_same_data_is_idempotent()
    {
        var externalRef = $"ext-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync("/api/users", ValidRequest(externalRef: externalRef));

        var response = await _client.PostAsJsonAsync("/api/users", ValidRequest(externalRef: externalRef));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Different_external_refs_create_distinct_users()
    {
        var r1 = await _client.PostAsJsonAsync("/api/users", ValidRequest(externalRef: $"ext-{Guid.NewGuid()}", name: "Alice"));
        var r2 = await _client.PostAsJsonAsync("/api/users", ValidRequest(externalRef: $"ext-{Guid.NewGuid()}", name: "Bob"));

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);

        var b1 = await r1.Content.ReadFromJsonAsync<UpsertUserResponse>();
        var b2 = await r2.Content.ReadFromJsonAsync<UpsertUserResponse>();
        Assert.NotEqual(b1!.Id, b2!.Id);
    }

    // ─── Validation ───────────────────────────────────────────────────────

    [Fact]
    public async Task User_without_external_ref_is_invalid()
    {
        var request = new UpsertUserRequest(null, "John", "1234", null);
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.True(body!.Errors.ContainsKey("externalRef"));
    }

    [Fact]
    public async Task User_with_external_ref_exceeding_max_length_is_invalid()
    {
        var request = ValidRequest(externalRef: new string('x', 256));
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.True(body!.Errors.ContainsKey("externalRef"));
    }

    [Fact]
    public async Task User_without_name_is_invalid()
    {
        var request = new UpsertUserRequest($"ext-{Guid.NewGuid()}", null, "1234", null);
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(body);
        Assert.True(body!.Errors.ContainsKey("name"));
    }

    [Fact]
    public async Task User_without_access_code_is_invalid()
    {
        var request = new UpsertUserRequest($"ext-{Guid.NewGuid()}", "John", null, null);
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(body);
        Assert.True(body!.Errors.ContainsKey("accessCode"));
    }

    [Fact]
    public async Task User_with_non_numeric_access_code_is_invalid()
    {
        var request = ValidRequest(accessCode: "abcd");
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.True(body!.Errors.ContainsKey("accessCode"));
    }

    [Fact]
    public async Task User_with_access_code_too_short_is_invalid()
    {
        var request = ValidRequest(accessCode: "123");
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.True(body!.Errors.ContainsKey("accessCode"));
    }

    [Fact]
    public async Task User_with_face_picture_exceeding_size_limit_is_invalid()
    {
        var facePic = new byte[204_801]; // just over 200 KB
        var request = ValidRequest(facePic: facePic);
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.True(body!.Errors.ContainsKey("facePic"));
    }

    // ─── Get User ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Getting_existing_user_returns_their_data()
    {
        var created = await _client.PostAsJsonAsync("/api/users", ValidRequest());
        var id = created.Headers.Location!.ToString().Split('/').Last();

        var response = await _client.GetAsync($"/api/users/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GetUserResponse>();
        Assert.NotNull(body);
        Assert.Equal("John Doe", body!.Name);
        Assert.Equal("1234", body.AccessCode);
        Assert.NotEmpty(body.ExternalRef);
    }

    [Fact]
    public async Task Getting_unknown_user_returns_not_found()
    {
        var response = await _client.GetAsync("/api/users/99999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
