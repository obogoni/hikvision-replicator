using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

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
}
