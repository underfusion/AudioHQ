using System;
using AudioHQ.Core;

namespace AudioHQ.App.ViewModels;

/// <summary>
/// The seam between a channel's state machine and real WASAPI hardware.
///
/// <see cref="ChannelLifecycleController"/> decides WHEN to open, close and retry an output;
/// this decides what actually happens when it tries. Everything device-shaped lives behind
/// here, so the state machine can be exercised against a fake - including the paths that are
/// impossible to arrange on a real desk (a device that fails exactly twice then succeeds, an
/// endpoint that vanishes mid-activation, an exclusive-mode rejection).
/// </summary>
public interface IChannelActivationService
{
    /// <summary>Point future activations at a different endpoint id (same physical output).</summary>
    void RebindDevice(string deviceId);

    /// <summary>
    /// Resolve a fresh device and open a live output on it. Never throws: failures come back
    /// as <see cref="ChannelActivationResult.DeviceMissing"/> or as status text.
    /// </summary>
    ChannelActivationResult Activate(
        string channelName,
        double gain,
        bool muted,
        EqSettings eq,
        Action<OutputChannel, Exception?> playbackStopped);
}
