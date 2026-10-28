using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;

namespace ChiliMusic;

public partial class MainWindow
{
    private Point _libraryPressPoint;
    private LibraryItem? _pressedLibrary;
    private bool _openingLibrary;

    private static bool IsCardControl(DependencyObject? source)
    {
        while (source != null && source is not ListBoxItem)
        {
            if (source is ButtonBase) return true;
            source = source is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(source);
        }
        return false;
    }
    private void LibraryPointerDown(object sender, MouseButtonEventArgs e)
    {
        _pressedLibrary = null;
        if (e.ChangedButton != MouseButton.Left || e.ClickCount > 1 || Keyboard.Modifiers != ModifierKeys.None || IsCardControl(e.OriginalSource as DependencyObject)) return;
        _libraryPressPoint = e.GetPosition(LibraryList);
        _pressedLibrary = Row(e.OriginalSource as DependencyObject)?.DataContext as LibraryItem;
    }
    private void LibraryPointerUp(object sender, MouseButtonEventArgs e)
    {
        var item = _pressedLibrary; _pressedLibrary = null;
        if (item == null || e.ChangedButton != MouseButton.Left || _openingLibrary || Keyboard.Modifiers != ModifierKeys.None || IsCardControl(e.OriginalSource as DependencyObject)) return;
        var delta = e.GetPosition(LibraryList) - _libraryPressPoint;
        if (Math.Abs(delta.X) >= SystemParameters.MinimumHorizontalDragDistance || Math.Abs(delta.Y) >= SystemParameters.MinimumVerticalDragDistance || !ReferenceEquals(Row(e.OriginalSource as DependencyObject)?.DataContext, item)) return;
        _openingLibrary = true; e.Handled = true;
        Vm.Run(async () => { try { await Vm.BrowseAsync(item.Title, item: item); } finally { _openingLibrary = false; } });
    }
}
