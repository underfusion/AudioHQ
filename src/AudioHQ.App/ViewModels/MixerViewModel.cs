using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Input;
using AudioHQ.App;
using AudioHQ.Core;
using NAudio.CoreAudioApi;

namespace AudioHQ.App.ViewModels;

public sealed record LatencyPreset(string Name, int Ms)
{
    public override string ToString() => Name;
}

/// <summary>
/// Root view model: source selection (= master, the source device's Windows volume),
/// latency, and a user-curated, persisted list of named output channels.
/// </summary>
public sealed class MixerViewModel : ViewModelBase, IDisposable
{
    private readonly MirrorEngine _engine = new();
    private readonly MixerSettings _settings;
    private MMDevice? _selectedSource;
    private string _engineStatus = "";
    private LatencyPreset _selectedLatency;
    private bool _loaded;
    private bool _dirty;
    private bool _isEditingMaster;

    public ObservableCollection<MMDevice> Sources { get; } = new();
    public ObservableCollection<ChannelViewModel> Channels { get; } = new();

    public ICommand RemoveChannelCommand { get; }

    public LatencyPreset[] LatencyPresets { get; } =
    {
        new("Ultra (15 ms)", 15),
        new("Low (30 ms)", 30),
        new("Balanced (60 ms)", 60),
        new("Safe (100 ms)", 100),
    };

    public MixerViewModel()
    {
        _settings = MixerSettings.Load();
        RemoveChannelCommand = new RelayCommand(p => RemoveChannel(p as ChannelViewModel));

        foreach (var device in AudioDevices.GetActiveRenderDevices())
            Sources.Add(device);

        _selectedLatency = LatencyPresets.FirstOrDefault(p => p.Ms == _settings.LatencyMs)
                           ?? LatencyPresets[1];

        var source = ResolveSource();
        _selectedSource = source;

        BuildChannels(source?.ID);
        if (source is not null)
        {
            RestartEngine(source);
            // Restore the ON channels from last session now that capture is running.
            foreach (var channel in Channels.Where(c => c.PendingActive && c.IsAvailable))
                channel.IsActive = true;
        }

        _loaded = true;

        // Keep the HKCU Run entry in step with the saved preference (refreshes the
        // exe path if the app was moved since it was first enabled).
        StartupRegistration.Set(_settings.RunWithWindows);
    }

    private MMDevice? ResolveSource()
    {
        if (_settings.SourceDeviceId is not null)
        {
            var saved = Sources.FirstOrDefault(d => d.ID == _settings.SourceDeviceId);
            if (saved is not null) return saved;
        }

        var defaultDevice = AudioDevices.GetDefaultRender();
        return Sources.FirstOrDefault(d => d.ID == defaultDevice.ID) ?? Sources.FirstOrDefault();
    }

