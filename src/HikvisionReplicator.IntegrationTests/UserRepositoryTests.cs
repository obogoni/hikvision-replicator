using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;
using OneOf;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// The database is the authority for both uniqueness rules (AD-022), so these tests drive the
/// repository directly — deliberately bypassing any service-level pre-check — and assert that a
/// lost race arrives as a domain error rather than an exception. An exception here is what a
/// caller would experience as a 500 where the specification promises a conflict.
/// <para>
/// The two collisions are asserted separately and with their own messages, because the
/// translation keys off index names: renaming one index would otherwise degrade its conflict
/// into an unhandled failure with nothing to catch it.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class UserRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const int Racers = 4;

    private static readonly DateTime Now = new(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static User NewUser(
        string externalRef,
        string accessCode = "123456",
        string name = "Ada Lovelace"
    ) =>
        User.Create(
                externalRef,
                name,
                accessCode,
                FaceFingerprint.Create("0f1e2d3c", 51_200, 800, 600).AsT0,
                [0x01, 0x02, 0x03],
                Now
            )
            .AsT0;

    private async Task GivenRegisteredUserAsync(string externalRef, string accessCode = "123456")
    {
        await using var context = fixture.CreateDbContext();
        context.Users.Add(NewUser(externalRef, accessCode));
        await context.SaveChangesAsync();
    }

    private async Task<int> CountUsersAsync()
    {
        await using var context = fixture.CreateDbContext();
        return await context.Users.CountAsync();
    }

    /// <summary>
    /// Fires <see cref="Racers"/> inserts at the database at once, each on its own connection and
    /// released together, so the collision is decided by PostgreSQL rather than by a check any of
    /// them ran first. Anything that escapes as an exception fails the test rather than being
    /// counted as a conflict.
    /// </summary>
    private async Task<IReadOnlyList<OneOf<Success, ConflictError>>> RaceToRegisterAsync(
        Func<int, User> candidate
    )
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var racers = Enumerable
            .Range(0, Racers)
            .Select(async index =>
            {
                await using var context = fixture.CreateDbContext();
                var repository = new UserRepository(context);
                await start.Task;
                return await repository.AddIfKeysFreeAsync(
                    candidate(index),
                    CancellationToken.None
                );
            })
            .ToList();

        start.SetResult();
        return await Task.WhenAll(racers);
    }

    // ─── The insert path ─────────────────────────────────────────────────

    [Fact]
    public async Task Spectator_with_free_keys_is_stored()
    {
        await using var context = fixture.CreateDbContext();
        var repository = new UserRepository(context);

        var result = await repository.AddIfKeysFreeAsync(
            NewUser("TICKET-1"),
            CancellationToken.None
        );

        Assert.True(result.IsT0);

        await using var verification = fixture.CreateDbContext();
        var stored = await verification.Users.SingleAsync();
        Assert.Equal("TICKET-1", stored.ExternalRef.Value);
        Assert.Equal("123456", stored.AccessCode.Value);
    }

    [Fact]
    public async Task Spectator_reusing_a_registered_external_reference_is_rejected_as_a_conflict()
    {
        await GivenRegisteredUserAsync("TICKET-1");

        await using var context = fixture.CreateDbContext();
        var repository = new UserRepository(context);

        var result = await repository.AddIfKeysFreeAsync(
            NewUser("TICKET-1", "222222"),
            CancellationToken.None
        );

        Assert.True(result.IsT1);
        Assert.Equal(IUserRepository.ExternalRefAlreadyRegistered, result.AsT1.Message);
        Assert.Equal(1, await CountUsersAsync());
    }

    [Fact]
    public async Task Spectator_reusing_an_active_access_code_is_rejected_as_a_conflict()
    {
        await GivenRegisteredUserAsync("TICKET-1", "123456");

        await using var context = fixture.CreateDbContext();
        var repository = new UserRepository(context);

        var result = await repository.AddIfKeysFreeAsync(
            NewUser("TICKET-2", "123456"),
            CancellationToken.None
        );

        Assert.True(result.IsT1);
        Assert.Equal(IUserRepository.AccessCodeAlreadyInUse, result.AsT1.Message);
        Assert.Equal(1, await CountUsersAsync());
    }

    [Fact]
    public async Task Colliding_key_is_named_differently_depending_on_which_one_collided()
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

        Assert.NotEqual(byExternalRef.AsT1.Message, byAccessCode.AsT1.Message);
    }

    // AD-007 requires cancellation to be end-to-end. Threading the token
    // through a signature proves nothing — only aborting on a cancelled one does.

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

    // ─── USR-07 / USR-08: the real races ─────────────────────────────────

    [Fact]
    public async Task Spectators_registered_at_once_under_one_external_reference_yield_one_user()
    {
        var results = await RaceToRegisterAsync(index => NewUser("TICKET-1", $"10000{index}"));

        Assert.Single(results, result => result.IsT0);
        Assert.All(
            results.Where(result => result.IsT1),
            result =>
                Assert.Equal(IUserRepository.ExternalRefAlreadyRegistered, result.AsT1.Message)
        );
        Assert.Equal(1, await CountUsersAsync());
    }

    [Fact]
    public async Task Spectators_claiming_one_access_code_at_once_yield_one_user()
    {
        var results = await RaceToRegisterAsync(index => NewUser($"TICKET-{index}", "123456"));

        Assert.Single(results, result => result.IsT0);
        Assert.All(
            results.Where(result => result.IsT1),
            result => Assert.Equal(IUserRepository.AccessCodeAlreadyInUse, result.AsT1.Message)
        );
        Assert.Equal(1, await CountUsersAsync());
    }

    // ─── The update path ─────────────────────────────────────────────────

    [Fact]
    public async Task Access_code_change_onto_another_active_spectators_code_is_a_conflict()
    {
        await GivenRegisteredUserAsync("TICKET-1", "111111");
        await GivenRegisteredUserAsync("TICKET-2", "222222");

        await using var context = fixture.CreateDbContext();
        var repository = new UserRepository(context);
        var user = await context.Users.SingleAsync(u =>
            u.ExternalRef == ExternalRef.Create("TICKET-2").AsT0
        );
        user.Update("Ada Lovelace", "111111", null, null, Now.AddMinutes(1));

        var result = await repository.SaveIfKeysFreeAsync(CancellationToken.None);

        Assert.True(result.IsT1);
        Assert.Equal(IUserRepository.AccessCodeAlreadyInUse, result.AsT1.Message);

        await using var verification = fixture.CreateDbContext();
        var persisted = await verification.Users.SingleAsync(u =>
            u.ExternalRef == ExternalRef.Create("TICKET-2").AsT0
        );
        Assert.Equal("222222", persisted.AccessCode.Value);
    }

    [Fact]
    public async Task Access_code_change_onto_a_free_code_is_stored()
    {
        await GivenRegisteredUserAsync("TICKET-1", "111111");

        await using var context = fixture.CreateDbContext();
        var repository = new UserRepository(context);
        var user = await context.Users.SingleAsync();
        user.Update("Ada Lovelace", "999999", null, null, Now.AddMinutes(1));

        var result = await repository.SaveIfKeysFreeAsync(CancellationToken.None);

        Assert.True(result.IsT0);

        await using var verification = fixture.CreateDbContext();
        Assert.Equal("999999", (await verification.Users.SingleAsync()).AccessCode.Value);
    }

    // ─── AD-022: only these two constraints are translated ───────────────

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
}
