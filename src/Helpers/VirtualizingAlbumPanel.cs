using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Size = System.Windows.Size;
namespace ChiliMusic;

public sealed class VirtualizingAlbumPanel : VirtualizingPanel, IScrollInfo
{
    private const double ItemWidth = 132, ItemHeight = 188;
    private int _columns = 1, _first;
    private Size _extent, _viewport;
    private double _offset;
    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? 660 : available.Width;
        double height = double.IsInfinity(available.Height) ? 500 : available.Height;
        _viewport = new(width, height); _columns = Math.Max(1, (int)(width / ItemWidth));
        int count = ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0;
        _extent = new(width, Math.Ceiling(count / (double)_columns) * ItemHeight);
        _offset = Math.Clamp(_offset, 0, Math.Max(0, _extent.Height - height));
        ScrollOwner?.InvalidateScrollInfo();
        _first = Math.Max(0, ((int)(_offset / ItemHeight) - 1) * _columns);
        int last = Math.Min(count - 1, ((int)((_offset + height) / ItemHeight) + 2) * _columns - 1);
        var children = InternalChildren;
        var generator = ItemContainerGenerator;
        if (generator == null || count == 0) { if (children.Count > 0) RemoveInternalChildRange(0, children.Count); return _viewport; }
        for (int i = InternalChildren.Count - 1; i >= 0; i--)
        {
            int index = generator.IndexFromGeneratorPosition(new(i, 0));
            if (index < _first || index > last) { generator.Remove(new(i, 0), 1); RemoveInternalChildRange(i, 1); }
        }
        var position = generator.GeneratorPositionFromIndex(_first); int childIndex = position.Offset == 0 ? position.Index : position.Index + 1;
        using (generator.StartAt(position, GeneratorDirection.Forward, true))
            for (int index = _first; index <= last; index++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out bool fresh);
                if (fresh) { if (childIndex >= InternalChildren.Count) AddInternalChild(child); else InsertInternalChild(childIndex, child); generator.PrepareItemContainer(child); }
                child.Measure(new(ItemWidth, ItemHeight));
            }
        return _viewport;
    }
    protected override Size ArrangeOverride(Size final)
    {
        for (int i = 0; i < InternalChildren.Count; i++)
        {
            int index = ItemContainerGenerator.IndexFromGeneratorPosition(new(i, 0));
            InternalChildren[i].Arrange(new Rect(index % _columns * ItemWidth, index / _columns * ItemHeight - _offset, ItemWidth, ItemHeight));
        }
        return final;
    }
    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args) { base.OnItemsChanged(sender, args); if (InternalChildren.Count > 0) RemoveInternalChildRange(0, InternalChildren.Count); ItemContainerGenerator?.RemoveAll(); InvalidateMeasure(); }
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; } = true;
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => _offset;
    public ScrollViewer? ScrollOwner { get; set; }
    public void SetHorizontalOffset(double offset) { }
    protected override void BringIndexIntoView(int index) { if (index >= 0) SetVerticalOffset(index / _columns * ItemHeight); }
    public void SetVerticalOffset(double offset) { if (double.IsNaN(offset)) return; double value = Math.Clamp(offset, 0, Math.Max(0, _extent.Height - _viewport.Height)); if (Math.Abs(value - _offset) < .1) return; _offset = value; ScrollOwner?.InvalidateScrollInfo(); InvalidateMeasure(); }
    public void LineUp() => SetVerticalOffset(_offset - 32);
    public void LineDown() => SetVerticalOffset(_offset + 32);
    public void PageUp() => SetVerticalOffset(_offset - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset + _viewport.Height);
    public void MouseWheelUp() => SetVerticalOffset(_offset - 96);
    public void MouseWheelDown() => SetVerticalOffset(_offset + 96);
    public void LineLeft() { } public void LineRight() { } public void PageLeft() { } public void PageRight() { } public void MouseWheelLeft() { } public void MouseWheelRight() { }
    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var node = visual as DependencyObject; while (node != null && VisualTreeHelper.GetParent(node) != this) node = VisualTreeHelper.GetParent(node);
        if (node is UIElement child) { int index = InternalChildren.IndexOf(child); int item = ItemContainerGenerator.IndexFromGeneratorPosition(new(index, 0)); double top = item / _columns * ItemHeight; if (top < _offset) SetVerticalOffset(top); else if (top + ItemHeight > _offset + _viewport.Height) SetVerticalOffset(top + ItemHeight - _viewport.Height); }
        return rectangle;
    }
}
