using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace AudioHQ.App;

// Translucent ghost that trails the cursor during an app-row drag; renders above all
// other content on the window's adorner layer so it is never clipped by the panel.
internal sealed class DragAdorner : Adorner
{
    private readonly UIElement _child;
    private readonly double _offsetX;
    private readonly double _offsetY;
    private double _x;
    private double _y;

    internal DragAdorner(UIElement root, UIElement child, double offsetX, double offsetY)
        : base(root)
    {
        _child = child;
        _offsetX = offsetX;
        _offsetY = offsetY;
        IsHitTestVisible = false;
        Opacity = 0.72;
        AddVisualChild(_child);
        AddLogicalChild(_child);
    }

    internal void UpdatePosition(double x, double y)
    {
        _x = x - _offsetX;
        _y = y - _offsetY;
        AdornerLayer.GetAdornerLayer(AdornedElement)?.Update(AdornedElement);
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _child;

    protected override Size MeasureOverride(Size constraint)
    {
        _child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _child.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _child.Arrange(new Rect(_child.DesiredSize));
        return finalSize;
    }

    public override GeneralTransform GetDesiredTransform(GeneralTransform transform)
    {
        var group = new GeneralTransformGroup();
        group.Children.Add(base.GetDesiredTransform(transform));
        group.Children.Add(new TranslateTransform(_x, _y));
        return group;
    }
}
