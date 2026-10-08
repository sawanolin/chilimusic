using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace ChiliMusic;
public partial class PlayerBar : UserControl
{
    public static readonly DependencyProperty ShowUtilitiesProperty = DependencyProperty.Register(nameof(ShowUtilities), typeof(bool), typeof(PlayerBar), new PropertyMetadata(true));
    public bool ShowUtilities { get => (bool)GetValue(ShowUtilitiesProperty); set => SetValue(ShowUtilitiesProperty, value); }
    public PlayerBar() => InitializeComponent();
    private void VolumeWheel(object sender, MouseWheelEventArgs e) { ((PlayerViewModel)DataContext).Volume += Math.Sign(e.Delta) * 5; e.Handled = true; }
}
