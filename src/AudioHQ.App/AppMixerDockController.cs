using System;
using System.Windows;
using System.Windows.Controls;
using AudioHQ.App.ViewModels;

namespace AudioHQ.App;

/// <summary>
/// Owns where the per-app mixer lives: docked in the main window's left region, or detached
/// into its own floating window. It owns the floating window's whole lifetime and keeps the
/// dock button's icon/tooltip and the persisted AppMixerDetached flag in step with it.
///
/// The same <c>AppPanel</c> element is re-parented rather than rebuilt, so the rows, their
/// bindings and their scroll position survive a detach/attach.
///
/// Matches the existing controller pattern (AppPanelAnimator, MainWindowTraySync,
/// AppRowDragController): the window keeps the XAML, this keeps the orchestration.
/// </summary>
internal sealed class AppMixerDockController
{
    private readonly MainWindow _owner;
    private readonly Func<AppMixerViewModel?> _mixer;
    private readonly Func<MixerViewModel?> _viewModel;
    private AppMixerWindow? _window;

    public AppMixerDockController(MainWindow owner, Func<AppMixerViewModel?> mixer, Func<MixerViewModel?> viewModel)
    {
        _owner = owner;
        _mixer = mixer;
        _viewModel = viewModel;
    }

    /// <summary>True while the mixer lives in its own window rather than the main window.</summary>
    public bool IsDetached => _window is not null;

    /// <summary>Dock button: detach when docked, re-attach when floating.</summary>
    public void Toggle()
    {
        if (_window is null) Detach();
        else Attach();
    }

    /// <summary>
    /// Chevron while detached: hide/show the floating window instead of sliding the panel.
    /// Returns false when there is no floating window, so the caller animates the panel.
    /// </summary>
    public bool ToggleFloatingVisibility()
    {
        var mixer = _mixer();
        if (_window is null || mixer is null) return false;

        if (_window.IsVisible)
        {
            _window.Hide();
            mixer.IsExpanded = false;
        }
        else
        {
            // Expand first: showing the window triggers a refresh that reads this.
            mixer.IsExpanded = true;
            _window.Show();
            _window.Activate();
        }
        return true;
    }

    /// <summary>Puts the docked panel straight into its open or closed state, no animation.</summary>
    public void RestoreAttached(bool expanded)
    {
        var mixer = _mixer();
        if (mixer is null) return;

        ClearPanelAnimations();
        _owner.AppPanel.Width = expanded ? PanelWidth : 0;
        _owner.AppPanel.Margin = expanded ? OpenMargin : ClosedMargin;
        mixer.IsExpanded = expanded;
    }

    public void Detach()
    {
        var mixer = _mixer();
        var viewModel = _viewModel();
        if (mixer is null || viewModel is null || _window is not null) return;

        // Drop any in-flight slide animation first: an animation outlives a plain Width/Margin
        // assignment and would fight the floating window's layout.
        ClearPanelAnimations();
        _owner.AppMixerRegion.Children.Remove(_owner.AppPanel);
        _owner.AppPanel.Width = PanelWidth;
        _owner.AppPanel.Margin = new Thickness(0);
        mixer.IsExpanded = true;

        _window = new AppMixerWindow(_owner, _owner.AppPanel, _owner.AppMixerItems, mixer);
        _window.IsVisibleChanged += (_, _) => mixer.IsExpanded = _window?.IsVisible == true;

        viewModel.Settings.AppMixerDetached = true;
        viewModel.SaveSettings();
        SetDockButton(detached: true);
        _window.Show();
    }

    public void Attach()
    {
        var viewModel = _viewModel();
        if (_window is null || viewModel is null) return;

        _window.ReleaseMixer();
        _window.ClosePermanently();
        _window = null;

        _owner.AppMixerRegion.Children.Add(_owner.AppPanel);
        DockPanel.SetDock(_owner.AppPanel, Dock.Left);
        _owner.AppPanel.Width = PanelWidth;
        _owner.AppPanel.Margin = OpenMargin;

        var mixer = _mixer();
        if (mixer is not null) mixer.IsExpanded = true;

        viewModel.Settings.AppMixerDetached = false;
        viewModel.SaveSettings();
        SetDockButton(detached: false);
    }

    /// <summary>Main window is closing for good: release the panel and close the floating host.</summary>
    public void CloseFloating()
    {
        if (_window is null) return;
        _window.ReleaseMixer();
        _window.ClosePermanently();
        _window = null;
    }

    private void SetDockButton(bool detached)
    {
        _owner.AppMixerDockButton.ToolTip = detached ? "Attach mixer" : "Detach mixer";
        _owner.AppMixerDetachIcon.Visibility = detached ? Visibility.Collapsed : Visibility.Visible;
        _owner.AppMixerAttachIcon.Visibility = detached ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ClearPanelAnimations()
    {
        _owner.AppPanel.BeginAnimation(FrameworkElement.WidthProperty, null);
        _owner.AppPanel.BeginAnimation(FrameworkElement.MarginProperty, null);
    }

    private double PanelWidth => (double)_owner.FindResource("AppMixerPanelWidth");
    private Thickness OpenMargin => (Thickness)_owner.FindResource("AppMixerPanelOpenMargin");
    private Thickness ClosedMargin => (Thickness)_owner.FindResource("AppMixerPanelClosedMargin");
}
