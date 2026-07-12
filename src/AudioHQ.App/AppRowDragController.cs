using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using AudioHQ.App.ViewModels;

namespace AudioHQ.App;

/// <summary>Handles drag-reorder interactions for app-mixer rows, including the ghost adorner.</summary>
public sealed class AppRowDragController
{
    private readonly FrameworkElement _panel;
    private readonly Func<AppMixerViewModel?> _appMixer;
    private DragAdorner? _dragAdorner;
    private AdornerLayer? _adornerLayer;
    private Border? _dragSourceRow;
    private Border? _dropTarget;

    public AppRowDragController(FrameworkElement panel, Func<AppMixerViewModel?> appMixer)
    {
        _panel = panel;
        _appMixer = appMixer;
    }

    public void Start(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not AppSessionViewModel app) return;

        var row = FindTaggedRow(fe);
        if (row is not null)
            BeginGhost(row, e);

        GiveFeedbackEventHandler giveFeedback = (_, gev) =>
        {
            gev.UseDefaultCursors = false;
            Mouse.SetCursor(Cursors.SizeAll);
            if (_dragAdorner != null && GetCursorPos(out POINT pt))
            {
                var rel = _panel.PointFromScreen(new Point(pt.X, pt.Y));
                _dragAdorner.UpdatePosition(rel.X, rel.Y);
            }
            gev.Handled = true;
        };
        DragDrop.AddGiveFeedbackHandler(fe, giveFeedback);

        e.Handled = true;
        try
        {
            DragDrop.DoDragDrop(fe, app, DragDropEffects.Move);
        }
        finally
        {
            DragDrop.RemoveGiveFeedbackHandler(fe, giveFeedback);
            EndDrag();
        }
    }

    public void DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(AppSessionViewModel)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;

        if (sender is Border target && !ReferenceEquals(target, _dropTarget)
            && !ReferenceEquals(target, _dragSourceRow))
        {
            ClearDropHighlight();
            _dropTarget = target;
            target.BorderThickness = new Thickness(0, 2, 0, 0);
            target.BorderBrush = (Brush)Application.Current.Resources["AccentBlue"];
        }

        e.Handled = true;
    }

    public void Drop(object sender, DragEventArgs e)
    {
        ClearDropHighlight();
        var appMixer = _appMixer();
        if (appMixer is null) return;
        if (e.Data.GetData(typeof(AppSessionViewModel)) is not AppSessionViewModel source) return;
        if (sender is not FrameworkElement fe || fe.DataContext is not AppSessionViewModel target) return;
        appMixer.MoveApp(source, target);
        e.Handled = true;
    }

    private void BeginGhost(Border row, MouseButtonEventArgs e)
    {
        _adornerLayer = AdornerLayer.GetAdornerLayer(_panel);
        if (_adornerLayer is not null)
        {
            var dpi = VisualTreeHelper.GetDpi(row);
            int w = Math.Max(1, (int)Math.Round(row.ActualWidth * dpi.DpiScaleX));
            int h = Math.Max(1, (int)Math.Round(row.ActualHeight * dpi.DpiScaleY));
            var bmp = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bmp.Render(row);
            bmp.Freeze();

            var ghost = new Rectangle
            {
                Width = row.ActualWidth,
                Height = row.ActualHeight,
                RadiusX = 8,
                RadiusY = 8,
                Fill = new ImageBrush(bmp) { Stretch = Stretch.Fill },
                IsHitTestVisible = false,
                Effect = new DropShadowEffect
                {
                    BlurRadius = 12,
                    ShadowDepth = 4,
                    Opacity = 0.55,
                    Direction = 270,
                    Color = Colors.Black,
                },
            };
            var clickPos = e.GetPosition(row);
            _dragAdorner = new DragAdorner(_panel, ghost, clickPos.X, clickPos.Y);
            _adornerLayer.Add(_dragAdorner);
            var initPos = e.GetPosition(_panel);
            _dragAdorner.UpdatePosition(initPos.X, initPos.Y);
        }

        _dragSourceRow = row;
        row.Opacity = 0.35;
    }

    private void EndDrag()
    {
        ClearDropHighlight();
        if (_dragAdorner is not null && _adornerLayer is not null)
        {
            _adornerLayer.Remove(_dragAdorner);
            _dragAdorner = null;
            _adornerLayer = null;
        }
        if (_dragSourceRow is not null)
        {
            _dragSourceRow.Opacity = 1.0;
            _dragSourceRow = null;
        }
    }

    private void ClearDropHighlight()
    {
        if (_dropTarget is null) return;
        _dropTarget.BorderThickness = new Thickness(0);
        _dropTarget.BorderBrush = Brushes.Transparent;
        _dropTarget = null;
    }

    private static Border? FindTaggedRow(DependencyObject start)
    {
        DependencyObject? curr = start;
        while (curr is not null)
        {
            curr = VisualTreeHelper.GetParent(curr);
            if (curr is Border { Tag: "AppRow" } row) return row;
        }
        return null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);
}
