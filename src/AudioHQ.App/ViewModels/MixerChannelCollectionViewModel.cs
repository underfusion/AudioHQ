using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using AudioHQ.Core;
using NAudio.CoreAudioApi;

namespace AudioHQ.App.ViewModels;

/// <summary>Owns the curated output channel list, ordering and tray-focus selection.</summary>
public sealed class MixerChannelCollectionViewModel : ViewModelBase
{
    private readonly MirrorEngine _engine;
    private readonly ObservableCollection<MMDevice> _sources;
    private readonly Func<string?> _sourceId;
    private readonly Func<int> _latencyMs;
    private readonly Action _markDirty;
    private readonly EqPresetStore _eqPresets;
    private readonly Action _save;
    private ChannelViewModel? _focusedChannel;

    public MixerChannelCollectionViewModel(
        MirrorEngine engine,
        ObservableCollection<MMDevice> sources,
        Func<string?> sourceId,
        Func<int> latencyMs,
        Action markDirty,
        EqPresetStore eqPresets,
        Action save)
    {
        _engine = engine;
        _sources = sources;
        _sourceId = sourceId;
        _latencyMs = latencyMs;
        _markDirty = markDirty;
        _eqPresets = eqPresets;
        _save = save;

        RemoveChannelCommand = new RelayCommand(p => RemoveChannel(p as ChannelViewModel));
        FocusChannelCommand = new RelayCommand(p => ToggleFocusChannel(p as ChannelViewModel));
    }

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();
    public ICommand RemoveChannelCommand { get; }
    public ICommand FocusChannelCommand { get; }
    public ChannelViewModel? FocusedChannel => _focusedChannel;

    public void Build(IReadOnlyList<ChannelDefinition> persistedDefinitions, string? sourceId)
    {
        Channels.Clear();
        _focusedChannel = null;

        var definitions = persistedDefinitions;
        if (definitions.Count == 0)
        {
            // First run: seed from every device that is not the source, so nothing is lost.
            definitions = _sources.Where(d => d.ID != sourceId)
                                  .Select(d => new ChannelDefinition
                                  {
                                       DeviceId = d.ID,
                                      DeviceName = d.FriendlyName,
                                      Name = d.FriendlyName,
                                      Gain = 1.0,
                                  })
                                  .ToList();
        }

        var endpoints = _sources.Select(d => new AudioEndpointIdentity(d.ID, d.FriendlyName)).ToList();
        var reservedIds = new HashSet<string>();
        ChannelViewModel? focusedChannel = null;
        foreach (var definition in definitions)
        {
            string recoveryName = string.IsNullOrWhiteSpace(definition.DeviceName)
                ? definition.Name
                : definition.DeviceName;
            string? resolvedId = AudioEndpointIdentityResolver.Resolve(
                definition.DeviceId, recoveryName, endpoints, reservedIds);
            if (resolvedId is null
                && reservedIds.Contains(definition.DeviceId)
                && endpoints.Any(endpoint => endpoint.Id == definition.DeviceId))
            {
                // The stale strip earlier in the saved order already adopted this endpoint.
                // This later exact-id entry can only be the replacement the user added manually.
                Log.Write($"Channel '{definition.Name}': removed duplicate endpoint '{definition.DeviceId}' during recovery");
                if (definition.Focused)
                    focusedChannel = Channels.FirstOrDefault(channel => channel.DeviceId == definition.DeviceId);
                _markDirty();
                continue;
            }

            var liveDevice = resolvedId is null ? null : _sources.First(d => d.ID == resolvedId);
            string deviceId = liveDevice?.ID ?? definition.DeviceId;
            string deviceName = liveDevice?.FriendlyName ?? recoveryName;
            bool present = liveDevice is not null;
            if (present) reservedIds.Add(deviceId);

            if (deviceId != definition.DeviceId)
            {
                Log.Write($"Channel '{definition.Name}': restored renamed endpoint '{definition.DeviceId}' -> '{deviceId}' ({deviceName})");
                _markDirty();
            }
            else if (liveDevice is not null && definition.DeviceName != deviceName)
            {
                // Migrate settings written before DeviceName was persisted.
                _markDirty();
            }

            var vm = new ChannelViewModel(_engine, deviceId, present, definition.Name, definition.Gain,
                _latencyMs, _markDirty, _eqPresets, definition.Eq, deviceName, definition.Muted)
            {
                IsSource = deviceId == sourceId,
                WantsActive = definition.Active,
            };
            Channels.Add(vm);
            if (definition.Focused) focusedChannel = vm;
        }

        if (focusedChannel is not null)
        {
            focusedChannel.IsFocused = true;
            _focusedChannel = focusedChannel;
            OnPropertyChanged(nameof(FocusedChannel));
        }
    }

    /// <summary>Devices not yet used as a channel and not the current source - candidates to add.</summary>
    public IReadOnlyList<MMDevice> GetAvailableDevices()
    {
        var used = Channels.Select(c => c.DeviceId).ToHashSet();
        var sourceId = _sourceId();
        return _sources.Where(d => d.ID != sourceId && !used.Contains(d.ID)).ToList();
    }

    public void AddChannel(MMDevice device)
    {
        if (device is null || Channels.Any(c => c.DeviceId == device.ID)) return;
        var vm = new ChannelViewModel(_engine, device.ID, present: true, device.FriendlyName, 1.0,
            _latencyMs, _markDirty, _eqPresets, deviceName: device.FriendlyName)
        {
            IsSource = device.ID == _sourceId(),
        };
        Channels.Add(vm);
        Log.Write($"Channel added: '{device.FriendlyName}'");
        _save();
    }

    public void MoveChannel(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || newIndex < 0 || oldIndex >= Channels.Count || newIndex >= Channels.Count
            || oldIndex == newIndex) return;
        Channels.Move(oldIndex, newIndex);
        Log.Write($"Channel reordered: {oldIndex} -> {newIndex}");
        _save();
    }

    private void RemoveChannel(ChannelViewModel? channel)
    {
        if (channel is null) return;
        if (_focusedChannel == channel) ToggleFocusChannel(channel);
        channel.IsActive = false;
        Channels.Remove(channel);
        Log.Write($"Channel removed: '{channel.Name}'");
        _save();
    }

    private void ToggleFocusChannel(ChannelViewModel? channel)
    {
        if (channel is null) return;
        var previous = _focusedChannel;
        if (previous == channel)
        {
            previous.IsFocused = false;
            _focusedChannel = null;
        }
        else
        {
            if (previous is not null) previous.IsFocused = false;
            channel.IsFocused = true;
            _focusedChannel = channel;
        }

        OnPropertyChanged(nameof(FocusedChannel));
        _save();
    }
}
