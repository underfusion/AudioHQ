namespace AudioHQ.App.ViewModels;

/// <summary>Notification bubble state for root mixer notices and errors.</summary>
public sealed class MixerStatusViewModel : ViewModelBase
{
    private string _message = "";
    private bool _isError;

    public string Message
    {
        get => _message;
        private set { if (_message == value) return; _message = value; OnPropertyChanged(); }
    }

    public bool IsError
    {
        get => _isError;
        private set { if (_isError == value) return; _isError = value; OnPropertyChanged(); }
    }

    public void Set(string message, bool isError)
    {
        IsError = isError;
        Message = message;
    }

    public void Clear()
    {
        IsError = false;
        Message = "";
    }
}
