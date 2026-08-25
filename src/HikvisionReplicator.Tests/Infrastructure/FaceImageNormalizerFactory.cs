using System.Diagnostics.Metrics;
using HikvisionReplicator.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Tests.Infrastructure;

internal static class FaceImageNormalizerFactory
{
    /// <summary>
    /// The normalizer publishes its instruments through a factory rather than owning a meter, so
    /// a unit test needs one even though nothing here reads a measurement — the metrics
    /// themselves are asserted at the integration level, where a request produces them.
    /// </summary>
    private static readonly IMeterFactory MeterFactory = new ServiceCollection()
        .AddMetrics()
        .BuildServiceProvider()
        .GetRequiredService<IMeterFactory>();

    /// <summary>
    /// A normalizer on the shipped A-13 envelope unless a test needs to move a bound to reach a
    /// boundary it cannot otherwise reach with a committed fixture.
    /// </summary>
    public static SkiaFaceImageNormalizer Build(Action<FaceImageOptions>? configure = null)
    {
        var options = new FaceImageOptions();
        configure?.Invoke(options);
        return new SkiaFaceImageNormalizer(Options.Create(options), MeterFactory);
    }
}
