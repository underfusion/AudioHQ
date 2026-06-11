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
}
