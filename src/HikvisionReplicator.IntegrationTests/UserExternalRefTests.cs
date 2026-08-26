using System.Net;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// A-15 permits <em>any</em> non-blank string of 255 characters or fewer as an external
/// reference, and the reference is addressed as a path segment. This is where those two meet:
/// a key an integrator may legitimately mint has to survive being written into a URL, matched by
/// the route, and decoded back into the identifier it started as.
/// </summary>
[Collection(PostgresCollection.Name)]
public class UserExternalRefTests(PostgresFixture fixture) : UserApiTests(fixture)
{
    // ─── A-15 / spec Edge Cases: reserved characters in the key ──────────

    [Theory]
    [InlineData("TICKET%2026")]
    [InlineData("TICKET 2026")]
    [InlineData("TICKET+2026")]
    [InlineData("TICKET#2026")]
    [InlineData("ingresso-José-日本-Ω")]
    public async Task Reference_with_reserved_characters_is_registered_under_the_key_it_names(
        string externalRef
    )
    {
        var response = await UpsertAsync(externalRef, ValidUpsert());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal(externalRef, body.GetProperty("externalRef").GetString());
        Assert.Equal(
            $"/api/users/{Uri.EscapeDataString(externalRef)}",
            response.Headers.Location?.OriginalString
        );
    }

    [Theory]
    [InlineData("TICKET%2026")]
    [InlineData("TICKET 2026")]
    [InlineData("TICKET+2026")]
    [InlineData("TICKET#2026")]
    [InlineData("ingresso-José-日本-Ω")]
    public async Task Reference_with_reserved_characters_is_retrievable_under_the_key_it_names(
        string externalRef
    )
    {
        await UpsertAsync(externalRef, ValidUpsert());

        var response = await ReadAsync(externalRef);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal(externalRef, body.GetProperty("externalRef").GetString());
    }

    [Theory]
    [InlineData("TICKET%2026")]
    [InlineData("TICKET 2026")]
    [InlineData("TICKET+2026")]
    [InlineData("TICKET#2026")]
    [InlineData("ingresso-José-日本-Ω")]
    public async Task Reference_with_reserved_characters_is_removable_under_the_key_it_names(
        string externalRef
    )
    {
        await UpsertAsync(externalRef, ValidUpsert());

        var removal = await RemoveAsync(externalRef);

        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ReadAsync(externalRef)).StatusCode);
    }

    /// <summary>
    /// The one character A-15 had to give up. ASP.NET Core routing leaves <c>%2F</c> encoded in
    /// a route value rather than decoding it into a separator, so the escaped text — and not the
    /// key the integrator named — becomes the identity. The request succeeds, which is what makes
    /// it dangerous: two different spectators could be addressed under keys that differ only by
    /// an escape, with nothing reported to the caller.
    /// <para>
    /// Asserted rather than excluded, so that a host or framework change that starts decoding
    /// <c>%2F</c> fails here and sends the next reader back to A-15 instead of silently changing
    /// which row a live integrator's key resolves to.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Reference_containing_a_slash_is_registered_under_its_escaped_text_instead()
    {
        const string Named = "TICKET/2026";
        const string Escaped = "TICKET%2F2026";

        var response = await UpsertAsync(Named, ValidUpsert());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal(Escaped, body.GetProperty("externalRef").GetString());

        Assert.Null(await StoredUserAsync(Named));
        Assert.NotNull(await StoredUserAsync(Escaped));
    }
}
