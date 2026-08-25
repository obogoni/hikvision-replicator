namespace HikvisionReplicator.Api.Domain;

/// <summary>
/// The canonical JPEG bytes, held apart from <see cref="User"/> so the catalogue can be
/// queried without loading them (A-1). Deliberately <b>not</b> an
/// <see cref="Shared.IAggregateRoot"/>: it has no repository and is reachable only through
/// its user. Its content is always a normalizer derivative, so an empty array is a
/// programming error rather than a caller mistake.
/// </summary>
public class FacePicture
{
    public const string EmptyContent = "Face picture content cannot be empty.";

    public int Id { get; private set; }
    public int UserId { get; private set; }
    public byte[] Content { get; private set; } = [];

    private FacePicture() { } // for EF Core

    private FacePicture(byte[] content) => Content = content;

    internal static FacePicture ForUser(byte[] content)
    {
        EnsureNotEmpty(content);
        return new FacePicture(content);
    }

    internal void Replace(byte[] content)
    {
        EnsureNotEmpty(content);
        Content = content;
    }

    private static void EnsureNotEmpty(byte[] content)
    {
        if (content is null || content.Length == 0)
            throw new ArgumentException(EmptyContent, nameof(content));
    }
}
