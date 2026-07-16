using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace AudioHQ.App;

/// <summary>Animates the left app-mixer panel between collapsed and expanded layout tokens.</summary>
public sealed class AppPanelAnimator
{
    private readonly FrameworkElement _resourceOwner;
    private readonly FrameworkElement _panel;

    public AppPanelAnimator(FrameworkElement resourceOwner, FrameworkElement panel)
    {
        _resourceOwner = resourceOwner;
        _panel = panel;
    }

    public void Animate(bool expand)
    {
        var duration = (Duration)_resourceOwner.FindResource("Duration.PanelSlide");
        var ease = (IEasingFunction)_resourceOwner.FindResource("Ease.Standard");

        var panelWidth = (double)_resourceOwner.FindResource("AppMixerPanelWidth");
        _panel.BeginAnimation(FrameworkElement.WidthProperty,
            new DoubleAnimation
            {
                To = expand ? panelWidth : 0,
                Duration = duration,
                EasingFunction = ease,
            });

        var openMargin = (Thickness)_resourceOwner.FindResource("AppMixerPanelOpenMargin");
        var closedMargin = (Thickness)_resourceOwner.FindResource("AppMixerPanelClosedMargin");
        _panel.BeginAnimation(FrameworkElement.MarginProperty,
            new ThicknessAnimation
            {
                To = expand ? openMargin : closedMargin,
                Duration = duration,
                EasingFunction = ease,
            });
    }
}
