using System.Windows;
using System.Windows.Controls;

namespace ChiliMusic;

internal sealed class PlaylistEditWindow : DialogShell
{
    internal string PlaylistName => _name.Text.Trim();
    internal string Description => _description.Text.Trim();
    private readonly TextBox _name, _description;
    internal PlaylistEditWindow(string title, string name, string description = "", bool showDescription = false) : base(title, 480, showDescription ? 380 : 280)
    {
        MinHeight = showDescription ? 330 : 250; Body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); Body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _name = UiFactory.Input(name); _name.ToolTip = "歌单名称"; _description = UiFactory.Input(description); _description.ToolTip = "歌单说明";
        var form = new StackPanel(); form.Children.Add(UiFactory.Text("歌单名称", 11)); form.Children.Add(_name); if (showDescription) { form.Children.Add(UiFactory.Text("歌单说明", 11)); _description.Height = 76; _description.TextWrapping = TextWrapping.Wrap; _description.AcceptsReturn = true; form.Children.Add(_description); } Body.Children.Add(form);
        var footer = UiFactory.Toolbar(UiFactory.Button("取消", Close), UiFactory.Button("保存", () => { if (PlaylistName.Length > 0) { DialogResult = true; Close(); } }, true)); footer.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetRow(footer, 1); Body.Children.Add(footer); Loaded += (_, _) => { _name.Focus(); _name.SelectAll(); };
    }
}
