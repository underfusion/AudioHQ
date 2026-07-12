using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using AudioHQ.App.ViewModels;

namespace AudioHQ.App;

public partial class MasterStripControl : UserControl
{
    public MasterStripControl()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(
            new Action(PositionUnityLine), System.Windows.Threading.DispatcherPriority.Loaded);
        MasterFader.SizeChanged += (_, _) => PositionUnityLine();
    }

    // The master tops out at 100%, so 100% is the top of the track. Pin the green unity line
    // to the thumb centre at full scale so the thumb rests on the line at 100%.
    private void PositionUnityLine()
    {
        MasterFader.ApplyTemplate();
        MasterFader.UpdateLayout();
        if (MasterFader.Template?.FindName("PART_Track", MasterFader) is not Track track) return;
        if (track.Thumb is not { ActualHeight: > 0 } thumb || MasterUnityLine.Parent is not UIElement parent) return;

        double range = MasterFader.Maximum - MasterFader.Minimum;
        if (range <= 0) return;

        Point thumbCentre = thumb.TranslatePoint(new Point(thumb.ActualWidth / 2, thumb.ActualHeight / 2), parent);
        double travel = Math.Max(0, track.ActualHeight - thumb.ActualHeight);
        double unityY = thumbCentre.Y - (MasterFader.Maximum - MasterFader.Value) / range * travel;

        MasterUnityLine.Margin = new Thickness(0, unityY - MasterUnityLine.Height / 2, 0, 0);
        Canvas.SetTop(MasterTopLabel, unityY - 10 - MasterTopLabel.ActualHeight / 2);
    }

    private void Fader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is Slider slider)
        {
            slider.Value = 1.0;
            e.Handled = true;
        }
    }

    private void MasterName_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && DataContext is MixerViewModel viewModel)
        {
            viewModel.Master.IsEditing = true;
            e.Handled = true;
        }
    }

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox box) RenameTextBoxController.FocusWhenVisible(box);
    }

    private void MasterRenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        e.Handled = RenameTextBoxController.HandleKeyDown(box, e, CloseEditor);
    }

    private void MasterRenameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box) RenameTextBoxController.Commit(box, CloseEditor);
    }

    private void CloseEditor()
    {
        if (DataContext is MixerViewModel viewModel) viewModel.Master.IsEditing = false;
    }
}
