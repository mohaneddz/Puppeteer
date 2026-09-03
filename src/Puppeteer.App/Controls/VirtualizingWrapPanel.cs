using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Puppeteer.App.Controls;

/// <summary>
/// A wrap panel that only realizes the item containers currently in view. WPF ships a virtualizing
/// stack panel but no virtualizing wrap panel, so a grid of hundreds of project cards would realize
/// every card at once and lag badly. This assumes a uniform cell size (project cards are given a
/// fixed width/height in grid mode) which keeps the layout math — and the scrolling — cheap.
/// </summary>
public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(316d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(182d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ItemWidth { get => (double)GetValue(ItemWidthProperty); set => SetValue(ItemWidthProperty, value); }
    public double ItemHeight { get => (double)GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }

    private Size _cell = new(316, 182);
    private Size _extent;
    private Size _viewport;
    private Point _offset;

    public ScrollViewer? ScrollOwner { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }

    private int Columns => Math.Max(1, (int)(_viewport.Width / _cell.Width));

    protected override Size MeasureOverride(Size availableSize)
    {
        _cell = new Size(ItemWidth, ItemHeight);
        var owner = ItemsControl.GetItemsOwner(this);
        var itemCount = owner?.Items.Count ?? 0;
        _ = InternalChildren; // forces the generator to initialize
        var generator = ItemContainerGenerator;

        UpdateScrollInfo(availableSize, itemCount);
        GetVisibleRange(itemCount, out var firstIndex, out var lastIndex);

        if (itemCount > 0 && lastIndex >= firstIndex)
        {
            var startPos = generator.GeneratorPositionFromIndex(firstIndex);
            var childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;
            using var _ = generator.StartAt(startPos, GeneratorDirection.Forward, true);
            for (var i = firstIndex; i <= lastIndex; i++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out var newlyRealized);
                if (newlyRealized)
                {
                    if (childIndex >= InternalChildren.Count) AddInternalChild(child);
                    else InsertInternalChild(childIndex, child);
                    generator.PrepareItemContainer(child);
                }
                child.Measure(_cell);
            }
        }

        CleanupChildren(firstIndex, lastIndex);
        return new Size(
            double.IsInfinity(availableSize.Width) ? _extent.Width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? _extent.Height : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var columns = Columns;
        foreach (UIElement child in InternalChildren)
        {
            var itemIndex = owner?.ItemContainerGenerator.IndexFromContainer(child) ?? -1;
            if (itemIndex < 0) continue;
            var row = itemIndex / columns;
            var column = itemIndex % columns;
            child.Arrange(new Rect(
                column * _cell.Width - _offset.X,
                row * _cell.Height - _offset.Y,
                _cell.Width, _cell.Height));
        }
        return finalSize;
    }

    private void GetVisibleRange(int itemCount, out int firstIndex, out int lastIndex)
    {
        if (itemCount == 0) { firstIndex = 0; lastIndex = -1; return; }
        var columns = Columns;
        var firstRow = Math.Max(0, (int)(_offset.Y / _cell.Height));
        var rowsInView = (int)Math.Ceiling(_viewport.Height / _cell.Height) + 1;
        firstIndex = firstRow * columns;
        lastIndex = Math.Min(itemCount - 1, (firstRow + rowsInView + 1) * columns - 1);
    }

    private void CleanupChildren(int firstIndex, int lastIndex)
    {
        var generator = ItemContainerGenerator;
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var pos = new GeneratorPosition(i, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(pos);
            if (itemIndex < firstIndex || itemIndex > lastIndex)
            {
                generator.Remove(pos, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
    }

    private void UpdateScrollInfo(Size availableSize, int itemCount)
    {
        var viewport = new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        var columns = Math.Max(1, (int)(viewport.Width / _cell.Width));
        var rows = (int)Math.Ceiling((double)itemCount / columns);
        var extent = new Size(viewport.Width, rows * _cell.Height);

        if (extent != _extent) { _extent = extent; ScrollOwner?.InvalidateScrollInfo(); }
        if (viewport != _viewport) { _viewport = viewport; ScrollOwner?.InvalidateScrollInfo(); }

        var maxY = Math.Max(0, _extent.Height - _viewport.Height);
        if (_offset.Y > maxY) SetVerticalOffset(maxY);
    }

    public Size Extent => _extent;
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;

    public void SetVerticalOffset(double offset)
    {
        offset = Math.Max(0, Math.Min(offset, Math.Max(0, _extent.Height - _viewport.Height)));
        if (Math.Abs(offset - _offset.Y) < 0.001) return;
        _offset.Y = offset;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public void SetHorizontalOffset(double offset) { }

    // Scroll in small pixel steps so the wheel feels like a normal document, not a card-by-card jump.
    private const double LineStep = 20;
    private const double WheelStep = 60;
    public void LineUp() => SetVerticalOffset(_offset.Y - LineStep);
    public void LineDown() => SetVerticalOffset(_offset.Y + LineStep);
    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);
    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - WheelStep);
    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + WheelStep);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var child = InternalChildren.Cast<UIElement>().FirstOrDefault(c => c.IsAncestorOf(visual));
        if (child is null) return rectangle;
        var owner = ItemsControl.GetItemsOwner(this);
        var itemIndex = owner?.ItemContainerGenerator.IndexFromContainer(child) ?? -1;
        if (itemIndex < 0) return rectangle;
        var row = itemIndex / Columns;
        var top = row * _cell.Height;
        var bottom = top + _cell.Height;
        if (top < _offset.Y) SetVerticalOffset(top);
        else if (bottom > _offset.Y + _viewport.Height) SetVerticalOffset(bottom - _viewport.Height);
        return rectangle;
    }
}