    private void BuildChannels(string? sourceId)
    {
        Channels.Clear();

        var defs = _settings.Channels;
        if (defs.Count == 0)
        {
            // First run: seed from every device that is not the source, so nothing is lost.
            defs = Sources.Where(d => d.ID != sourceId)
                          .Select(d => new ChannelDefinition { DeviceId = d.ID, Name = d.FriendlyName, Gain = 1.0 })
                          .ToList();
        }

        foreach (var def in defs)
        {
            var device = Sources.FirstOrDefault(d => d.ID == def.DeviceId);
            var vm = new ChannelViewModel(_engine, def.DeviceId, device, def.Name, def.Gain,
                () => _selectedLatency.Ms, MarkDirty)
            {
                IsSource = def.DeviceId == sourceId,
                PendingActive = def.Active,
            };
            Channels.Add(vm);
        }
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
            Save();
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
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SourceName));
            OnPropertyChanged(nameof(MasterName));
            OnPropertyChanged(nameof(MasterVolume));
            OnPropertyChanged(nameof(MasterMuted));
            OnPropertyChanged(nameof(MasterPercent));
        }
    }

    public string SourceName => _selectedSource?.FriendlyName ?? "(no source)";

    /// <summary>Editable master label; empty override falls back to the source device name.</summary>
    public string MasterName
    {
        get => string.IsNullOrWhiteSpace(_settings.MasterName) ? SourceName : _settings.MasterName!;
        set
        {
            var trimmed = (value ?? "").Trim();
            _settings.MasterName = trimmed.Length == 0 ? null : trimmed;
            OnPropertyChanged();
            Save();
        }
    }

    /// <summary>Inline rename mode for the master strip.</summary>
    public bool IsEditingMaster
    {
        get => _isEditingMaster;
        set { _isEditingMaster = value; OnPropertyChanged(); }
    }

    private void RestartEngine(MMDevice source)
    {
        var wasActive = Channels.Where(c => c.IsActive).ToList();
        foreach (var channel in Channels) channel.IsActive = false;

        bool started = false;
        try
        {
            _engine.Start(source);
            EngineStatus = "";
            started = true;
        }
        catch (COMException ex)
        {
            Log.Write($"Engine.Start FAILED for '{source.FriendlyName}': {ex}");
            _engine.Stop();
            EngineStatus = (uint)ex.HResult == 0x8889000A
                ? $"Cannot capture '{source.FriendlyName}': locked in exclusive mode by another app. Pick a different source."
                : $"Cannot capture '{source.FriendlyName}': error 0x{ex.HResult:X8}. Pick a different source.";
        }

        foreach (var channel in Channels)
            channel.IsSource = channel.DeviceId == source.ID;

        if (started)
            foreach (var channel in wasActive.Where(c => c.IsAvailable))
                channel.IsActive = true;
    }

    /// <summary>Devices not yet used as a channel and not the current source - candidates to add.</summary>
    public IReadOnlyList<MMDevice> GetAvailableDevices()
    {
        var used = Channels.Select(c => c.DeviceId).ToHashSet();
        return Sources.Where(d => d.ID != _selectedSource?.ID && !used.Contains(d.ID)).ToList();
    }

    public void AddChannel(MMDevice device)
    {
        if (device is null || Channels.Any(c => c.DeviceId == device.ID)) return;
        var vm = new ChannelViewModel(_engine, device.ID, device, device.FriendlyName, 1.0,
            () => _selectedLatency.Ms, MarkDirty)
        {
            IsSource = device.ID == _selectedSource?.ID,
        };
        Channels.Add(vm);
        Log.Write($"Channel added: '{device.FriendlyName}'");
        Save();
    }

    private void RemoveChannel(ChannelViewModel? channel)
    {
        if (channel is null) return;
        channel.IsActive = false;
        Channels.Remove(channel);
        Log.Write($"Channel removed: '{channel.Name}'");
        Save();
    }

    public void MoveChannel(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || newIndex < 0 || oldIndex >= Channels.Count || newIndex >= Channels.Count
            || oldIndex == newIndex) return;
        Channels.Move(oldIndex, newIndex);
        Log.Write($"Channel reordered: {oldIndex} -> {newIndex}");
        Save();
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

    // --- Tray / startup options (persisted to settings.json) -------------------

    public bool CloseToTray
    {
        get => _settings.CloseToTray;
        set { if (_settings.CloseToTray == value) return; _settings.CloseToTray = value; OnPropertyChanged(); Save(); }
    }

    public bool MinimizeToTray
    {
        get => _settings.MinimizeToTray;
        set { if (_settings.MinimizeToTray == value) return; _settings.MinimizeToTray = value; OnPropertyChanged(); Save(); }
    }

    public bool RunWithWindows
    {
        get => _settings.RunWithWindows;
        set
        {
            if (_settings.RunWithWindows == value) return;
            _settings.RunWithWindows = value;
            StartupRegistration.Set(value);
            OnPropertyChanged();
            Save();
        }
    }

    private void MarkDirty() => _dirty = true;

    private void Save()
    {
        if (!_loaded) return;
        _settings.SourceDeviceId = _selectedSource?.ID;
        _settings.LatencyMs = _selectedLatency.Ms;
        _settings.Channels = Channels.Select(c => c.ToDefinition()).ToList();
        _settings.Save();
        _dirty = false;
    }

    public void Dispose()
    {
        if (_dirty) Save();
        _engine.Dispose();
    }
}
