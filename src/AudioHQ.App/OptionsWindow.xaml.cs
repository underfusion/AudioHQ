using System.Windows;

namespace AudioHQ.App;

/// <summary>Settings dialog: source device and latency preset. Binds to the same MixerViewModel.</summary>
public partial class OptionsWindow : Window
{
    public OptionsWindow() => InitializeComponent();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
