using System.Data.Common;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// Records the SQL every query actually sent, so a claim about what a specification reads is
/// evidence rather than an expectation.
/// </summary>
internal sealed class SqlRecorder : DbCommandInterceptor
{
    private readonly List<string> _commands = [];

    public IReadOnlyList<string> Commands => _commands;

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        _commands.Add(command.CommandText);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        _commands.Add(command.CommandText);
        return ValueTask.FromResult(result);
    }
}

/// <summary>
/// The query shapes, against the real database.
/// <para>
/// Two of these specifications differ only in whether tombstones are visible, and picking the
/// wrong one is silent: the active-only lookup makes a resurrection look like an unregistered
/// reference, and the including-deleted lookup makes a deleted spectator readable. Both
/// directions are asserted.
/// </para>
/// <para>
/// The no-bytes-loaded assertions are the only thing enforcing A-1. Nothing about the schema
/// prevents an Include from being added later, so the guarantee is proved from the SQL itself.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class UserSpecificationTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static ExternalRef Ref(string value) => ExternalRef.Create(value).AsT0;

    private static AccessCode Code(string value) => AccessCode.Create(value).AsT0;

    private static User NewUser(string externalRef, string accessCode, string name) =>
        User.Create(
                externalRef,
                name,
                accessCode,
                FaceFingerprint.Create("0f1e2d3c", 51_200, 800, 600).AsT0,
                [0x01, 0x02, 0x03],
                Now
            )
            .AsT0;

    private async Task<int> GivenRegisteredUserAsync(
        string externalRef,
        string accessCode = "123456",
        string name = "Ada Lovelace",
        bool deleted = false
    )
    {
        await using var context = fixture.CreateDbContext();
        var user = NewUser(externalRef, accessCode, name);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        if (deleted)
        {
            user.MarkDeleted(Now.AddMinutes(1));
            await context.SaveChangesAsync();
        }

        return user.Id;
    }

    private AppDbContext CreateRecordingContext(SqlRecorder recorder) =>
        new(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(fixture.ConnectionString)
                .AddInterceptors(recorder)
                .Options
        );

    private static void AssertNoFacePictureRead(SqlRecorder recorder)
    {
        Assert.NotEmpty(recorder.Commands);
        Assert.DoesNotContain(
            recorder.Commands,
            command =>
                command.Contains(
                    FacePictureConfiguration.TableName,
                    StringComparison.OrdinalIgnoreCase
                )
        );
    }

    // ─── USR-35 / USR-36 / USR-31: the active-only lookup ────────────────

    [Fact]
    public async Task Active_spectator_is_found_by_its_external_reference()
    {
        var userId = await GivenRegisteredUserAsync("TICKET-1");

        await using var context = fixture.CreateDbContext();
        var found = await new UserRepository(context).FirstOrDefaultAsync(
            new UserByExternalRefSpec(Ref("TICKET-1")),
            CancellationToken.None
        );

        Assert.NotNull(found);
        Assert.Equal(userId, found.Id);
    }

    [Fact]
    public async Task Deleted_spectator_is_invisible_to_the_active_lookup()
    {
        await GivenRegisteredUserAsync("TICKET-1", deleted: true);

        await using var context = fixture.CreateDbContext();
        var found = await new UserRepository(context).FirstOrDefaultAsync(
            new UserByExternalRefSpec(Ref("TICKET-1")),
            CancellationToken.None
        );

        Assert.Null(found);
    }

    [Fact]
    public async Task Unregistered_external_reference_matches_no_spectator()
    {
        await GivenRegisteredUserAsync("TICKET-1");

        await using var context = fixture.CreateDbContext();
        var repository = new UserRepository(context);

        Assert.Null(
            await repository.FirstOrDefaultAsync(
                new UserByExternalRefSpec(Ref("TICKET-9")),
                CancellationToken.None
            )
        );
        Assert.Null(
            await repository.FirstOrDefaultAsync(
                new UserByExternalRefIncludingDeletedSpec(Ref("TICKET-9")),
                CancellationToken.None
            )
        );
    }

    // ─── A-7 / USR-34: the lookup that must see the tombstone ────────────

    [Fact]
    public async Task Deleted_spectator_is_still_found_by_the_lookup_that_includes_tombstones()
    {
        var userId = await GivenRegisteredUserAsync("TICKET-1", deleted: true);

        await using var context = fixture.CreateDbContext();
        var found = await new UserRepository(context).FirstOrDefaultAsync(
            new UserByExternalRefIncludingDeletedSpec(Ref("TICKET-1")),
            CancellationToken.None
        );

        Assert.NotNull(found);
        Assert.Equal(userId, found.Id);
        Assert.NotNull(found.DeletedAt);
    }

    // ─── USR-06: the friendly access-code pre-check ──────────────────────

    [Fact]
    public async Task Active_spectator_is_found_by_its_access_code()
    {
        var userId = await GivenRegisteredUserAsync("TICKET-1", "123456");

        await using var context = fixture.CreateDbContext();
        var found = await new UserRepository(context).FirstOrDefaultAsync(
            new ActiveUserByAccessCodeSpec(Code("123456")),
            CancellationToken.None
        );

        Assert.NotNull(found);
        Assert.Equal(userId, found.Id);
    }

    [Fact]
    public async Task Deleted_spectators_access_code_matches_no_active_spectator()
    {
        await GivenRegisteredUserAsync("TICKET-1", "123456", deleted: true);

        await using var context = fixture.CreateDbContext();
        var found = await new UserRepository(context).FirstOrDefaultAsync(
            new ActiveUserByAccessCodeSpec(Code("123456")),
            CancellationToken.None
        );

        Assert.Null(found);
    }

    [Fact]
    public async Task Spectator_is_never_matched_by_its_own_access_code_when_it_is_the_one_excluded()
    {
        var userId = await GivenRegisteredUserAsync("TICKET-1", "123456");

        await using var context = fixture.CreateDbContext();
        var found = await new UserRepository(context).FirstOrDefaultAsync(
            new ActiveUserByAccessCodeSpec(Code("123456"), userId),
            CancellationToken.None
        );

        Assert.Null(found);
    }

    // ─── USR-44 / USR-45: the paged catalogue ────────────────────────────

    [Fact]
    public async Task Pages_together_contain_every_spectator_exactly_once()
    {
        await GivenRegisteredUserAsync("TICKET-1", "111111", "Ada Lovelace");
        await GivenRegisteredUserAsync("TICKET-2", "222222", "Grace Hopper");
        await GivenRegisteredUserAsync("TICKET-3", "333333", "Alan Turing");

        await using var context = fixture.CreateDbContext();
        var repository = new UserRepository(context);

        var first = await repository.ListAsync(new ActiveUsersPagedSpec(0, 2), CancellationToken.None);
        var second = await repository.ListAsync(new ActiveUsersPagedSpec(2, 2), CancellationToken.None);

        var references = first.Concat(second).Select(user => user.ExternalRef.Value).ToList();
        Assert.Equal(2, first.Count);
        Assert.Single(second);
        Assert.Equal(["TICKET-1", "TICKET-2", "TICKET-3"], references);
    }

    [Fact]
    public async Task Listing_the_catalogue_excludes_deleted_spectators()
    {
        await GivenRegisteredUserAsync("TICKET-1", "111111");
        await GivenRegisteredUserAsync("TICKET-2", "222222", deleted: true);

        await using var context = fixture.CreateDbContext();
        var listed = await new UserRepository(context).ListAsync(
            new ActiveUsersPagedSpec(0, 10),
            CancellationToken.None
        );

        Assert.Equal(["TICKET-1"], listed.Select(user => user.ExternalRef.Value));
    }

    // ─── A-1: the bytes are never read back ──────────────────────────────

    [Fact]
    public async Task Looking_up_a_spectator_never_reads_the_face_picture_table()
    {
        await GivenRegisteredUserAsync("TICKET-1");

        var recorder = new SqlRecorder();
        await using var context = CreateRecordingContext(recorder);
        var repository = new UserRepository(context);

        var found = await repository.FirstOrDefaultAsync(
            new UserByExternalRefSpec(Ref("TICKET-1")),
            CancellationToken.None
        );
        await repository.FirstOrDefaultAsync(
            new UserByExternalRefIncludingDeletedSpec(Ref("TICKET-1")),
            CancellationToken.None
        );
        await repository.FirstOrDefaultAsync(
            new ActiveUserByAccessCodeSpec(Code("123456")),
            CancellationToken.None
        );

        Assert.NotNull(found);
        Assert.Null(found.Picture);
        AssertNoFacePictureRead(recorder);
    }

    [Fact]
    public async Task Listing_the_catalogue_never_reads_the_face_picture_table()
    {
        await GivenRegisteredUserAsync("TICKET-1", "111111");
        await GivenRegisteredUserAsync("TICKET-2", "222222");

        var recorder = new SqlRecorder();
        await using var context = CreateRecordingContext(recorder);

        var listed = await new UserRepository(context).ListAsync(
            new ActiveUsersPagedSpec(0, 10),
            CancellationToken.None
        );

        Assert.Equal(2, listed.Count);
        Assert.All(listed, user => Assert.Null(user.Picture));
        AssertNoFacePictureRead(recorder);
    }
}
