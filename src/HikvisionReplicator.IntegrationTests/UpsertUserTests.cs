namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// The <c>UpsertUser</c> use case — <c>PUT /api/users/{externalRef}</c> — in full.
/// <para>
/// One route, three situations, and the reason they are three files rather than one class of
/// forty-four tests (AD-037). The route is a single idempotent upsert (A-2), so it is a single
/// use case and takes a single class name; the situations differ in what the registry held
/// before the call, which is the axis a reader is actually navigating by:
/// <list type="bullet">
/// <item><b>Registration</b> — the reference was never seen. The create half.</item>
/// <item><b>Amendment</b> — the reference names an active spectator. The update half.</item>
/// <item><b>Resurrection</b> — the reference names a tombstone, and the spectator comes back
/// in the row it already had.</item>
/// </list>
/// </para>
/// <para>
/// Splitting on any other axis would cut across that. A field-validation test belongs with the
/// situation whose request carries the field, not in a fourth "validation" bucket — that is how
/// the suite ends up with a test whose home nobody can predict.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public partial class UpsertUserTests(PostgresFixture fixture) : UserApiTests(fixture)
{
    /// <summary>
    /// A fixed instant the clock-controlled tests read, shared by all three parts. USR-11 puts
    /// timestamps on an injected <see cref="TimeProvider"/>, so an exact instant can be asserted
    /// rather than a tolerance.
    /// </summary>
    private static readonly DateTimeOffset Kickoff = new(2026, 8, 25, 18, 45, 0, TimeSpan.Zero);
}
