namespace AudioHQ.App.ViewModels;

public interface IAppMixerRow
{
    string Key { get; }
    bool IsPinned { get; set; }
}
