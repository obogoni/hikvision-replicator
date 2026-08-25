using HikvisionReplicator.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Tests.Infrastructure;

internal static class FaceImageNormalizerFactory
{
    /// <summary>
    /// A normalizer on the shipped A-13 envelope unless a test needs to move a bound to reach a
    /// boundary it cannot otherwise reach with a committed fixture.
    /// </summary>
    public static SkiaFaceImageNormalizer Build(Action<FaceImageOptions>? configure = null)
    {
        var options = new FaceImageOptions();
        configure?.Invoke(options);
        return new SkiaFaceImageNormalizer(Options.Create(options));
    }
}
