using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace AudioHQ.App;

/// <summary>
/// A small rotary knob (like the bandwidth controls on hardware EQs). Drag up/down to
/// change <see cref="Value"/> between <see cref="Minimum"/> and <see cref="Maximum"/>;
/// double-click resets to <see cref="Default"/>. The pointer sweeps a 270 degree arc.
/// </summary>
public sealed class Knob : FrameworkElement
{
    private const double StartAngle = -135.0; // at Minimum
    private const double EndAngle = 135.0;    // at Maximum
    private const double DragRange = 140.0;   // pixels of vertical drag for the full sweep

    private bool _dragging;
    private double _dragStartY;
    private double _dragStartValue;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(1.0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.AffectsRender,
            null, CoerceValue));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DefaultProperty = DependencyProperty.Register(
        nameof(Default), typeof(double), typeof(Knob),
        new FrameworkPropertyMetadata(1.0));

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Default { get => (double)GetValue(DefaultProperty); set => SetValue(DefaultProperty, value); }

    public Knob()
    {
        Cursor = Cursors.SizeNS;
    }

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var knob = (Knob)d;
        double v = (double)baseValue;
        return Math.Clamp(v, knob.Minimum, knob.Maximum);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Value = Default; // reset to the band default
            e.Handled = true;
            return;
        }
        _dragging = true;
        _dragStartY = e.GetPosition(this).Y;
        _dragStartValue = Value;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        double dy = _dragStartY - e.GetPosition(this).Y; // up = increase
        double span = Maximum - Minimum;
        Value = _dragStartValue + dy / DragRange * span;
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = size / 2 - 1;

        var dial = Brush("ButtonBrush", Color.FromRgb(0x2D, 0x35, 0x45));
        var rim = Brush("DimTextBrush", Color.FromRgb(0x8B, 0x93, 0xA5));
        var pointer = Brush("AccentBlue", Color.FromRgb(0x3B, 0x82, 0xF6));

        // A transparent backing so the whole bounds is hit-testable (drag anywhere on the knob).
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        dc.DrawEllipse(dial, new Pen(rim, 1.5), centre, radius, radius);

        double span = Maximum - Minimum;
        double frac = span <= 0 ? 0.5 : Math.Clamp((Value - Minimum) / span, 0, 1);
        double angle = (StartAngle + frac * (EndAngle - StartAngle)) * Math.PI / 180.0;
        var dir = new Vector(Math.Sin(angle), -Math.Cos(angle)); // 0 = up, clockwise
        var tip = centre + dir * (radius - 2);
        dc.DrawLine(new Pen(pointer, 2.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            centre + dir * (radius * 0.35), tip);
    }

    private Brush Brush(string key, Color fallback)
    {
        if (TryFindResource(key) is Brush b) return b;
        return new SolidColorBrush(fallback);
    }
}
