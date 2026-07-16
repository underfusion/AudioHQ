using System;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioHQ.Core;

/// <summary>
/// Enumerates the per-application audio sessions on a render endpoint - the same list
/// the Windows volume mixer shows. A fresh default-device snapshot is taken on each call
/// (refreshes are on-demand, not continuous), so the returned sessions are always live.
/// </summary>
public static class AppSessions
{
    /// <summary>
    /// Sessions currently present on the default render device, newest snapshot. Never
    /// throws; returns an empty list if the device or its session list cannot be read.
    /// </summary>
    public static List<AppSession> ForDefaultRender()
    {
        var result = new List<AppSession>();

        MMDevice device;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (Exception ex)
        {
            Log.Write($"AppSessions: no default render device: {ex.Message}");
            return result;
        }

        try
        {
            var sessions = device.AudioSessionManager.Sessions;
            if (sessions is null) return result;

            for (int i = 0; i < sessions.Count; i++)
            {
                try
                {
                    var control = sessions[i];
                    // Expired sessions are dead apps; the Windows mixer drops them too.
                    if (control.State == AudioSessionState.AudioSessionStateExpired) continue;
                    result.Add(new AppSession(control));
                }
                catch (Exception ex)
                {
                    Log.Write($"AppSessions: session {i} skipped: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Write($"AppSessions.ForDefaultRender failed: {ex.Message}");
        }
        finally
        {
            // This snapshot is retaken every 2s while the app-mixer panel is open, so an
            // undisposed device here leaks a handle per refresh that GC never reclaims.
            // Each returned AppSession keeps its own SimpleAudioVolume, whose COM object
            // carries an independent reference and stays readable AND writable after this
            // dispose - measured, not assumed.
            try
            {
                device.Dispose();
            }
            catch (Exception ex)
            {
                Log.Write($"AppSessions: device dispose failed: {ex.Message}");
            }
        }

        Log.Write($"AppSessions: {result.Count} session(s) on default render device");
        return result;
    }
}
