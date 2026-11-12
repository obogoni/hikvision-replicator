using System.Net;
using System.Net.Security;
using HikvisionReplicator.Api.Infrastructure.Isapi;
using HikvisionReplicator.Api.Shared;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace HikvisionReplicator.Tests.Infrastructure.Isapi;

/// <summary>
/// One authenticated client per reader identity, and the hold on refused credentials
/// (ISA-21, ISA-22, ISA-37). No socket is opened: the handler is inspected, not used.
/// </summary>
public class ReaderConnectionsTests
{
    private static readonly DeviceEndpoint FrontGate = new(1, "10.0.0.5", 443, "admin", "Front-Pa55");
    private static readonly DeviceEndpoint BackGate = new(2, "10.0.0.6", 443, "admin", "Back-Pa55");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 12, 18, 0, 0, TimeSpan.Zero));
    private readonly List<TrackedHandler> _handlers = [];

    private ReaderConnections NewConnections() =>
        new(
            _clock,
            _ =>
            {
                var handler = new TrackedHandler();
                _handlers.Add(handler);
                return handler;
            }
        );

    private sealed class TrackedHandler : HttpMessageHandler
    {
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    // ─── A client per reader identity ───

    [Fact]
    public void The_same_reader_twice_gets_the_same_client()
    {
        using var connections = NewConnections();

        var first = connections.ClientFor(FrontGate);
        var second = connections.ClientFor(FrontGate with { });

        Assert.Same(first, second);
        Assert.Single(_handlers);
    }

    public static TheoryData<DeviceEndpoint> ChangedIdentities =>
        new()
        {
            FrontGate with { Password = "Front-Pa56" },
            FrontGate with { Username = "operator" },
            FrontGate with { IpAddress = "10.0.0.7" },
            FrontGate with { Port = 8443 },
        };

    [Theory]
    [MemberData(nameof(ChangedIdentities))]
    public async Task A_changed_identity_gets_a_new_client_and_the_old_one_is_disposed(
        DeviceEndpoint changed
    )
    {
        using var connections = NewConnections();
        var old = connections.ClientFor(FrontGate);

        var replacement = connections.ClientFor(changed);

        Assert.NotSame(old, replacement);
        Assert.Equal(2, _handlers.Count);
        Assert.True(_handlers[0].Disposed);
        Assert.False(_handlers[1].Disposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => old.GetAsync("/"));
    }

    [Fact]
    public void Another_readers_client_is_unaffected_when_one_reader_changes()
    {
        using var connections = NewConnections();
        var back = connections.ClientFor(BackGate);
        connections.ClientFor(FrontGate);

        connections.ClientFor(FrontGate with { Password = "Front-Pa56" });

        Assert.Same(back, connections.ClientFor(BackGate));
        Assert.False(_handlers[0].Disposed);
    }

    [Fact]
    public void Disposing_releases_every_client()
    {
        var connections = NewConnections();
        connections.ClientFor(FrontGate);
        connections.ClientFor(BackGate);

        connections.Dispose();

        Assert.All(_handlers, handler => Assert.True(handler.Disposed));
    }

    // ─── The hold (ISA-37) ───

    [Fact]
    public void A_hold_is_reported_until_its_time()
    {
        using var connections = NewConnections();
        connections.Hold(FrontGate, "authentication failed", _clock.GetUtcNow().AddMinutes(30));

        _clock.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromTicks(1));

        Assert.True(connections.IsHeld(FrontGate, out var reason));
        Assert.Equal("authentication failed", reason);
    }

    [Fact]
    public void A_hold_is_not_reported_once_its_time_has_come()
    {
        using var connections = NewConnections();
        connections.Hold(FrontGate, "authentication failed", _clock.GetUtcNow().AddMinutes(30));

        _clock.Advance(TimeSpan.FromMinutes(30));

        Assert.False(connections.IsHeld(FrontGate, out var reason));
        Assert.Null(reason);
    }

    [Fact]
    public void Changed_credentials_are_not_held()
    {
        using var connections = NewConnections();
        connections.Hold(FrontGate, "authentication failed", _clock.GetUtcNow().AddMinutes(30));

        Assert.False(connections.IsHeld(FrontGate with { Password = "Front-Pa56" }, out _));
        Assert.False(connections.IsHeld(BackGate, out _));
    }

    // ─── The handler and client (ISA-21, ISA-22) ───

    [Fact]
    public void The_handler_offers_the_readers_credentials_by_digest_only()
    {
        using var handler = ReaderConnections.CreateHandler(FrontGate, new IsapiOptions());
        var cache = Assert.IsType<CredentialCache>(handler.Credentials);
        var reader = new Uri("https://10.0.0.5:443/");

        var digest = cache.GetCredential(reader, "Digest");
        Assert.NotNull(digest);
        Assert.Equal("admin", digest.UserName);
        Assert.Equal("Front-Pa55", digest.Password);
        Assert.Null(cache.GetCredential(reader, "Basic"));
        Assert.Null(cache.GetCredential(reader, "NTLM"));
        Assert.False(handler.PreAuthenticate);
    }

    [Fact]
    public void The_handler_accepts_the_readers_self_signed_certificate()
    {
        using var handler = ReaderConnections.CreateHandler(FrontGate, new IsapiOptions());

        var callback = handler.SslOptions.RemoteCertificateValidationCallback;

        Assert.NotNull(callback);
        Assert.True(callback(this, null, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void The_handler_follows_no_redirect_and_recycles_connections()
    {
        var options = new IsapiOptions { ConnectionLifetime = TimeSpan.FromMinutes(7) };

        using var handler = ReaderConnections.CreateHandler(FrontGate, options);

        Assert.False(handler.AllowAutoRedirect);
        Assert.Equal(TimeSpan.FromMinutes(7), handler.PooledConnectionLifetime);
    }

    [Fact]
    public void The_client_speaks_https_to_the_readers_address_with_no_timeout_of_its_own()
    {
        using var connections = new ReaderConnections(
            Options.Create(new IsapiOptions()),
            _clock
        );

        var client = connections.ClientFor(FrontGate);

        Assert.Equal("https://10.0.0.5/", client.BaseAddress!.AbsoluteUri);
        Assert.Equal(443, client.BaseAddress.Port);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Fact]
    public void An_ipv6_reader_is_addressed_in_brackets()
    {
        using var connections = new ReaderConnections(
            Options.Create(new IsapiOptions()),
            _clock
        );

        var client = connections.ClientFor(FrontGate with { IpAddress = "::1", Port = 8443 });

        Assert.Equal("https://[::1]:8443/", client.BaseAddress!.AbsoluteUri);
    }
}
