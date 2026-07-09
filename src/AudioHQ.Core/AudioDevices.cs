using NAudio.CoreAudioApi;

namespace AudioHQ.Core;

/// <summary>Enumeration of Windows audio render endpoints.</summary>
public static class AudioDevices
{
    public static MMDevice GetDefaultRender()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    public static List<MMDevice> GetActiveRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
    }

    /// <summary>
    /// Fresh MMDevice for an endpoint id, or null when the endpoint is missing or not active.
    /// Always activate audio clients through a fresh instance: a cached MMDevice can be
    /// invalidated by sleep/resume or an unplug/replug while the endpoint itself is fine
    /// (AUDCLNT_E_DEVICE_INVALIDATED on the old COM object). The caller owns the instance
    /// and must dispose it.
    /// </summary>
    public static MMDevice? FindRenderById(string id)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDevice(id);
            if (device.State == DeviceState.Active) return device;
            device.Dispose();
            return null;
        }
        catch (Exception ex)
        {
            Log.Write($"FindRenderById('{id}') failed: {ex.Message}");
            return null;
        }
    }
}
