namespace HikvisionReplicator.Api.Shared;

/// <summary>The reader accepted the operation (WRK-03).</summary>
public sealed record Delivered;

/// <summary>
/// The reader answered and refused this record (WRK-15, WRK-16). It is up — only the item is at
/// fault, so this costs the item an attempt and never pauses the reader.
/// </summary>
public sealed record Rejected(string Reason);

/// <summary>
/// The reader never judged the record — refused connection, transport error (WRK-11). Costs the
/// item nothing; consecutive ones pause the reader (WRK-12).
/// </summary>
public sealed record Unreachable(string Reason);
