using System.Windows;
using System.Windows.Controls;

namespace ChiliMusic;

internal sealed class LyricsMatchWindow : DialogShell
{
    private readonly PlayerViewModel _vm;
    private readonly TextBox _query = UiFactory.Input();
    private readonly ListBox _results = new();
    private readonly TextBlock _status = UiFactory.Text("", 11);
    private readonly CancellationTokenSource _life = new();
    private readonly Track? _original;
    private bool _busy;
    internal LyricsMatchWindow(PlayerViewModel vm) : base("匹配歌词", 650, 530)
    {
        _vm = vm; _original = vm.Current; _query.Text = vm.Current == null ? "" : vm.Current.Title + " " + vm.Current.Artist;
        Body.RowDefinitions.Add(new() { Height = GridLength.Auto }); Body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); Body.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel(); var search = UiFactory.Button("搜索", () => _ = SearchAsync(), true); search.Margin = new Thickness(0, 3, 0, 7); _query.Margin = new Thickness(0, 3, 10, 7); DockPanel.SetDock(search, Dock.Right); header.Children.Add(search); header.Children.Add(_query); Body.Children.Add(header);
        _results.ItemTemplate = (DataTemplate)Application.Current.Resources["TrackTemplate"]; _results.Background = System.Windows.Media.Brushes.Transparent;
        var results = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(6), Margin = new Thickness(0, 4, 0, 8), Child = UiFactory.ListWithEmptyState(_results, "选择正确的歌曲版本", "按歌名和歌手搜索，选中结果后使用歌词。") };
        results.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); results.SetResourceReference(Border.BorderBrushProperty, "BorderBrush"); Grid.SetRow(results, 1); Body.Children.Add(results);
        var footer = new DockPanel { LastChildFill = true }; var apply = UiFactory.Button("使用歌词", () => _ = ApplyAsync(), true); DockPanel.SetDock(apply, Dock.Right); footer.Children.Add(apply); footer.Children.Add(_status); Grid.SetRow(footer, 2); Body.Children.Add(footer);
        _query.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { e.Handled = true; _ = SearchAsync(); } }; _results.MouseDoubleClick += (_, _) => _ = ApplyAsync();
        Loaded += async (_, _) => await SearchAsync(); Closed += (_, _) => _life.Cancel();
    }
    private async Task SearchAsync()
    {
        if (_busy) return; _busy = true; _status.Text = "正在搜索…";
        try { var rows = await _vm.Api.Netease.SearchLyricsAsync(_query.Text.Trim(), _life.Token); if (!_life.IsCancellationRequested) { _results.ItemsSource = rows; _status.Text = rows.Count == 0 ? "没有找到歌曲，试试其他关键词" : "选择与当前歌曲一致的版本"; } }
        catch (OperationCanceledException) { } catch (Exception e) { _status.Text = e is ApiException ? e.Message : "搜索失败，请重试。"; } finally { _busy = false; }
    }
    private async Task ApplyAsync()
    {
        if (_busy || _results.SelectedItem is not Track track) return; if (!ReferenceEquals(_original, _vm.Current)) { _status.Text = "歌曲已切换，请重新打开匹配歌词"; return; } _busy = true;
        try { await _vm.ApplyLyricsMatchAsync(track); Close(); } catch (OperationCanceledException) { } catch (Exception e) { _status.Text = e is ApiException ? e.Message : "歌词加载失败，请重试。"; } finally { _busy = false; }
    }
}
