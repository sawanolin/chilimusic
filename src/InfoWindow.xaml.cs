using System.Windows;
namespace ChiliMusic;
public partial class InfoWindow : Window
{
    public InfoWindow(PlayerViewModel vm) { InitializeComponent(); WindowAppearance.Attach(this); DataContext = vm; }
    private void CloseClick(object sender, RoutedEventArgs e) => Close();
}
