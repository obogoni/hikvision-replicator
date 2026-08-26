using System.Data.Common;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>Records the SQL every query actually sent, so a claim about what a read touches is
/// evidence rather than an expectation.</summary>
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
/// <b>The exception to the black-box rule, and the whole of it for users (AD-036).</b>
/// <para>
/// Every other user test drives a use case through the HTTP surface. These do not, and each one
/// earns that by naming an observable the HTTP surface <em>cannot distinguish</em> — a wrong
/// implementation here returns byte-identical responses to a right one. If a test added to this
/// class cannot state such an observable in a sentence, it belongs in a use-case class instead.
/// </para>
/// <para>
/// The four kinds, and why HTTP is blind to each:
/// <list type="bullet">
/// <item><b>What a read touches.</b> A response that omits the face bytes looks the same whether
/// or not they were loaded, so only the emitted SQL discriminates. These assertions are the only
/// thing enforcing A-1, on the latency path AD-014 makes the primary quality attribute.</item>
/// <item><b>The shape of the two unique indexes.</b> Their asymmetry is deliberate and is the
/// single most misreadable thing in the schema. Swapping the filters keeps most round-trips
/// green; the index definitions do not lie.</item>
/// <item><b>Which failures are <em>not</em> translated.</b> AD-022 turns two named index
/// violations into conflicts. Everything else must stay an exception — arranging a foreign
/// constraint violation or a vanished row needs the database, not a request.</item>
/// <item><b>Cancellation.</b> A pre-cancelled token proves the abort; racing a real HTTP
/// client against its own request would assert scheduling luck.</item>
/// </list>
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class UserPersistenceContractTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static ExternalRef Ref(string value) => ExternalRef.Create(value).AsT0;

    private static AccessCode Code(string value) => AccessCode.Create(value).AsT0;

    private static User NewUser(
        string externalRef,
        string accessCode = "123456",
        string name = "Ada Lovelace",
        byte[]? content = null
    ) =>
        User.Create(
                externalRef,
                name,
                accessCode,
                FaceFingerprint.Create("0f1e2d3c", 51_200, 800, 600).AsT0,
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

    private async Task<int> CountUsersAsync()
    {
        await using var context = fixture.CreateDbContext();
        return await context.Users.CountAsync();
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

    // ─── A-1: the face bytes never ride along on a read ──────────────────

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

    [Fact]
    public async Task Reading_a_spectator_does_not_bring_its_face_picture_with_it()
    {
        await GivenRegisteredUserAsync("TICKET-1");

        await using var context = fixture.CreateDbContext();
        var stored = await context.Users.SingleAsync();

        Assert.Null(stored.Picture);
    }

    // ─── The asymmetry of the two unique indexes ─────────────────────────

    [Fact]
    public async Task External_reference_uniqueness_applies_to_every_row()
    {
        var definition = await IndexDefinitionAsync(UserConfiguration.ExternalRefIndexName);

        Assert.Contains("UNIQUE", definition, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Access_code_uniqueness_is_scoped_to_spectators_that_are_not_deleted()
    {
        var definition = await IndexDefinitionAsync(UserConfiguration.AccessCodeIndexName);

        Assert.Contains("UNIQUE", definition, StringComparison.Ordinal);
        Assert.Contains(UserConfiguration.ActiveRowsFilter, definition, StringComparison.Ordinal);
    }

    // ─── AD-022: only the two named indexes become conflicts ─────────────

    [Fact]
    public async Task Collision_on_another_unique_index_is_not_reported_as_a_key_conflict()
    {
        const string ForeignIndex = "IX_users_Name_test_only";

        await using (var setup = fixture.CreateDbContext())
        {
            await setup.Database.ExecuteSqlRawAsync(
                $"""CREATE UNIQUE INDEX "{ForeignIndex}" ON users ("Name")"""
            );
        }

        try
        {
            await GivenRegisteredUserAsync("TICKET-1", "111111");

            await using var context = fixture.CreateDbContext();
            var repository = new UserRepository(context);

            // A 23505, but on an index this repository knows nothing about. Reporting it as one
            // of the two key conflicts would tell the caller to change a key that is already free.
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                repository.AddIfKeysFreeAsync(NewUser("TICKET-2", "222222"), CancellationToken.None)
            );
        }
        finally
        {
            await using var teardown = fixture.CreateDbContext();
            await teardown.Database.ExecuteSqlRawAsync($"""DROP INDEX "{ForeignIndex}" """);
        }
    }

    /// <summary>
    /// Which message each index maps to, asserted <em>deterministically</em>.
    /// <para>
    /// The use-case tests do cover this — `Spectators_registered_at_once_under_one_reference…`
    /// and its access-code twin assert the detail their losers receive. But a service-level
    /// pre-check answers first whenever it can, so those tests only reach this mapping when a
    /// racer slips past it, and which of them does is scheduling. Swapping the two arms was
    /// observed failing **two** race tests on one run and **one** on the next. A guard that
    /// depends on thread scheduling is not evidence (AD-026), so the mapping is also proved
    /// here, where the pre-check is bypassed and the database decides every time.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Each_colliding_key_is_reported_as_the_key_that_actually_collided()
    {
        await GivenRegisteredUserAsync("TICKET-1", "123456");

        await using var refContext = fixture.CreateDbContext();
        var byExternalRef = await new UserRepository(refContext).AddIfKeysFreeAsync(
            NewUser("TICKET-1", "222222"),
            CancellationToken.None
        );

        await using var codeContext = fixture.CreateDbContext();
        var byAccessCode = await new UserRepository(codeContext).AddIfKeysFreeAsync(
            NewUser("TICKET-2", "123456"),
            CancellationToken.None
        );

        Assert.Equal(IUserRepository.ExternalRefAlreadyRegistered, byExternalRef.AsT1.Message);
        Assert.Equal(IUserRepository.AccessCodeAlreadyInUse, byAccessCode.AsT1.Message);
    }

    [Fact]
    public async Task Failure_that_is_not_a_unique_violation_is_not_reported_as_a_conflict()
    {
        await GivenRegisteredUserAsync("TICKET-1");

        await using var context = fixture.CreateDbContext();
        var repository = new UserRepository(context);
        var user = await context.Users.SingleAsync();

        // Remove the row behind the tracked instance, so saving fails for a reason that
        // has nothing to do with either unique index.
        await using (var other = fixture.CreateDbContext())
        {
            await other.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync();
        }

        user.Update("Grace Hopper", "123456", null, null, Now.AddMinutes(1));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
            repository.SaveIfKeysFreeAsync(CancellationToken.None)
        );
    }

    // ─── AD-007: cancellation is end-to-end, not a threaded parameter ────

    [Fact]
    public async Task Registering_a_spectator_aborts_when_the_caller_has_already_cancelled()
    {
        await using var context = fixture.CreateDbContext();
        var repository = new UserRepository(context);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.AddIfKeysFreeAsync(NewUser("TICKET-1"), cancelled.Token)
        );

        Assert.Equal(0, await CountUsersAsync());
    }

    // ─── The cascade production never triggers ───────────────────────────

    /// <summary>
    /// Removal tombstones the row, so the application never issues the hard delete this covers.
    /// It is kept because the FK is what guarantees no orphaned biometric can outlive its owner
    /// if a row is ever removed out-of-band — by a future purge job, or by hand.
    /// </summary>
    [Fact]
    public async Task Face_picture_is_removed_when_its_spectators_row_is_removed()
    {
        var userId = await GivenRegisteredUserAsync("TICKET-1");

        await using var context = fixture.CreateDbContext();
        await context.Users.Where(user => user.Id == userId).ExecuteDeleteAsync();

        await using var verification = fixture.CreateDbContext();
        Assert.Equal(0, await verification.Set<FacePicture>().CountAsync());
    }
}
