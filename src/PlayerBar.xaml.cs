using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace ChiliMusic;
public partial class PlayerBar : UserControl
{
    public static readonly DependencyProperty ShowUtilitiesProperty = DependencyProperty.Register(nameof(ShowUtilities), typeof(bool), typeof(PlayerBar), new PropertyMetadata(true));
    public bool ShowUtilities { get => (bool)GetValue(ShowUtilitiesProperty); set => SetValue(ShowUtilitiesProperty, value); }
    private bool _seeking;
    public PlayerBar() => InitializeComponent();
    private void SeekStarted(object sender, DragStartedEventArgs e) { _seeking = true; System.Windows.Data.BindingOperations.ClearBinding(SeekSlider, System.Windows.Controls.Slider.ValueProperty); }
    private void SeekCompleted(object sender, DragCompletedEventArgs e) { ((PlayerViewModel)DataContext).Seek(SeekSlider.Value); _seeking = false; RestoreBinding(); }
    private void SeekReleased(object sender, MouseButtonEventArgs e) { if (!_seeking) { ((PlayerViewModel)DataContext).Seek(SeekSlider.Value); RestoreBinding(); } }
    private void RestoreBinding() => SeekSlider.SetBinding(System.Windows.Controls.Slider.ValueProperty, new Binding("Position") { Mode = System.Windows.Data.BindingMode.OneWay });
    private void VolumeWheel(object sender, MouseWheelEventArgs e) { ((PlayerViewModel)DataContext).Volume += Math.Sign(e.Delta) * 5; e.Handled = true; }
}
