using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AudioHQ.App.ViewModels;

namespace AudioHQ.App;

/// <summary>Native titled host used while the app mixer is detached from the main window.</summary>
public sealed class AppMixerWindow : Window
{
    private const double MixerChromeHeight = 52;
    private const double LayoutRoundingAllowance = 2;
    private const int MaximumVisibleRows = 10;
    private const double FallbackRowHeight = 72;
    private readonly Grid _host;
    private readonly FrameworkElement _mixer;
    private readonly ItemsControl _items;
    private readonly AppMixerViewModel _viewModel;
    private double _naturalHeight;
    private bool _isAtNaturalHeight = true;
    private bool _wasUserResized;
    private bool _allowClose;

    public AppMixerWindow(
        Window owner,
        FrameworkElement mixer,
        ItemsControl items,
        AppMixerViewModel viewModel)
    {
        Owner = owner;
        _mixer = mixer;
        _items = items;
        _viewModel = viewModel;
        Title = "AudioHQ - Mixer";
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Background = ThemeResources.Brush("Brush.Surface");
        double mixerWidth = (double)owner.FindResource("AppMixerPanelWidth");
        SizeToContent = SizeToContent.WidthAndHeight;
        MaxHeight = AvailableHeight();
        UpdateMaximumHeight();

        var thumbTemplate = new ControlTemplate(typeof(Thumb));
        var thumbBackground = new FrameworkElementFactory(typeof(Border));
        thumbBackground.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        thumbTemplate.VisualTree = thumbBackground;

        var grip = new Thumb
        {
            Width = mixerWidth / 2,
            Height = 20,
            Cursor = Cursors.SizeNS,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Brushes.Transparent,
            Template = thumbTemplate,
        };
        grip.DragDelta += ResizeGrip_DragDelta;

        var gripLine = new Border
        {
            Width = mixerWidth / 2,
            Height = 2,
            CornerRadius = new CornerRadius(1),
            Background = ThemeResources.Brush("Brush.TextMuted"),
            Opacity = 0.65,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 5),
            IsHitTestVisible = false,
        };

        _host = new Grid();
        _host.Children.Add(mixer);
        _host.Children.Add(grip);
        _host.Children.Add(gripLine);
        Content = _host;
        Loaded += (_, _) =>
        {
            RecalculateNaturalHeight();
            WindowPlacement.LeftOfOwner(this);
        };
        owner.LocationChanged += OwnerMoved;
        owner.SizeChanged += OwnerMoved;
        viewModel.Apps.CollectionChanged += Apps_CollectionChanged;
    }

    private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_naturalHeight <= 0) RecalculateNaturalHeight();
        SizeToContent = SizeToContent.Manual;
        Height = Math.Clamp(ActualHeight + e.VerticalChange, 180, _naturalHeight);
        _isAtNaturalHeight = Math.Abs(Height - _naturalHeight) < 0.5;
        _wasUserResized = !_isAtNaturalHeight;
    }

    private void Apps_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(RecalculateNaturalHeight, DispatcherPriority.Loaded);
    }

    private void RecalculateNaturalHeight()
    {
        double previousNaturalHeight = _naturalHeight;
        _items.UpdateLayout();
        double measuredRowHeight = 0;
        double rowWidth = _items.ActualWidth > 0 ? _items.ActualWidth : Width;
        for (int i = 0; i < _items.Items.Count; i++)
        {
            if (_items.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement row) continue;
            row.Measure(new Size(rowWidth, double.PositiveInfinity));
            double renderedHeight = row.ActualHeight > 0 ? row.ActualHeight : row.DesiredSize.Height;
            measuredRowHeight = Math.Max(measuredRowHeight, renderedHeight);
        }

        double rowHeight = measuredRowHeight > 0 ? measuredRowHeight : FallbackRowHeight;
        int visibleRows = Math.Min(_viewModel.Apps.Count, MaximumVisibleRows);
        double rowsHeight = rowHeight * visibleRows;
        double nativeChromeHeight = Math.Max(0, ActualHeight - _host.ActualHeight);
        double contentHeight = MixerChromeHeight + rowsHeight + LayoutRoundingAllowance + nativeChromeHeight;
        _naturalHeight = Math.Min(Math.Max(100, contentHeight), AvailableHeight());
        MaxHeight = _naturalHeight;

        if (!_wasUserResized || SizeToContent != SizeToContent.Manual || _isAtNaturalHeight || previousNaturalHeight <= 0)
        {
            Width = ActualWidth;
            SizeToContent = SizeToContent.Manual;
            Height = _naturalHeight;
            _isAtNaturalHeight = true;
        }
        else if (Height > _naturalHeight)
        {
            Height = _naturalHeight;
            _isAtNaturalHeight = true;
        }
    }

    private void OwnerMoved(object? sender, EventArgs e)
    {
        UpdateMaximumHeight();
        WindowPlacement.LeftOfOwner(this);
    }

    private void UpdateMaximumHeight()
    {
        if (_naturalHeight > 0) MaxHeight = Math.Min(_naturalHeight, AvailableHeight());
    }

    private double AvailableHeight() => Math.Max(180, SystemParameters.WorkArea.Bottom - Owner.Top);

    public void ReleaseMixer()
    {
        _host.Children.Remove(_mixer);
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            _viewModel.IsExpanded = false;
            return;
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (Owner is { } owner)
        {
            owner.LocationChanged -= OwnerMoved;
            owner.SizeChanged -= OwnerMoved;
        }
        _viewModel.Apps.CollectionChanged -= Apps_CollectionChanged;
        base.OnClosed(e);
    }
}
