using System;
using AudioHQ.Core;
using NAudio.CoreAudioApi;

namespace AudioHQ.App.ViewModels;

/// <summary>Master strip state backed by the selected source device's Windows endpoint volume.</summary>
public sealed class MixerMasterViewModel : ViewModelBase
{
    private readonly MixerSettings _settings;
    private readonly Func<MMDevice?> _source;
    private readonly Func<string> _sourceName;
    private readonly Action _save;
    private bool _isEditing;

    public MixerMasterViewModel(
        MixerSettings settings,
        Func<MMDevice?> source,
        Func<string> sourceName,
        Action save)
    {
        _settings = settings;
        _source = source;
        _sourceName = sourceName;
        _save = save;
    }

    /// <summary>Editable master label; empty override falls back to the source device name.</summary>
    public string Name
    {
        get => string.IsNullOrWhiteSpace(_settings.MasterName) ? _sourceName() : _settings.MasterName!;
        set
        {
            var trimmed = (value ?? "").Trim();
            _settings.MasterName = trimmed.Length == 0 ? null : trimmed;
            OnPropertyChanged();
            _save();
        }
    }

    /// <summary>Inline rename mode for the master strip.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        set { if (_isEditing == value) return; _isEditing = value; OnPropertyChanged(); }
    }

    public double Volume
    {
        get
        {
            try { return _source()?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0; }
            catch (Exception ex) { Log.Write($"MasterVolume read failed: {ex.Message}"); return 0; }
        }
        set
        {
            var source = _source();
            if (source is null) return;
            try { source.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(value, 0, 1); }
            catch (Exception ex) { Log.Write($"MasterVolume set failed: {ex.Message}"); }
            OnPropertyChanged();
            OnPropertyChanged(nameof(Percent));
        }
    }

    public bool Muted
    {
        get
        {
            try { return _source()?.AudioEndpointVolume.Mute ?? false; }
            catch (Exception ex) { Log.Write($"MasterMuted read failed: {ex.Message}"); return false; }
        }
        set
        {
            var source = _source();
            if (source is null) return;
            try { source.AudioEndpointVolume.Mute = value; }
            catch (Exception ex) { Log.Write($"MasterMuted set failed: {ex.Message}"); }
            OnPropertyChanged();
        }
    }

    public string Percent => $"{Math.Round(Volume * 100)}%";

    public void NotifySourceChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Volume));
        OnPropertyChanged(nameof(Muted));
        OnPropertyChanged(nameof(Percent));
    }
}
