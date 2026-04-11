using System.Net;
using System.Net.Http.Json;
using HikvisionReplicator.Api.Features.Users.CreateUser;
using Microsoft.AspNetCore.Http;

namespace HikvisionReplicator.Tests;

public class UserEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public UserEndpointsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static CreateUserRequest ValidCreate(
        string name = "John Doe",
        string accessCode = "1234",
        byte[]? facePic = null
    ) => new(name, accessCode, facePic);

    // ─── Create User ──────────────────────────────────────────────────────

    [Fact]
    public async Task Post_ValidUser_Returns201WithLocationAndBody()
    {
        var response = await _client.PostAsJsonAsync("/api/users", ValidCreate());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith("/api/users/", response.Headers.Location!.ToString());

        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(body);
        Assert.Equal("John Doe", body!.Name);
        Assert.Equal("1234", body.AccessCode);
        Assert.True(body.Id > 0);
    }

    [Fact]
    public async Task Post_WithFacePic_Returns201()
    {
        var facePic = new byte[1024]; // 1 KB
        var response = await _client.PostAsJsonAsync("/api/users", ValidCreate(facePic: facePic));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Post_MissingName_Returns400WithFieldError()
    {
        var request = new CreateUserRequest(null, "1234", null);
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(body);
        Assert.True(body!.Errors.ContainsKey("name"));
    }

    [Fact]
    public async Task Post_MissingAccessCode_Returns400WithFieldError()
    {
        var request = new CreateUserRequest("John", null, null);
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(body);
        Assert.True(body!.Errors.ContainsKey("accessCode"));
    }

    [Fact]
    public async Task Post_NonNumericAccessCode_Returns400()
    {
        var request = ValidCreate(accessCode: "abcd");
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.True(body!.Errors.ContainsKey("accessCode"));
    }

    [Fact]
    public async Task Post_AccessCodeTooShort_Returns400()
    {
        var request = ValidCreate(accessCode: "123");
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.True(body!.Errors.ContainsKey("accessCode"));
    }

    [Fact]
    public async Task Post_FacePicTooLarge_Returns400()
    {
        var facePic = new byte[204_801]; // just over 200 KB
        var request = ValidCreate(facePic: facePic);
        var response = await _client.PostAsJsonAsync("/api/users", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.True(body!.Errors.ContainsKey("facePic"));
    }

    [Fact]
    public async Task Post_ResponseDoesNotIncludeFacePic()
    {
        var facePic = new byte[1024];
        var response = await _client.PostAsJsonAsync("/api/users", ValidCreate(facePic: facePic));

        var body = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(body);
    }
}
