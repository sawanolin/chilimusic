using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace ChiliMusic;

public static class RowNumbering
{
    private static readonly DependencyPropertyKey IndexPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "Index", typeof(int), typeof(RowNumbering), new PropertyMetadata(-1));
    public static readonly DependencyProperty IndexProperty = IndexPropertyKey.DependencyProperty;
    public static int GetIndex(DependencyObject target) => (int)target.GetValue(IndexProperty);
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(RowNumbering), new PropertyMetadata(false, EnabledChanged));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    private static readonly DependencyProperty ControllerProperty = DependencyProperty.RegisterAttached(
        "Controller", typeof(Controller), typeof(RowNumbering));

    private static void EnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ListBox list) return;
        if (list.GetValue(ControllerProperty) is Controller previous) previous.Dispose();
        list.SetValue(ControllerProperty, (bool)args.NewValue ? new Controller(list) : null);
    }

    private sealed class Controller : IDisposable
    {
        private readonly ListBox _list;
        private bool _pending, _disposed;
        public Controller(ListBox list)
        {
            _list = list; list.Loaded += Changed;
            list.ItemContainerGenerator.StatusChanged += Changed;
            list.ItemContainerGenerator.ItemsChanged += ItemsChanged;
            list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Scrolled));
            Schedule();
        }
        private void Changed(object? sender, EventArgs args) => Schedule();
        private void ItemsChanged(object sender, ItemsChangedEventArgs args) => Schedule();
        private void Scrolled(object sender, ScrollChangedEventArgs args) => Schedule();
        private void Schedule()
        {
            if (_pending || _disposed || _list.Dispatcher.HasShutdownStarted) return;
            _pending = true;
            _list.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                _pending = false; if (_disposed) return;
                foreach (var row in Rows(_list))
                    row.SetValue(IndexPropertyKey, _list.ItemContainerGenerator.IndexFromContainer(row));
            });
        }
        private static IEnumerable<ListBoxItem> Rows(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ListBoxItem row) yield return row;
                else if (child is not ListBox) foreach (var nested in Rows(child)) yield return nested;
            }
        }
        public void Dispose()
        {
            _disposed = true; _list.Loaded -= Changed;
            _list.ItemContainerGenerator.StatusChanged -= Changed;
            _list.ItemContainerGenerator.ItemsChanged -= ItemsChanged;
            _list.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(Scrolled));
        }
    }
}
