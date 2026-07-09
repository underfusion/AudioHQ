using System.Collections.Generic;
using System.Linq;

namespace AudioHQ.App;

public static class MixerSettingsProjection
{
    public static void Apply(
        MixerSettings settings,
        string? preferredSourceId,
        int latencyMs,
        IEnumerable<ChannelDefinition> channels,
        IEnumerable<EqPreset> eqPresets)
    {
        settings.SourceDeviceId = preferredSourceId;
        settings.LatencyMs = latencyMs;
        settings.Channels = channels.ToList();
        settings.EqPresets = eqPresets.ToList();
    }
}
