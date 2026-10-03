using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// <b>The third below-HTTP contract class, and the only one this feature adds (AD-040,
/// extending AD-036).</b>
/// <para>
/// Every other replication test drives a use case through the HTTP surface. These do not, and
/// each one earns that the same way the two persistence-contract classes do: by naming, in its
/// own doc comment, an observable the HTTP surface <em>cannot distinguish</em> — a wrong
/// implementation returns byte-identical responses to a right one. A test added here that
/// cannot state such an observable belongs in a use-case class instead.
/// </para>
/// <para>
/// Two things put this feature below HTTP at all: the queue has no read API of its own
/// (feature 8 owns that), and the schema is where its central invariant actually lives — the
/// partial unique index is the authority for REP-09, never a read-then-write check (AD-022).
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class ReplicationQueueContractTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static User NewUser(string externalRef, string accessCode = "123456") =>
        User.Create(
                externalRef,
                "Ada Lovelace",
                accessCode,
                FaceFingerprint.Create("0f1e2d3c", 51_200, 800, 600).AsT0,
                [0x01, 0x02, 0x03],
                Now
            )
            .AsT0;

    private static Device NewDevice(string ipAddress = "10.0.0.5") =>
        Device.Create("Turnstile A", ipAddress, 80, "admin", "cipher", 50_000, Now).AsT0;

    private async Task<int> GivenRegisteredDeviceAsync(string ipAddress = "10.0.0.5")
    {
        await using var context = fixture.CreateDbContext();
        var device = NewDevice(ipAddress);
        context.Devices.Add(device);
        await context.SaveChangesAsync();
        return device.Id;
    }

    private async Task<int> GivenRegisteredUserAsync(string externalRef = "TICKET-1")
    {
        await using var context = fixture.CreateDbContext();
        var user = NewUser(externalRef);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user.Id;
    }

    private async Task GivenPendingWorkAsync(
        int userId,
        int deviceId,
        ReplicationOperation operation = ReplicationOperation.Add
    )
    {
        await using var context = fixture.CreateDbContext();
        context.Replications.Add(
            Replication.Create(userId, deviceId, operation, ReplicationLane.Live, Now)
        );
        await context.SaveChangesAsync();
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

    /// <summary>What PostgreSQL does to the referencing rows when the referenced row goes away.</summary>
    private async Task<string> DeleteRuleAsync(string table, string column)
    {
        await using var context = fixture.CreateDbContext();
        var rules = await context
            .Database.SqlQuery<string>(
                $"""
                SELECT constraints.delete_rule AS "Value"
                FROM information_schema.referential_constraints constraints
                JOIN information_schema.key_column_usage columns
                  ON columns.constraint_name = constraints.constraint_name
                WHERE columns.table_name = {table} AND columns.column_name = {column}
                """
            )
            .ToListAsync();

        return Assert.Single(rules);
    }

    // ─── REP-09: the invariant is an index, so the index is what must be asserted ───

    /// <summary>
    /// The shape of the pending index.
    /// <para>
    /// HTTP cannot distinguish it. An index that is unique over the pair <em>unconditionally</em>
    /// serves every sequential round-trip identically — the second intent for a pair only
    /// collides once the first has been superseded, and a suite that never races sees the same
    /// bytes either way. Drop the filter and a legitimate second piece of work is refused; drop
    /// the uniqueness and REP-09 is enforced by nothing at all. `pg_indexes` does not lie.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Outstanding_work_is_unique_per_spectator_and_reader_only_while_it_is_pending()
    {
        var definition = await IndexDefinitionAsync(ReplicationConfiguration.PendingIndexName);

        Assert.Contains("UNIQUE", definition, StringComparison.Ordinal);
        Assert.Contains("\"UserId\", \"DeviceId\"", definition, StringComparison.Ordinal);

        // PostgreSQL re-renders the predicate with its own casts — `WHERE (("Status")::text =
        // 'Pending'::text)` — so the filter is asserted by its two load-bearing halves rather
        // than by the literal string the configuration sent.
        Assert.Contains(" WHERE ", definition, StringComparison.Ordinal);
        var filter = definition[definition.IndexOf(" WHERE ", StringComparison.Ordinal)..];
        Assert.Contains("\"Status\"", filter, StringComparison.Ordinal);
        Assert.Contains($"'{nameof(ReplicationStatus.Pending)}'", filter, StringComparison.Ordinal);
    }

    /// <summary>
    /// The drain order feature 4 inherits exists, and is not unique.
    /// <para>
    /// HTTP cannot distinguish it: an index is a performance and ordering structure, so its
    /// absence changes no response whatsoever — only the plan. Making it unique, on the other
    /// hand, would reject the second piece of work in a lane outright, and nothing in this
    /// feature's own round-trips would reveal which of the two mistakes was made.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_queue_is_indexed_by_lane_before_status_and_constrains_neither()
    {
        var definition = await IndexDefinitionAsync(ReplicationConfiguration.LaneStatusIndexName);

        Assert.DoesNotContain("UNIQUE", definition, StringComparison.Ordinal);
        Assert.Contains("\"Lane\", \"Status\"", definition, StringComparison.Ordinal);
    }

    /// <summary>
    /// That a reader can be owed a backfill once, and only once.
    /// <para>
    /// HTTP cannot distinguish it. Nothing in this feature returns an intent, and registering
    /// the same reader twice is already refused by the address index (REP-43), so the second
    /// insert never happens through a route at all. The index is what makes a repeated
    /// expansion impossible to record, and it is unfiltered on purpose: an <c>Expanded</c>
    /// intent still occupies the reader's slot.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_reader_is_owed_at_most_one_backfill_whether_or_not_it_has_been_expanded()
    {
        var definition = await IndexDefinitionAsync(BackfillIntentConfiguration.DeviceIndexName);

        Assert.Contains("UNIQUE", definition, StringComparison.Ordinal);
        Assert.Contains("\"DeviceId\"", definition, StringComparison.Ordinal);
        Assert.DoesNotContain(" WHERE ", definition, StringComparison.Ordinal);
    }

    /// <summary>
    /// That the schema carries every index the design names, under the names it names.
    /// <para>
    /// HTTP is blind to all four: an index changes a query plan, not a response body. Two of
    /// them are load-bearing beyond performance — the pending index is REP-09's only enforcement
    /// and the backfill index is REP-14's — and the repository translates a violation by
    /// matching the index <em>name</em>, so a rename degrades a 409 into a 500 with nothing
    /// else noticing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_queue_schema_carries_every_index_the_design_names()
    {
        await using var context = fixture.CreateDbContext();
        var indexes = await context
            .Database.SqlQuery<string>(
                $"""
                SELECT indexname AS "Value" FROM pg_indexes
                WHERE tablename IN ('replications', 'backfill_intents')
                """
            )
            .ToListAsync();

        Assert.Contains(ReplicationConfiguration.PendingIndexName, indexes);
        Assert.Contains(ReplicationConfiguration.LaneStatusIndexName, indexes);
        Assert.Contains(BackfillIntentConfiguration.DeviceIndexName, indexes);

        // EF's own convention index on the cascading foreign key — what keeps a device delete
        // from sequentially scanning the queue to find the rows it takes with it.
        Assert.Contains("IX_replications_DeviceId", indexes);
    }

    // ─── REP-16 / AD-034: the two foreign keys are deliberately asymmetric ───

    /// <summary>
    /// Which way each foreign key falls when its principal row is removed.
    /// <para>
    /// HTTP cannot distinguish the pair. A delete only exercises one of the two rules at a
    /// time, and the spectator rule is unreachable through the API by construction: removal
    /// tombstones the row (AD-034), so no request ever deletes a user. Swap the behaviours and
    /// deleting a reader becomes a constraint violation instead of a 204 — which is a defect in
    /// the schema, visible here, long before a route can report it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Queued_work_follows_the_reader_it_was_owed_to_and_never_the_spectator()
    {
        Assert.Equal(
            "CASCADE",
            await DeleteRuleAsync(ReplicationConfiguration.TableName, nameof(Replication.DeviceId))
        );
        Assert.Equal(
            "RESTRICT",
            await DeleteRuleAsync(ReplicationConfiguration.TableName, nameof(Replication.UserId))
        );
    }

    /// <summary>
    /// That a backfill debt dies with the reader it was owed to.
    /// <para>
    /// HTTP cannot distinguish it from the delete side: a reader with an outstanding intent
    /// either returns 204 or fails the constraint, and until the intent exists no route can
    /// tell which. The rule lives in the schema, so this is where it is read.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_backfill_debt_follows_the_reader_it_was_recorded_for()
    {
        Assert.Equal(
            "CASCADE",
            await DeleteRuleAsync(
                BackfillIntentConfiguration.TableName,
                nameof(BackfillIntent.DeviceId)
            )
        );
    }

    // ─── Operator-facing storage: words, not ordinals ───

    /// <summary>
    /// What the columns actually hold.
    /// <para>
    /// HTTP is blind to this twice over: nothing in this feature returns a replication at all,
    /// and even once feature 8 does, an ordinal and a word deserialise to the same enum member.
    /// The difference only matters where it cannot be seen from a response — an operator reading
    /// the table, and the silent re-mapping of every stored row that inserting an enum member
    /// would cause if these were numbers.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Queued_work_stores_its_operation_lane_and_status_as_words()
    {
        var userId = await GivenRegisteredUserAsync();
        var deviceId = await GivenRegisteredDeviceAsync();

        await using (var context = fixture.CreateDbContext())
        {
            context
                .Set<Replication>()
                .Add(
                    Replication.Create(
                        userId,
                        deviceId,
                        ReplicationOperation.Add,
                        ReplicationLane.Live,
                        Now
                    )
                );
            await context.SaveChangesAsync();
        }

        await using var verification = fixture.CreateDbContext();
        var stored = await verification
            .Database.SqlQuery<string>(
                $"""SELECT "Operation" || '/' || "Lane" || '/' || "Status" AS "Value" FROM replications"""
            )
            .ToListAsync();

        Assert.Equal("Add/Live/Pending", Assert.Single(stored));
    }

    // ─── REP-13: the index's refusal is a domain answer, not a 500 ───

    /// <summary>
    /// Which message a lost duplicate-pending race produces.
    /// <para>
    /// HTTP cannot distinguish it in this feature at all: nothing saves a replication through
    /// a route yet, and once the fan-out does, the mapping is reachable only when two upserts
    /// for one spectator genuinely interleave. Which racer loses is scheduling, and a guard
    /// that depends on thread scheduling is not evidence (AD-026) — so the mapping is proved
    /// here, where the index decides every time.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_second_piece_of_outstanding_work_for_a_pair_is_reported_as_a_conflict()
    {
        var userId = await GivenRegisteredUserAsync();
        var deviceId = await GivenRegisteredDeviceAsync();
        await GivenPendingWorkAsync(userId, deviceId);

        await using var context = fixture.CreateDbContext();
        context.Replications.Add(
            Replication.Create(
                userId,
                deviceId,
                ReplicationOperation.Update,
                ReplicationLane.Live,
                Now
            )
        );
        var violation = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync()
        );

        var translated = new ReplicationRepository(context).TranslateIfDuplicatePending(violation);

        // Literal text, deliberately. Comparing only against the constant proves the right
        // branch ran, not that the message is right: change the constant and assertion and
        // implementation move together, green all the way (AD-036's tautology trap).
        Assert.Equal(
            "Replication work for this user and device is already queued.",
            translated.AsT1.Message
        );
        Assert.Equal(IReplicationRepository.DuplicatePendingWork, translated.AsT1.Message);
    }

    /// <summary>
    /// That a collision somewhere else is left alone.
    /// <para>
    /// HTTP cannot distinguish a mis-translation here from a correct one: both arms end in a
    /// 409, and only the sentence differs — telling an operator its queued work collided when
    /// what actually collided was a reader's backfill slot sends them to fix the wrong thing.
    /// Provoking a violation on a specific other index needs the database, not a request.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_collision_on_another_index_is_not_reported_as_duplicate_pending_work()
    {
        var deviceId = await GivenRegisteredDeviceAsync();

        await using (var first = fixture.CreateDbContext())
        {
            first.BackfillIntents.Add(BackfillIntent.Create(deviceId, Now));
            await first.SaveChangesAsync();
        }

        await using var context = fixture.CreateDbContext();
        context.BackfillIntents.Add(BackfillIntent.Create(deviceId, Now));
        var violation = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync()
        );

        var translated = new ReplicationRepository(context).TranslateIfDuplicatePending(violation);

        Assert.True(translated.IsT0);
    }

    /// <summary>
    /// That a failure which is not a uniqueness violation at all is left alone.
    /// <para>
    /// HTTP cannot distinguish it: a swallowed foreign-key violation becomes a 409 the caller
    /// can do nothing about, where the truth is a defect that should surface. Arranging a
    /// dangling foreign key needs the database — no route can stage work for a reader that
    /// was never catalogued.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_failure_that_is_not_a_unique_violation_is_not_reported_as_duplicate_pending_work()
    {
        var userId = await GivenRegisteredUserAsync();

        await using var context = fixture.CreateDbContext();
        context.Replications.Add(
            Replication.Create(
                userId,
                deviceId: 9_999,
                ReplicationOperation.Add,
                ReplicationLane.Live,
                Now
            )
        );
        var violation = await Assert.ThrowsAsync<DbUpdateException>(() =>
            context.SaveChangesAsync()
        );

        var translated = new ReplicationRepository(context).TranslateIfDuplicatePending(violation);

        Assert.True(translated.IsT0);
    }

    /// <summary>
    /// That superseding is what makes room for the next intent.
    /// <para>
    /// HTTP cannot distinguish this until the fan-out exists, and even then only by racing.
    /// It is the other half of the pending index: if the filter were dropped the index would
    /// refuse this insert, and the queue could never record a second intent for a pair at all
    /// — which is the shape REP-08 depends on.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Superseding_outstanding_work_leaves_room_for_the_intent_that_replaced_it()
    {
        var userId = await GivenRegisteredUserAsync();
        var deviceId = await GivenRegisteredDeviceAsync();
        await GivenPendingWorkAsync(userId, deviceId);

        await using (var context = fixture.CreateDbContext())
        {
            var outstanding = await context.Replications.SingleAsync();
            outstanding.Supersede(Now.AddMinutes(1));
            context.Replications.Add(
                Replication.Create(
                    userId,
                    deviceId,
                    ReplicationOperation.Update,
                    ReplicationLane.Live,
                    Now.AddMinutes(1)
                )
            );

            await context.SaveChangesAsync();
        }

        await using var verification = fixture.CreateDbContext();
        var rows = await verification.Replications.ToListAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(
            ReplicationOperation.Update,
            Assert.Single(rows, row => row.Status == ReplicationStatus.Pending).Operation
        );
        Assert.Equal(
            ReplicationOperation.Add,
            Assert.Single(rows, row => row.Status == ReplicationStatus.Superseded).Operation
        );
    }

    // ─── The navigation exists for exactly one case: a spectator with no key yet ───

    /// <summary>
    /// That work staged for an unsaved spectator is written against the key the database
    /// issues, not against the <c>0</c> the aggregate carried when the intent was formed.
    /// <para>
    /// HTTP cannot distinguish it, and this is the trap the whole navigation exists for: the
    /// registration event is raised inside the factory, before any insert. Without the mapped
    /// navigation the row is written with <c>UserId = 0</c> — which, on an empty-enough
    /// database, is simply a foreign key to a spectator who does not exist, with the response
    /// unchanged either way.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Work_staged_for_an_unsaved_spectator_is_keyed_to_the_spectator_the_database_stored()
    {
        var deviceId = await GivenRegisteredDeviceAsync();

        int expectedUserId;
        await using (var context = fixture.CreateDbContext())
        {
            var user = NewUser("TICKET-1");
            context.Users.Add(user);
            context
                .Set<Replication>()
                .Add(
                    Replication.Create(
                        user,
                        deviceId,
                        ReplicationOperation.Add,
                        ReplicationLane.Live,
                        Now
                    )
                );

            // One save: the spectator and the work owed for them commit together (REP-07).
            await context.SaveChangesAsync();
            expectedUserId = user.Id;
        }

        await using var verification = fixture.CreateDbContext();
        var stored = await verification.Set<Replication>().SingleAsync();

        Assert.NotEqual(0, stored.UserId);
        Assert.Equal(expectedUserId, stored.UserId);
        Assert.Equal(deviceId, stored.DeviceId);
    }

    // ─── REP-17…REP-20, REP-44…REP-46: the backfill expansion ───────────
    //
    // The expansion is the strongest form of AD-040's blind spot: it has no HTTP surface at
    // all. Feature 8 owns the queue's API and feature 4 owns its execution, so there is no
    // response to compare a right implementation against a wrong one — every test below
    // still names its own observable, because the rule is the sentence and not the count.

    private async Task<int> GivenRecordedDebtAsync(int deviceId)
    {
        await using var context = fixture.CreateDbContext();
        var debt = BackfillIntent.Create(deviceId, Now);
        context.BackfillIntents.Add(debt);
        await context.SaveChangesAsync();
        return debt.Id;
    }

    private async Task GivenActiveSpectatorsAsync(int count)
    {
        await using var context = fixture.CreateDbContext();
        for (var index = 0; index < count; index++)
            context.Users.Add(NewUser($"TICKET-{index}", $"{100_000 + index}"));
        await context.SaveChangesAsync();
    }

    private async Task<int> GivenTombstonedSpectatorAsync(string externalRef, string accessCode)
    {
        await using var context = fixture.CreateDbContext();
        var user = NewUser(externalRef, accessCode);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        user.MarkDeleted(Now.AddMinutes(1));
        await context.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>
    /// Runs the expansion the way feature 4 will: resolved from the container, invoked
    /// directly, and committed by the caller's own save — because it stages and never saves
    /// (AD-041).
    /// </summary>
    private async Task<int> ExpandAsync(int deviceId, bool commit = true)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var fanOut = scope.ServiceProvider.GetRequiredService<ReplicationFanOut>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var staged = await fanOut.ExpandBackfillAsync(deviceId, Now, CancellationToken.None);

        if (commit)
            await context.SaveChangesAsync();

        return staged;
    }

    private async Task<List<Replication>> QueuedWorkAsync()
    {
        await using var context = fixture.CreateDbContext();
        return await context.Replications.OrderBy(work => work.Id).ToListAsync();
    }

    /// <summary>
    /// What a reader's recorded debt actually turns into.
    /// <para>
    /// HTTP cannot distinguish it, in the strongest sense AD-040 describes: nothing invokes
    /// the expansion over a route and nothing returns a replication, so there is no response
    /// at all to tell a correct expansion from one that staged the wrong lane, the wrong
    /// operation, or nothing.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Expanding_a_debt_queues_one_bulk_enrolment_per_active_spectator()
    {
        var deviceId = await GivenRegisteredDeviceAsync();
        await GivenRecordedDebtAsync(deviceId);
        await GivenActiveSpectatorsAsync(5);

        var staged = await ExpandAsync(deviceId);

        Assert.Equal(5, staged);

        var queued = await QueuedWorkAsync();
        Assert.Equal(5, queued.Count);
        Assert.All(
            queued,
            work =>
            {
                Assert.Equal(ReplicationOperation.Add, work.Operation);

                // Bulk, not Live. AD-038's whole preemption relationship is that a spectator
                // at a turnstile goes ahead of a roster being loaded; staging a backfill on
                // the live lane would put 50,000 rows in front of them.
                Assert.Equal(ReplicationLane.Bulk, work.Lane);
                Assert.Equal(ReplicationStatus.Pending, work.Status);
                Assert.Equal(deviceId, work.DeviceId);
            }
        );
    }

    /// <summary>
    /// That a spectator whose ticket was refunded is not enrolled by the backfill.
    /// <para>
    /// HTTP cannot distinguish it: a tombstoned spectator already reads as not found on
    /// every route (USR-31), so enrolling them anyway changes no response — it puts a
    /// destroyed face on a turnstile and nothing in the API says so.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Expanding_a_debt_skips_tombstoned_spectators()
    {
        var deviceId = await GivenRegisteredDeviceAsync();
        await GivenRecordedDebtAsync(deviceId);
        await GivenActiveSpectatorsAsync(5);
        var refunded = await GivenTombstonedSpectatorAsync("REFUNDED-1", "900001");
        var alsoRefunded = await GivenTombstonedSpectatorAsync("REFUNDED-2", "900002");

        var staged = await ExpandAsync(deviceId);

        Assert.Equal(5, staged);

        var queued = await QueuedWorkAsync();
        Assert.DoesNotContain(queued, work => work.UserId == refunded);
        Assert.DoesNotContain(queued, work => work.UserId == alsoRefunded);
    }

    /// <summary>
    /// That a live intent outranks a backfill one.
    /// <para>
    /// HTTP cannot distinguish it twice over: the expansion has no route, and the duplicate
    /// a wrong implementation would stage is refused by the pending index rather than
    /// returned to anyone. Staging it would either fail the whole expansion or, with the
    /// filter dropped, demote work the live lane was meant to carry first.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_spectator_already_owed_work_by_the_reader_keeps_their_live_intent()
    {
        var deviceId = await GivenRegisteredDeviceAsync();
        await GivenRecordedDebtAsync(deviceId);
        await GivenActiveSpectatorsAsync(3);

        await using (var context = fixture.CreateDbContext())
        {
            var first = await context.Users.OrderBy(user => user.Id).FirstAsync();
            await GivenPendingWorkAsync(first.Id, deviceId);
        }

        var staged = await ExpandAsync(deviceId);

        Assert.Equal(2, staged);

        var queued = await QueuedWorkAsync();
        Assert.Equal(3, queued.Count);
        Assert.Single(queued, work => work.Lane == ReplicationLane.Live);
        Assert.Equal(3, queued.Select(work => work.UserId).Distinct().Count());
    }

    /// <summary>
    /// That a settled debt is recorded as settled, with the clock it was settled by.
    /// <para>
    /// HTTP cannot distinguish it: no route returns an intent, and the status is the only
    /// thing that stops the roster being queued a second time — which is itself invisible
    /// until the queue is read.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Expanding_a_debt_settles_it()
    {
        var deviceId = await GivenRegisteredDeviceAsync();
        await GivenRecordedDebtAsync(deviceId);
        await GivenActiveSpectatorsAsync(2);

        await ExpandAsync(deviceId);

        await using var context = fixture.CreateDbContext();
        var debt = await context.BackfillIntents.SingleAsync();
        Assert.Equal(BackfillStatus.Expanded, debt.Status);
        Assert.Equal(Now, debt.ExpandedAt);
    }

    /// <summary>
    /// That expanding a settled debt does nothing at all.
    /// <para>
    /// HTTP cannot distinguish it: whatever drains the queue gets no response a route can
    /// show, and a second expansion that staged the roster again would be refused row by
    /// row by the pending index — turning an idempotent no-op into a failure nobody asked
    /// for.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Expanding_a_debt_a_second_time_queues_nothing_further()
    {
        var deviceId = await GivenRegisteredDeviceAsync();
        await GivenRecordedDebtAsync(deviceId);
        await GivenActiveSpectatorsAsync(4);
        await ExpandAsync(deviceId);
        var afterTheFirst = (await QueuedWorkAsync()).Select(work => work.Id).ToList();

        var staged = await ExpandAsync(deviceId);

        Assert.Equal(0, staged);
        Assert.Equal(afterTheFirst, (await QueuedWorkAsync()).Select(work => work.Id));
    }

    /// <summary>
    /// That a debt owed to a decommissioned reader is simply gone.
    /// <para>
    /// HTTP cannot distinguish it: the delete already answered 204 and the expansion answers
    /// nobody. A throw here would surface in whatever drains the queue as a failure to retry
    /// forever, for a reader that will never come back.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Expanding_a_debt_for_a_reader_that_has_been_deleted_queues_nothing()
    {
        var deviceId = await GivenRegisteredDeviceAsync();
        await GivenRecordedDebtAsync(deviceId);
        await GivenActiveSpectatorsAsync(3);

        await using (var context = fixture.CreateDbContext())
        {
            await context.Devices.Where(device => device.Id == deviceId).ExecuteDeleteAsync();
        }

        var staged = await ExpandAsync(deviceId);

        Assert.Equal(0, staged);
        Assert.Empty(await QueuedWorkAsync());
    }

    /// <summary>
    /// That two expansions of the same debt cannot leave a spectator owed the same work
    /// twice.
    /// <para>
    /// HTTP cannot distinguish it — the expansion has no route — and the duplicate would not
    /// be visible from one either: both callers are whatever drains the queue. The pending
    /// index is the arbiter, exactly as it is for two upserts, and this is where that can be
    /// made to happen.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Expanding_a_debt_concurrently_leaves_one_piece_of_work_per_spectator()
    {
        var deviceId = await GivenRegisteredDeviceAsync();
        await GivenRecordedDebtAsync(deviceId);
        await GivenActiveSpectatorsAsync(3);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var racers = Enumerable
            .Range(0, 2)
            .Select(async _ =>
            {
                await start.Task;
                try
                {
                    return await ExpandAsync(deviceId);
                }
                catch (DbUpdateException)
                {
                    // The loser's save is refused by the pending index, which is the
                    // guarantee rather than a defect: nothing of its roster is committed.
                    return 0;
                }
            })
            .ToList();

        start.SetResult();
        await Task.WhenAll(racers);

        var queued = await QueuedWorkAsync();
        Assert.Equal(3, queued.Count);
        Assert.Equal(3, queued.Select(work => work.UserId).Distinct().Count());
        Assert.All(queued, work => Assert.Equal(ReplicationStatus.Pending, work.Status));
    }

    /// <summary>
    /// That the expansion stages into its caller's transaction and commits nothing itself.
    /// <para>
    /// HTTP cannot distinguish it — there is no route — and neither can a test that saves
    /// straight afterwards: both end with the rows in the table. The difference only shows
    /// when the caller's save never comes, which is the case AD-041 exists for.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_expansion_stages_the_roster_without_committing_it()
    {
        var deviceId = await GivenRegisteredDeviceAsync();
        await GivenRecordedDebtAsync(deviceId);
        await GivenActiveSpectatorsAsync(3);

        var staged = await ExpandAsync(deviceId, commit: false);

        Assert.Equal(3, staged);
        Assert.Empty(await QueuedWorkAsync());

        await using var context = fixture.CreateDbContext();
        Assert.Equal(BackfillStatus.Pending, (await context.BackfillIntents.SingleAsync()).Status);
    }

    /// <summary>
    /// That a debt owed when the registry is empty is still settled.
    /// <para>
    /// HTTP cannot distinguish it: nothing is queued either way, and the only difference is
    /// whether the debt stays outstanding — which, left Pending, would re-expand against
    /// whatever roster exists by then and silently re-run on every sweep.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Expanding_a_debt_owed_when_nobody_is_registered_still_settles_it()
    {
        var deviceId = await GivenRegisteredDeviceAsync();
        await GivenRecordedDebtAsync(deviceId);

        var staged = await ExpandAsync(deviceId);

        Assert.Equal(0, staged);
        Assert.Empty(await QueuedWorkAsync());

        await using var context = fixture.CreateDbContext();
        Assert.Equal(
            BackfillStatus.Expanded,
            (await context.BackfillIntents.SingleAsync()).Status
        );
    }
}
