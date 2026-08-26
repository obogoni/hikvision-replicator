using System.Net;
using System.Text.Json;
using HikvisionReplicator.Api.Features.Users.ListUsers;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// Paging through the registry for a pre-event audit (USR-42…USR-45). The interesting number is
/// three spectators over pages of two: it is the smallest arrangement where a page boundary
/// falls between rows, which is where a non-total order loses or repeats one of them.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ListUsersTests(PostgresFixture fixture) : UserApiTests(fixture)
{
    private Task<HttpResponseMessage> ListAsync(string query = "") =>
        Client.GetAsync($"/api/users{query}");

    /// <summary>Registers spectators in order and reports the ids the registry gave them.</summary>
    private async Task<List<int>> GivenRegisteredSpectatorsAsync(int count)
    {
        var ids = new List<int>();
        for (var index = 0; index < count; index++)
        {
            var response = await UpsertAsync(
                $"TICKET-{index}",
                ValidUpsert(name: $"Spectator {index}", accessCode: $"90000{index}")
            );
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            ids.Add((await ReadBodyAsync(response)).GetProperty("id").GetInt32());
        }

        return ids;
    }

    private static List<int> IdsOf(JsonElement page) =>
        [.. page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetInt32())];

    // ─── USR-42: the catalogue is paged ──────────────────────────────────

    [Fact]
    public async Task Registered_spectators_are_returned_as_a_page()
    {
        var registered = await GivenRegisteredSpectatorsAsync(3);

        var response = await ListAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal(registered, IdsOf(body));
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(ListUsersService.DefaultPageSize, body.GetProperty("pageSize").GetInt32());
        Assert.False(body.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Page_reports_that_another_page_follows_it()
    {
        var registered = await GivenRegisteredSpectatorsAsync(3);

        var body = await ReadBodyAsync(await ListAsync("?page=1&pageSize=2"));

        Assert.Equal(registered.Take(2), IdsOf(body));
        Assert.Equal(2, body.GetProperty("pageSize").GetInt32());
        Assert.True(body.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Final_page_reports_that_nothing_follows_it()
    {
        var registered = await GivenRegisteredSpectatorsAsync(3);

        var body = await ReadBodyAsync(await ListAsync("?page=2&pageSize=2"));

        Assert.Equal(registered.Skip(2), IdsOf(body));
        Assert.Equal(2, body.GetProperty("page").GetInt32());
        Assert.False(body.GetProperty("hasMore").GetBoolean());
    }

    // ─── USR-44: no spectator is skipped or repeated at a boundary ───────

    [Fact]
    public async Task Every_spectator_appears_exactly_once_across_the_pages()
    {
        var registered = await GivenRegisteredSpectatorsAsync(3);

        var first = IdsOf(await ReadBodyAsync(await ListAsync("?page=1&pageSize=2")));
        var second = IdsOf(await ReadBodyAsync(await ListAsync("?page=2&pageSize=2")));

        List<int> seen = [.. first, .. second];

        Assert.Equal(registered, seen);
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    [Fact]
    public async Task Spectators_keep_the_same_order_however_the_pages_are_cut()
    {
        var registered = await GivenRegisteredSpectatorsAsync(3);

        var wholeCatalogue = IdsOf(await ReadBodyAsync(await ListAsync("?pageSize=3")));
        var oneAtATime = new List<int>();
        for (var page = 1; page <= 3; page++)
            oneAtATime.AddRange(IdsOf(await ReadBodyAsync(await ListAsync($"?page={page}&pageSize=1"))));

        Assert.Equal(registered, wholeCatalogue);
        Assert.Equal(registered, oneAtATime);
    }

    // ─── USR-43: an oversized page is clamped, not honoured ──────────────

    [Fact]
    public async Task Page_size_above_the_permitted_maximum_is_clamped_to_it()
    {
        await GivenRegisteredSpectatorsAsync(3);

        var body = await ReadBodyAsync(
            await ListAsync($"?pageSize={ListUsersService.MaxPageSize + 5_000}")
        );

        Assert.Equal(ListUsersService.MaxPageSize, body.GetProperty("pageSize").GetInt32());
    }

    // ─── USR-45: exclusions, and an empty registry is not a failure ──────

    [Fact]
    public async Task Removed_spectators_are_absent_from_the_catalogue()
    {
        var registered = await GivenRegisteredSpectatorsAsync(3);
        Assert.Equal(HttpStatusCode.NoContent, (await RemoveAsync("TICKET-1")).StatusCode);

        var body = await ReadBodyAsync(await ListAsync());

        Assert.Equal(new[] { registered[0], registered[2] }, IdsOf(body));
    }

    [Fact]
    public async Task Listing_an_empty_registry_returns_an_empty_page()
    {
        var response = await ListAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.False(body.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Nonsensical_page_request_is_answered_rather_than_refused()
    {
        await GivenRegisteredSpectatorsAsync(3);

        var response = await ListAsync("?page=0&pageSize=0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(1, body.GetProperty("pageSize").GetInt32());
        Assert.Single(body.GetProperty("items").EnumerateArray());
    }

    // ─── A-1: the bytes never travel with the catalogue ──────────────────

    [Fact]
    public async Task Catalogue_never_carries_the_face_picture_bytes()
    {
        var registered = await GivenRegisteredSpectatorsAsync(1);
        var stored = await StoredPictureAsync(registered[0]);
        Assert.NotNull(stored);

        var response = await ListAsync();
        var text = await response.Content.ReadAsStringAsync();

        var item = Assert.Single((await ReadBodyAsync(response)).GetProperty("items").EnumerateArray());
        Assert.False(item.TryGetProperty("facePicture", out _));
        Assert.False(item.TryGetProperty("picture", out _));
        Assert.DoesNotContain(Convert.ToBase64String(stored!.Content), text, StringComparison.Ordinal);
        Assert.Equal(stored.Content.Length, item.GetProperty("faceByteSize").GetInt32());
    }
}
