using System.Windows;
using System.Windows.Interop;
namespace ChiliMusic;
public partial class MiniPlayerWindow : Window
{
    private App Host => (App)Application.Current;
    public MiniPlayerWindow(PlayerViewModel vm)
    {
        InitializeComponent(); DataContext = vm; AllowDrop = true; DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop) ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None; e.Handled = true; }; Drop += (_, e) => { if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) { var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop); vm.Run(() => vm.OpenLocalFilesAsync(files)); e.Handled = true; } }; Deactivated += (_, _) => Hide(); Closing += (_, e) => { if (!Host.Exiting) { e.Cancel = true; Hide(); } };
    }
    public void Popup()
    {
        Show(); var cursor = System.Windows.Forms.Cursor.Position; var area = System.Windows.Forms.Screen.FromPoint(cursor).WorkingArea;
        var source = PresentationSource.FromVisual(this); var scale = source?.CompositionTarget?.TransformFromDevice ?? System.Windows.Media.Matrix.Identity;
        var bottom = scale.Transform(new Point(area.Right, area.Bottom)); Left = bottom.X - Width - 10; Top = bottom.Y - Height - 10; Activate();
    }
    private void Expand(object sender, RoutedEventArgs e) { Hide(); Host.ShowMain(); }
    private void QueueClick(object sender, RoutedEventArgs e) { Hide(); Host.ShowMain(true); }
    private void SearchClick(object sender, RoutedEventArgs e) { Hide(); Host.ShowMain(false, true); }
    private void SettingsClick(object sender, RoutedEventArgs e) { Hide(); Host.ShowSettings(); }
    private void VolumeWheel(object sender, System.Windows.Input.MouseWheelEventArgs e) { Host.Vm.Volume += Math.Sign(e.Delta) * 5; e.Handled = true; }
    private void RandomClick(object sender, RoutedEventArgs e) => Host.Vm.Run(() => Host.Vm.RandomAsync(false));
}
