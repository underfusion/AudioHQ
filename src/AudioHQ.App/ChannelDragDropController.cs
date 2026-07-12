using System.Windows;
using System.Windows.Input;
using AudioHQ.App.ViewModels;

namespace AudioHQ.App;

public static class ChannelDragDropController
{
    public static void StartDrag(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not ChannelViewModel channel) return;
        DragDrop.DoDragDrop(fe, channel, DragDropEffects.Move);
        e.Handled = true;
    }

    public static void DragOver(DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ChannelViewModel))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    public static void Drop(MixerViewModel viewModel, object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ChannelViewModel)) is not ChannelViewModel source) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not ChannelViewModel target) return;
        if (ReferenceEquals(source, target)) return;

        int from = viewModel.Channels.IndexOf(source);
        int to = viewModel.Channels.IndexOf(target);
        viewModel.MoveChannel(from, to);
        e.Handled = true;
    }
}
