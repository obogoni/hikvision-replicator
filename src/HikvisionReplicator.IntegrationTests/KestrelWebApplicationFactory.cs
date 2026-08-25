using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// The application on a real Kestrel socket instead of the in-memory test server.
/// <para>
/// <b>Only for what the in-memory server cannot answer.</b> <c>TestServer</c> implements no
/// <c>IHttpMaxRequestBodySizeFeature</c>, so a route's request-size limit is silently
/// unenforceable there: an oversized body is read in full and refused later by application code,
/// which is the opposite of what USR-19 asks for at the transport layer. Everything else belongs
/// on the ordinary in-memory harness, which is faster and needs no port.
/// </para>
/// </summary>
internal sealed class KestrelWebApplicationFactory(
    string connectionString,
    Action<IWebHostBuilder> configure
) : TestWebApplicationFactory(connectionString)
{
    private IHost? _liveHost;

    /// <summary>Where the live server ended up listening.</summary>
    public Uri BaseAddress { get; private set; } = null!;

    public HttpClient CreateLiveClient() => new() { BaseAddress = BaseAddress };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        configure(builder);
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        // The in-memory host is built first, from the untouched builder, because switching the
        // builder to Kestrel below is not reversible.
        var inMemoryHost = builder.Build();

        // Port 0: the operating system picks a free one, so parallel test classes never collide.
        builder.ConfigureWebHost(web => web.UseKestrel().UseUrls("http://127.0.0.1:0"));

        _liveHost = builder.Build();
        _liveHost.Start();

        var addresses = _liveHost
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>();

        BaseAddress = new Uri(addresses!.Addresses.First());

        inMemoryHost.Start();
        return inMemoryHost;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _liveHost?.Dispose();

        base.Dispose(disposing);
    }
}
