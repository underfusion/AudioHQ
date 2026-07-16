using System;

namespace AudioHQ.Core;

/// <summary>
/// The output device wants a different number of channels than the source produces, and it
/// refused every shared-mode format we offered.
///
/// The mirror pipeline resamples the SAMPLE RATE per output but does not re-map channels, so
/// feeding a stereo capture to a device whose mix format is 5.1/7.1 fails at init. WASAPI
/// reports that as a bare "format not supported" (0x88890008), which tells the user nothing
/// about what is actually wrong or what to do - hence this dedicated failure.
///
/// Raised only AFTER both init attempts have failed, so a driver that would have accepted the
/// format is never pre-emptively rejected.
/// </summary>
public sealed class ChannelCountMismatchException : Exception
{
    public ChannelCountMismatchException(int sourceChannels, int deviceChannels, Exception inner)
        : base($"Source is {sourceChannels}-channel but the output device expects {deviceChannels}.", inner)
    {
        SourceChannels = sourceChannels;
        DeviceChannels = deviceChannels;
    }

    /// <summary>Channel count the capture (and therefore the pipeline) produces.</summary>
    public int SourceChannels { get; }

    /// <summary>Channel count the output device's shared-mode mix format requires.</summary>
    public int DeviceChannels { get; }
}
