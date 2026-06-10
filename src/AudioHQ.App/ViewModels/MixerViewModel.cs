using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using AudioHQ.Core;
using NAudio.CoreAudioApi;

namespace AudioHQ.App.ViewModels;

public sealed record LatencyPreset(string Name, int Ms)
{
    public override string ToString() => Name;
}

/// <summary>Root view model: source selection, master strip (Windows volume on the source device), output strips.</summary>
public sealed class MixerViewModel : ViewModelBase, IDisposable
{
    private readonly MirrorEngine _engine = new();
    private MMDevice? _selectedSource;
    private string _engineStatus = "";
    private LatencyPreset _selectedLatency;

    public ObservableCollection<MMDevice> Sources { get; } = new();
    public ObservableCollection<ChannelViewModel> Channels { get; } = new();

    public LatencyPreset[] LatencyPresets { get; } =
    {
        new("Ultra (15 ms)", 15),
        new("Low (30 ms)", 30),
        new("Balanced (60 ms)", 60),
        new("Safe (100 ms)", 100),
    };

    public MixerViewModel()
    {
        _selectedLatency = LatencyPresets[1];

        foreach (var device in AudioDevices.GetActiveRenderDevices())
            Sources.Add(device);

        var defaultDevice = AudioDevices.GetDefaultRender();
        SelectedSource = Sources.FirstOrDefault(d => d.ID == defaultDevice.ID) ?? Sources.FirstOrDefault();
    }

    public LatencyPreset SelectedLatency
    {
        get => _selectedLatency;
        set
        {
            if (value is null || value == _selectedLatency) return;
            _selectedLatency = value;
            Log.Write($"Latency preset -> {value.Name}");
            OnPropertyChanged();

            // Re-open active channels so the new buffer size takes effect.
            foreach (var channel in Channels.Where(c => c.IsActive))
            {
                channel.IsActive = false;
                channel.IsActive = true;
            }
        }
    }

    public MMDevice? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (value is null || value == _selectedSource) return;
            _selectedSource = value;
            RestartEngine(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(SourceName));
            OnPropertyChanged(nameof(MasterVolume));
            OnPropertyChanged(nameof(MasterMuted));
            OnPropertyChanged(nameof(MasterPercent));
        }
    }

    public string SourceName => _selectedSource?.FriendlyName ?? "(no source)";

    private void RestartEngine(MMDevice source)
    {
        Channels.Clear();

        try
        {
            _engine.Start(source);
            EngineStatus = "";
        }
        catch (COMException ex)
        {
            Log.Write($"Engine.Start FAILED for '{source.FriendlyName}': {ex}");
            _engine.Stop();
            EngineStatus = (uint)ex.HResult == 0x8889000A
                ? $"Cannot capture '{source.FriendlyName}': locked in exclusive mode by another app. Pick a different source."
                : $"Cannot capture '{source.FriendlyName}': error 0x{ex.HResult:X8}. Pick a different source.";
        }

        foreach (var device in Sources.Where(d => d.ID != source.ID))
            Channels.Add(new ChannelViewModel(_engine, device, () => _selectedLatency.Ms));
    }

    public string EngineStatus
    {
        get => _engineStatus;
        private set { _engineStatus = value; OnPropertyChanged(); }
    }

    public double MasterVolume
    {
        get => _selectedSource?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0;
        set
        {
            if (_selectedSource is null) return;
            _selectedSource.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(value, 0, 1);
            OnPropertyChanged();
            OnPropertyChanged(nameof(MasterPercent));
        }
    }

    public bool MasterMuted
    {
        get => _selectedSource?.AudioEndpointVolume.Mute ?? false;
        set
        {
            if (_selectedSource is null) return;
            _selectedSource.AudioEndpointVolume.Mute = value;
            OnPropertyChanged();
        }
    }

    public string MasterPercent => $"{Math.Round(MasterVolume * 100)}%";

    public void Dispose() => _engine.Dispose();
}
