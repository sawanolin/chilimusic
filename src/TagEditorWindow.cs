using System.Windows;
using System.Windows.Controls;

namespace ChiliMusic;

internal sealed class TagEditorWindow : DialogShell
{
    internal TagEditorWindow(PlayerViewModel vm, Track[] tracks) : base("编辑本地音乐标签", 690, 650)
    {
        Body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); Body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var content = new StackPanel(); var fields = new Dictionary<string, (CheckBox Apply, TextBox Text)>();
        content.Children.Add(UiFactory.Text($"已选 {tracks.Length} 首。仅修改勾选的字段；先预览，再写入文件。", 12));
        foreach (var (name, value) in new[] { ("标题", tracks.Length == 1 ? tracks[0].Title : ""), ("歌手", tracks[0].Artist), ("专辑", tracks[0].Album), ("专辑艺术家", tracks[0].AlbumArtist), ("年份", tracks[0].Year.ToString()), ("流派", tracks[0].Genre), ("曲目号", tracks[0].TrackNumber.ToString()), ("碟片号", tracks[0].DiscNumber.ToString()) })
        {
            var apply = UiFactory.Check(name, false); var input = UiFactory.Input(value); fields[name] = (apply, input); var row = new Grid { Margin = new Thickness(0, 2, 0, 2) }; row.ColumnDefinitions.Add(new() { Width = new GridLength(112) }); row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) }); apply.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(apply); Grid.SetColumn(input, 1); row.Children.Add(input); content.Children.Add(row);
        }
        var status = UiFactory.Text("", 11); string? cover = null; content.Children.Add(UiFactory.Button("选择封面…", () => { var picker = new Microsoft.Win32.OpenFileDialog { Filter = "图片|*.jpg;*.jpeg;*.png", Title = "替换内嵌封面" }; if (picker.ShowDialog(this) == true) { cover = picker.FileName; status.Text = "封面：" + Path.GetFileName(cover); } }));
        var scroll = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Body.Children.Add(scroll);
        var footer = new StackPanel(); footer.Children.Add(status); string? confirmed = null;
        string Preview() => string.Join("\n", fields.Where(p => p.Value.Apply.IsChecked == true).Select(p => p.Key + " → " + p.Value.Text.Text)) + (cover == null ? "" : "\n封面 → " + cover);
        var save = UiFactory.Button("写入标签", () => vm.Run(async () =>
        {
            string preview = Preview(); if (preview.Trim().Length == 0) { status.Text = "请先勾选要修改的字段。"; return; }
            if (confirmed != preview) { status.Text = "修改内容已变化，请先预览。"; return; }
            var values = fields.Where(p => p.Value.Apply.IsChecked == true).ToDictionary(p => p.Key, p => p.Value.Text.Text.Trim());
            foreach (string key in new[] { "年份", "曲目号", "碟片号" }) if (values.TryGetValue(key, out string? value) && !uint.TryParse(value, out _)) { status.Text = key + "应为非负整数。"; return; }
            int completed = 0; var errors = new List<string>();
            foreach (var track in tracks)
            {
                try { await Task.Run(() => Write(track.LocalPath!, values, cover)); completed++; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or TagLib.CorruptFileException or TagLib.UnsupportedFormatException) { errors.Add(track.Title); }
            }
            await vm.LocalLibrary.ImportAsync(tracks.Select(t => t.LocalPath!)); status.Text = $"已保存 {completed} 首" + (errors.Count == 0 ? "" : "；未完成：" + string.Join("、", errors)); confirmed = null;
        }), true);
        var actions = UiFactory.Toolbar(UiFactory.Button("预览修改", () => { confirmed = Preview(); new TextReportWindow("标签修改预览", string.Join("\n", tracks.Select(t => t.LocalPath)) + "\n\n" + confirmed) { Owner = this }.ShowDialog(); }), save); actions.HorizontalAlignment = HorizontalAlignment.Right; footer.Children.Add(actions); Grid.SetRow(footer, 1); Body.Children.Add(footer);
        Loaded += (_, _) => MainWindow.NameAccessibleControls(this);
    }
    internal static void Write(string path, IReadOnlyDictionary<string, string> values, string? cover)
    {
        using var file = TagLib.File.Create(path); var tag = file.Tag;
        foreach (var (key, value) in values) switch (key)
        {
            case "标题": tag.Title = value; break; case "歌手": tag.Performers = value.Split(" / ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); break; case "专辑": tag.Album = value; break;
            case "专辑艺术家": tag.AlbumArtists = value.Split(" / ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); break; case "年份": tag.Year = uint.Parse(value); break; case "流派": tag.Genres = [value]; break; case "曲目号": tag.Track = uint.Parse(value); break; case "碟片号": tag.Disc = uint.Parse(value); break;
        }
        if (cover != null) { if (new FileInfo(cover).Length > 10 * 1024 * 1024) throw new IOException("封面超过 10 MB。"); tag.Pictures = [new TagLib.Picture(cover) { Type = TagLib.PictureType.FrontCover }]; } file.Save();
    }
}
