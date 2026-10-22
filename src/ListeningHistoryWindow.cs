using System.Windows;
using System.Windows.Controls;

namespace ChiliMusic;

internal sealed class ListeningHistoryWindow : DialogShell
{
    internal ListeningHistoryWindow(PlayerViewModel vm) : base("本机听歌历史", 760, 540)
    {
        var list = new ListBox { ItemsSource = vm.ListeningHistory.Where(h => h.Track.IsLocal || vm.Api.CacheScopeAllowed(h.Scope)).ToList(), DisplayMemberPath = "Label" };
        Body.RowDefinitions.Add(new() { Height = GridLength.Auto }); Body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); Body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Body.Children.Add(UiFactory.Text("按播放时间倒序排列；恢复当时列表后从选中歌曲开始播放。", 11)); Grid.SetRow(list, 1); Body.Children.Add(list);
        void Play() { if (list.SelectedItem is ListeningEntry entry) vm.Run(() => vm.PlayHistoryAsync(entry)); }
        list.MouseDoubleClick += (_, _) => Play(); var footer = UiFactory.Buttons(UiFactory.Button("恢复当时列表并播放", Play, true), UiFactory.Button("清除历史", () => { vm.ClearListeningHistory(); list.ItemsSource = null; })); Grid.SetRow(footer, 2); Body.Children.Add(footer);
    }
}
