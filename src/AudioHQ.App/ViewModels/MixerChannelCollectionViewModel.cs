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

        var definitions = persistedDefinitions;
        if (definitions.Count == 0)
        {
            // First run: seed from every device that is not the source, so nothing is lost.
            definitions = _sources.Where(d => d.ID != sourceId)
                                  .Select(d => new ChannelDefinition
                                  {
                                      DeviceId = d.ID,
                                      Name = d.FriendlyName,
                                      Gain = 1.0,
                                  })
                                  .ToList();
        }

        foreach (var definition in definitions)
        {
            bool present = _sources.Any(d => d.ID == definition.DeviceId);
            var vm = new ChannelViewModel(_engine, definition.DeviceId, present, definition.Name, definition.Gain,
                _latencyMs, _markDirty, _eqPresets, definition.Eq)
            {
                IsSource = definition.DeviceId == sourceId,
                WantsActive = definition.Active,
            };
            Channels.Add(vm);
        }

        var focusedDefinition = definitions.FirstOrDefault(d => d.Focused);
        if (focusedDefinition is not null)
        {
            var channel = Channels.FirstOrDefault(c => c.DeviceId == focusedDefinition.DeviceId);
            if (channel is not null)
            {
                channel.IsFocused = true;
                _focusedChannel = channel;
                OnPropertyChanged(nameof(FocusedChannel));
            }
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
            _latencyMs, _markDirty, _eqPresets)
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
