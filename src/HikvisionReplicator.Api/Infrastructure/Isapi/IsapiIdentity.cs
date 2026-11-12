using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HikvisionReplicator.Api.Infrastructure.Isapi;

/// <summary>
/// How a spectator is known on a reader: the key (A-2) and the name it can hold (ISA-26).
/// </summary>
public static class IsapiIdentity
{
    /// <summary>
    /// A-2: lowercase hex of the first 16 bytes of SHA-256 over the UTF-8 reference — 32 ASCII
    /// characters, whatever the reference holds, and the same in every process.
    /// </summary>
    public static string EmployeeNo(string externalRef)
    {
        ArgumentNullException.ThrowIfNull(externalRef);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(externalRef));
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// ISA-26: the longest prefix of whole text elements that fits in <paramref name="max"/>
    /// UTF-16 characters, so a surrogate pair or a combining sequence is never split. A name
    /// that fits is returned unchanged.
    /// </summary>
    public static string TruncateName(string name, int max)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length <= max)
            return name;

        var length = 0;
        while (length < name.Length)
        {
            var next = StringInfo.GetNextTextElementLength(name, length);
            if (length + next > max)
                break;
            length += next;
        }

        return name[..length];
    }
}
