using System;
using System.Windows;

namespace AudioHQ.App.ViewModels;

/// <summary>
/// Marshals work onto the WPF UI thread. Engine callbacks (source lost, playback stopped)
/// and system events arrive on capture/render/background threads, so view models route
/// through here before touching bound state.
/// </summary>
internal static class UiDispatcher
{
    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread: inline when already on it, otherwise
    /// posted asynchronously.
    ///
    /// Always BeginInvoke, never Invoke: these callbacks come off the audio threads, and a
    /// synchronous hop would block the capture/render thread until the UI thread is free -
    /// glitching audio at best, deadlocking at worst.
    ///
    /// With no <see cref="Application"/> (unit tests, shutdown) it runs inline.
    /// </summary>
    public static void Post(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            dispatcher.BeginInvoke(action);
        else
            action();
    }
}
