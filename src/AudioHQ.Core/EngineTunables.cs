namespace AudioHQ.Core;

/// <summary>
/// The engine's tuning numbers, in one place with the reasoning behind them.
///
/// These were scattered as literals across OutputChannel, LoopbackMirror and
/// AdaptiveResampler, where the trade-off each one encodes was invisible - and where two
/// related values (the resync ceiling and the backlog target) could drift into each other
/// without anyone noticing. They are `const`, so the compiler still inlines them and the
/// EQ/resampler hot loops gain no indirection.
///
/// Changing anything here changes how the mirror sounds. Read the comment first.
/// </summary>
internal static class EngineTunables
{
    /// <summary>
    /// How much audio a channel's buffer can hold at all. Only a ceiling for pathological
    /// stalls - the adaptive resampler normally keeps the real backlog near
    /// <see cref="TargetBacklogMarginMs"/>, orders of magnitude below this.
    /// </summary>
    public const int BufferSeconds = 2;

    /// <summary>
    /// Jitter headroom above the render buffer before a hard resync. Capture delivers ~10ms
    /// chunks, so anything past latency + this is pure added delay rather than jitter.
    /// Used as: maxBacklog = latencyMs + this.
    /// </summary>
    public const double ResyncMarginMs = 25.0;

    /// <summary>
    /// The trough (minimum backlog) the resampler steers toward, as a margin above latency.
    /// It must stay ABOVE the WASAPI pull granularity (~latencyMs per render callback) plus
    /// delivery jitter, or the buffer underruns at the low point and feeds silence (crackle).
    /// Kept well under <see cref="ResyncMarginMs"/> so normal drift never trips a resync.
    /// Raise back toward 10 if a jittery source starts to crackle.
    /// </summary>
    public const double TargetBacklogMarginMs = 5.0;

    /// <summary>Largest ratio deviation the resampler ever applies (0.5% ~= 8 cents of pitch - inaudible).</summary>
    public const double ResamplerMaxCorrection = 0.005;

    /// <summary>Proportional gain: correction per second of trough error (0.01 s error -> max).</summary>
    public const double ResamplerGain = 0.5;

    /// <summary>EMA factor applied to the trough each control tick (~5/s) -> ~1 s settling.</summary>
    public const double ResamplerTroughSmoothing = 0.3;

    /// <summary>Hard cap on the resampler fill loop so a pathological ratio can never spin forever.</summary>
    public const int ResamplerMaxIterations = 8;
}
