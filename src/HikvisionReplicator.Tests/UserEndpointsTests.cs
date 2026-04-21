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
    public async Task Post_ValidUser_Returns201WithLocationAndBody()
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
    }

    [Fact]
    public async Task Post_WithFacePic_Returns201()
    {
        var facePic = new byte[1024]; // 1 KB
        var response = await _client.PostAsJsonAsync("/api/users", ValidRequest(facePic: facePic));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_ResponseDoesNotIncludeFacePic()
    {
        var facePic = new byte[1024];
        var response = await _client.PostAsJsonAsync("/api/users", ValidRequest(facePic: facePic));

        var body = await response.Content.ReadFromJsonAsync<UpsertUserResponse>();
        Assert.NotNull(body);
    }

    // ─── Update (subsequent call with same ExternalRef) ───────────────────

    [Fact]
    public async Task Post_SameExternalRef_Returns200WithUpdatedBody()
    {
        var externalRef = $"ext-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync("/api/users", ValidRequest(externalRef: externalRef, name: "Original"));

        var response = await _client.PostAsJsonAsync("/api/users", ValidRequest(externalRef: externalRef, name: "Updated"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UpsertUserResponse>();
        Assert.Equal("Updated", body!.Name);
        Assert.Equal(externalRef, body.ExternalRef);
    }

    [Fact]
    public async Task Post_SameExternalRef_TwiceWithSameData_Returns200()
    {
        var externalRef = $"ext-{Guid.NewGuid()}";
        await _client.PostAsJsonAsync("/api/users", ValidRequest(externalRef: externalRef));

        var response = await _client.PostAsJsonAsync("/api/users", ValidRequest(externalRef: externalRef));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Post_DifferentExternalRefs_CreateDistinctUsers()
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
    public async Task Post_MissingExternalRef_Returns400WithFieldError()
    {
        var request = new UpsertUserRequest(null, "John", "1234", null);
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.True(body!.Errors.ContainsKey("externalRef"));
    }

    [Fact]
    public async Task Post_ExternalRefTooLong_Returns400WithFieldError()
    {
        var request = ValidRequest(externalRef: new string('x', 256));
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.True(body!.Errors.ContainsKey("externalRef"));
    }

    [Fact]
    public async Task Post_MissingName_Returns400WithFieldError()
    {
        var request = new UpsertUserRequest($"ext-{Guid.NewGuid()}", null, "1234", null);
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(body);
        Assert.True(body!.Errors.ContainsKey("name"));
    }

    [Fact]
    public async Task Post_MissingAccessCode_Returns400WithFieldError()
    {
        var request = new UpsertUserRequest($"ext-{Guid.NewGuid()}", "John", null, null);
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(body);
        Assert.True(body!.Errors.ContainsKey("accessCode"));
    }

    [Fact]
    public async Task Post_NonNumericAccessCode_Returns400()
    {
        var request = ValidRequest(accessCode: "abcd");
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.True(body!.Errors.ContainsKey("accessCode"));
    }

    [Fact]
    public async Task Post_AccessCodeTooShort_Returns400()
    {
        var request = ValidRequest(accessCode: "123");
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.True(body!.Errors.ContainsKey("accessCode"));
    }

    [Fact]
    public async Task Post_FacePicTooLarge_Returns400()
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
    public async Task Get_ExistingUser_Returns200WithBody()
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
    public async Task Get_NonExistentUser_Returns404()
    {
        var response = await _client.GetAsync("/api/users/99999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
