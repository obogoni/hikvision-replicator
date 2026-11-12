using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using HikvisionReplicator.Api.Shared;
using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Api.Infrastructure.Isapi;

/// <summary>
/// One warm, digest-authenticated client per reader, and the memory of credentials a reader has
/// refused (design § ReaderConnections). An interface so a test can put a scripted handler under
/// the adapter.
/// </summary>
public interface IReaderConnections
{
    /// <summary>The client for this endpoint's current identity; a changed identity replaces it.</summary>
    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "The design names it For; the codebase is C# only, so the VB keyword clash is moot."
    )]
    HttpClient For(DeviceEndpoint endpoint);

    /// <summary>
    /// ISA-37: whether this endpoint's credentials are held after the reader refused them, and
    /// the reason it gave. Credentials that changed since are never held.
    /// </summary>
    bool IsHeld(DeviceEndpoint endpoint, [NotNullWhen(true)] out string? reason);

    /// <summary>ISA-37: hold this endpoint's credentials until <paramref name="until"/>.</summary>
    void Hold(DeviceEndpoint endpoint, string reason, DateTimeOffset until);
}

/// <summary>
/// Clients are keyed by <c>(DeviceId, IpAddress, Port, Username, SHA-256(Password))</c>, so the
/// key never holds the plaintext password. A new key for a device disposes that device's old
/// client: the worker never has two calls in flight to one reader (WRK-06) and reloads the
/// endpoint before each (WRK-33), so nothing is using it. A singleton; the worker's device loops
/// share it across threads.
/// </summary>
public sealed class ReaderConnections : IReaderConnections, IDisposable
{
    private readonly Func<DeviceEndpoint, HttpMessageHandler> _handlerFactory;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _gate = new();
    private readonly Dictionary<int, (ReaderKey Key, HttpClient Client)> _clients = [];
    private readonly Dictionary<int, (ReaderKey Key, string Reason, DateTimeOffset Until)> _holds = [];
    private bool _disposed;

    public ReaderConnections(IOptions<IsapiOptions> options, TimeProvider timeProvider)
        : this(timeProvider, endpoint => CreateHandler(endpoint, options.Value)) { }

    /// <summary>For tests: the handler under every client comes from <paramref name="handlerFactory"/>.</summary>
    internal ReaderConnections(
        TimeProvider timeProvider,
        Func<DeviceEndpoint, HttpMessageHandler> handlerFactory
    )
    {
        _timeProvider = timeProvider;
        _handlerFactory = handlerFactory;
    }

    public HttpClient For(DeviceEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var key = ReaderKey.Of(endpoint);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_clients.TryGetValue(endpoint.DeviceId, out var current))
            {
                if (current.Key == key)
                    return current.Client;

                current.Client.Dispose();
            }

            // Timeout is infinite: only the worker's token governs time (WRK-11).
            var client = new HttpClient(_handlerFactory(endpoint), disposeHandler: true)
            {
                BaseAddress = BaseAddressOf(endpoint),
                Timeout = Timeout.InfiniteTimeSpan,
            };
            _clients[endpoint.DeviceId] = (key, client);
            return client;
        }
    }

    public bool IsHeld(DeviceEndpoint endpoint, [NotNullWhen(true)] out string? reason)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var key = ReaderKey.Of(endpoint);

        lock (_gate)
        {
            if (
                _holds.TryGetValue(endpoint.DeviceId, out var hold)
                && hold.Key == key
                && _timeProvider.GetUtcNow() < hold.Until
            )
            {
                reason = hold.Reason;
                return true;
            }
        }

        reason = null;
        return false;
    }

    public void Hold(DeviceEndpoint endpoint, string reason, DateTimeOffset until)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var key = ReaderKey.Of(endpoint);

        lock (_gate)
            _holds[endpoint.DeviceId] = (key, reason, until);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            foreach (var (_, client) in _clients.Values)
                client.Dispose();
            _clients.Clear();
        }
    }

    /// <summary>
    /// ISA-21 and ISA-22: digest only (a <see cref="CredentialCache"/> entry for <c>Digest</c>
    /// never falls back to Basic), the reader's self-signed certificate accepted on this handler
    /// alone, no redirects followed, and pooled connections recycled.
    /// </summary>
    [SuppressMessage(
        "Security",
        "CA5359:Do not disable certificate validation",
        Justification = "A-5 / ISA-22: readers present self-signed certificates; accepted on this handler only, never process-wide."
    )]
    internal static SocketsHttpHandler CreateHandler(DeviceEndpoint endpoint, IsapiOptions options)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(options);

        var credentials = new CredentialCache
        {
            {
                BaseAddressOf(endpoint),
                "Digest",
                new NetworkCredential(endpoint.Username, endpoint.Password)
            },
        };

        var handler = new SocketsHttpHandler
        {
            Credentials = credentials,
            PreAuthenticate = false,
            AllowAutoRedirect = false,
            PooledConnectionLifetime = options.ConnectionLifetime,
        };
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        return handler;
    }

    /// <summary>HTTPS to <c>IpAddress:Port</c>; an IPv6 address is bracketed.</summary>
    internal static Uri BaseAddressOf(DeviceEndpoint endpoint) =>
        new UriBuilder(Uri.UriSchemeHttps, endpoint.IpAddress, endpoint.Port).Uri;

    private readonly record struct ReaderKey(
        int DeviceId,
        string IpAddress,
        int Port,
        string Username,
        string PasswordHash
    )
    {
        public static ReaderKey Of(DeviceEndpoint endpoint) =>
            new(
                endpoint.DeviceId,
                endpoint.IpAddress,
                endpoint.Port,
                endpoint.Username,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint.Password)))
            );
    }
}
