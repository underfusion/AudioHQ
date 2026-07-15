using AudioHQ.App;

namespace AudioHQ.Tests;

public sealed class AudioEndpointIdentityResolverTests
{
    private static readonly AudioEndpointIdentity[] Endpoints =
    {
        new("new-tv", "Q90A (NVIDIA High Definition Audio)"),
        new("speakers", "Speakers (USB Audio)"),
    };

    [Fact]
    public void Resolve_PrefersExactEndpointId()
    {
        var resolved = AudioEndpointIdentityResolver.Resolve(
            "speakers", "outdated name", Endpoints);

        Assert.Equal("speakers", resolved);
    }

    [Fact]
    public void Resolve_UsesUniqueFriendlyNameWhenIdChanged()
    {
        var resolved = AudioEndpointIdentityResolver.Resolve(
            "old-tv", "Q90A (NVIDIA High Definition Audio)", Endpoints);

        Assert.Equal("new-tv", resolved);
    }

    [Fact]
    public void Resolve_RejectsAmbiguousOrReservedNameMatch()
    {
        var duplicate = Endpoints.Append(new AudioEndpointIdentity(
            "second-tv", "Q90A (NVIDIA High Definition Audio)"));

        Assert.Null(AudioEndpointIdentityResolver.Resolve("old-tv",
            "Q90A (NVIDIA High Definition Audio)", duplicate));
        Assert.Null(AudioEndpointIdentityResolver.Resolve("old-tv",
            "Q90A (NVIDIA High Definition Audio)", Endpoints, new HashSet<string> { "new-tv" }));
    }
}
