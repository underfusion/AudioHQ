using System;
using System.Windows;
using AudioHQ.App.ViewModels;
using AudioHQ.Core;

namespace AudioHQ.App;

public partial class MainWindow : Window
{
    private MixerViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        Title = $"AudioHQ v{AppVersion.Display}";

        try
        {
            _viewModel = new MixerViewModel();
            DataContext = _viewModel;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to start audio engine:\n{ex.Message}", "AudioHQ",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel?.Dispose();
        base.OnClosed(e);
    }
}
