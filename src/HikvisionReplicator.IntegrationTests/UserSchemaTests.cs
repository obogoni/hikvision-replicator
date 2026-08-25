using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// The schema itself, asserted against the real database the application migrated into
/// existence — not against the EF model that produced it.
/// <para>
/// The two unique indexes are deliberately asymmetric and that asymmetry is the single most
/// misreadable thing in the schema, so each one is proved from both sides: an external
/// reference stays reserved after its holder is tombstoned, because resurrection has to find
/// it (A-7); an access code returns to the pool, because USR-06 scopes it to active users.
/// Swapping either filter breaks a different criterion while leaving the other's test green.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class UserSchemaTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static User NewUser(
        string externalRef,
        string accessCode = "123456",
        string name = "Ada Lovelace",
        string contentHash = "0f1e2d3c",
        byte[]? content = null
    ) =>
        User.Create(
                externalRef,
                name,
                accessCode,
                FaceFingerprint.Create(contentHash, 51_200, 800, 600).AsT0,
                content ?? [0x01, 0x02, 0x03],
                Now
            )
            .AsT0;

    private async Task<int> GivenRegisteredUserAsync(
        string externalRef,
        string accessCode = "123456",
        byte[]? content = null
    )
    {
        await using var context = fixture.CreateDbContext();
        var user = NewUser(externalRef, accessCode, content: content);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>
    /// Tombstones straight through SQL rather than through the aggregate, so these tests
    /// prove what the <em>index filter</em> does and never what the domain happens to do.
    /// </summary>
    private async Task GivenTombstonedAsync(int userId)
    {
        await using var context = fixture.CreateDbContext();
        await context.Database.ExecuteSqlAsync(
            $"""UPDATE users SET "DeletedAt" = {Now} WHERE "Id" = {userId}"""
        );
    }

    private async Task<string> IndexDefinitionAsync(string indexName)
    {
        await using var context = fixture.CreateDbContext();
        var definitions = await context
            .Database.SqlQuery<string>(
                $"""SELECT indexdef AS "Value" FROM pg_indexes WHERE indexname = {indexName}"""
            )
            .ToListAsync();

        return Assert.Single(definitions);
    }

    private static async Task<PostgresException> SaveExpectingUniqueViolationAsync(
        AppDbContext context
    )
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync()
        );
        return Assert.IsType<PostgresException>(exception.InnerException);
    }

    // ─── USR-38: the schema comes from a migration applied at startup ────

    [Fact]
    public async Task Registry_tables_are_created_when_the_application_starts()
    {
        var tables = await fixture.ListPublicTablesAsync();

        Assert.Contains(UserConfiguration.TableName, tables);
        Assert.Contains(FacePictureConfiguration.TableName, tables);
    }

    [Fact]
    public async Task Registry_schema_is_recorded_as_an_applied_migration()
    {
        var applied = await fixture.ListAppliedMigrationsAsync();

        Assert.Contains(
            applied,
            migration => migration.EndsWith("AddUserRegistry", StringComparison.Ordinal)
        );
    }

    // ─── AD-009: the value objects survive the round trip ────────────────

    [Fact]
    public async Task Spectator_is_stored_and_read_back_with_every_identity_field_intact()
    {
        await using var context = fixture.CreateDbContext();
        context.Users.Add(NewUser("TICKET-1", "998877", content: [0xAA, 0xBB]));
        await context.SaveChangesAsync();

        await using var verification = fixture.CreateDbContext();
        var stored = await verification.Users.SingleAsync();

        Assert.Equal("TICKET-1", stored.ExternalRef.Value);
        Assert.Equal("998877", stored.AccessCode.Value);
        Assert.Equal("Ada Lovelace", stored.Name);
        Assert.Equal("0f1e2d3c", stored.Face.ContentHash);
        Assert.Equal(51_200, stored.Face.ByteSize);
        Assert.Equal(800, stored.Face.Width);
        Assert.Equal(600, stored.Face.Height);
        Assert.Null(stored.DeletedAt);
        Assert.Equal(Now, stored.CreatedAt);
        Assert.Equal(Now, stored.UpdatedAt);
    }

    // ─── IX_users_ExternalRef: unique across every row, tombstones included ───

    [Fact]
    public async Task Two_spectators_cannot_share_an_external_reference()
    {
        await GivenRegisteredUserAsync("TICKET-1");

        await using var context = fixture.CreateDbContext();
        context.Users.Add(NewUser("TICKET-1", "222222"));

        var violation = await SaveExpectingUniqueViolationAsync(context);

        Assert.Equal(PostgresErrorCodes.UniqueViolation, violation.SqlState);
        Assert.Equal(UserConfiguration.ExternalRefIndexName, violation.ConstraintName);
    }

    [Fact]
    public async Task Deleted_spectators_external_reference_stays_reserved()
    {
        var userId = await GivenRegisteredUserAsync("TICKET-1");
        await GivenTombstonedAsync(userId);

        await using var context = fixture.CreateDbContext();
        context.Users.Add(NewUser("TICKET-1", "222222"));

        var violation = await SaveExpectingUniqueViolationAsync(context);

        Assert.Equal(UserConfiguration.ExternalRefIndexName, violation.ConstraintName);
    }

    [Fact]
    public async Task External_references_differing_only_by_letter_case_are_two_spectators()
    {
        await GivenRegisteredUserAsync("ticket-1");

        await using var context = fixture.CreateDbContext();
        context.Users.Add(NewUser("TICKET-1", "222222"));
        await context.SaveChangesAsync();

        await using var verification = fixture.CreateDbContext();
        Assert.Equal(2, await verification.Users.CountAsync());
    }

    [Fact]
    public async Task External_reference_uniqueness_applies_to_every_row()
    {
        var definition = await IndexDefinitionAsync(UserConfiguration.ExternalRefIndexName);

        Assert.Contains("UNIQUE", definition, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE", definition, StringComparison.Ordinal);
    }

    // ─── IX_users_AccessCode: unique among active rows only ──────────────

    [Fact]
    public async Task Two_active_spectators_cannot_share_an_access_code()
    {
        await GivenRegisteredUserAsync("TICKET-1", "123456");

        await using var context = fixture.CreateDbContext();
        context.Users.Add(NewUser("TICKET-2", "123456"));

        var violation = await SaveExpectingUniqueViolationAsync(context);

        Assert.Equal(PostgresErrorCodes.UniqueViolation, violation.SqlState);
        Assert.Equal(UserConfiguration.AccessCodeIndexName, violation.ConstraintName);
    }

    [Fact]
    public async Task Deleted_spectators_access_code_can_be_reused()
    {
        var userId = await GivenRegisteredUserAsync("TICKET-1", "123456");
        await GivenTombstonedAsync(userId);

        await using var context = fixture.CreateDbContext();
        context.Users.Add(NewUser("TICKET-2", "123456"));
        await context.SaveChangesAsync();

        await using var verification = fixture.CreateDbContext();
        var reusing = await verification.Users.SingleAsync(user =>
            user.ExternalRef == ExternalRef.Create("TICKET-2").AsT0
        );
        Assert.Equal("123456", reusing.AccessCode.Value);
    }

    [Fact]
    public async Task Access_code_uniqueness_is_scoped_to_spectators_that_are_not_deleted()
    {
        var definition = await IndexDefinitionAsync(UserConfiguration.AccessCodeIndexName);

        Assert.Contains("UNIQUE", definition, StringComparison.Ordinal);
        Assert.Contains(UserConfiguration.ActiveRowsFilter, definition, StringComparison.Ordinal);
    }

    // ─── face_pictures: 1:1, cascade delete, never auto-included ─────────

    [Fact]
    public async Task Face_picture_is_stored_alongside_its_spectator()
    {
        var userId = await GivenRegisteredUserAsync("TICKET-1", content: [0x0A, 0x0B, 0x0C]);

        await using var context = fixture.CreateDbContext();
        var picture = await context.Set<FacePicture>().SingleAsync();

        Assert.Equal(userId, picture.UserId);
        Assert.Equal([0x0A, 0x0B, 0x0C], picture.Content);
    }

    [Fact]
    public async Task Face_picture_is_removed_when_its_spectators_row_is_removed()
    {
        var userId = await GivenRegisteredUserAsync("TICKET-1");

        await using var context = fixture.CreateDbContext();
        await context.Users.Where(user => user.Id == userId).ExecuteDeleteAsync();

        await using var verification = fixture.CreateDbContext();
        Assert.Equal(0, await verification.Set<FacePicture>().CountAsync());
    }

    [Fact]
    public async Task Reading_a_spectator_does_not_bring_its_face_picture_with_it()
    {
        await GivenRegisteredUserAsync("TICKET-1");

        await using var context = fixture.CreateDbContext();
        var stored = await context.Users.SingleAsync();

        Assert.Null(stored.Picture);
    }
}
