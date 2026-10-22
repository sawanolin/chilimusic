using System.Windows.Media;

namespace ChiliMusic;

public partial class MainWindow
{
    private CancellationTokenSource? _detailCoverRequest;
    private LibraryItem? _detailCoverItem;
    private string? _detailCoverKey;
    private void CancelDetailCover()
    {
        _detailCoverRequest?.Cancel(); _detailCoverRequest?.Dispose(); _detailCoverRequest = null;
        _detailCoverKey = null; _detailCoverItem = null;
    }
    private void RefreshDetailCover()
    {
        if (!IsVisible) { CancelDetailCover(); HeroCover.Source = null; return; }
        var item = Vm.BrowseItem;
        var localPaths = new[] { item?.LocalCoverPath }.Concat(Vm.Results.Select(t => t.EmbeddedCoverPath)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).Take(2).Where(File.Exists).ToArray();
        var coverIds = new[] { item?.CoverArt }.Concat(Vm.Results.Where(t => !t.IsLocal).Select(t => t.CoverArt)).OfType<string>().Where(id => id.Length > 0).Distinct().Take(2).ToArray();
        string key = item == null ? "" : string.Join("\0", new[] { item.Kind, item.Id }.Concat(localPaths.Select(p => p + File.GetLastWriteTimeUtc(p).Ticks)).Concat(coverIds.Select(id => Vm.Api.ScopeForId(id) + id)));
        if (ReferenceEquals(item, _detailCoverItem) && key == _detailCoverKey) return;
        CancelDetailCover(); _detailCoverItem = item; _detailCoverKey = key;
        HeroCover.Source = item?.Cover;
        if (item == null) return;
        var request = _detailCoverRequest = new(); var token = request.Token;
        Vm.Run(async () =>
        {
            ImageSource? image = null;
            try
            {
                foreach (string path in localPaths)
                {
                    image = await Task.Run(() => CoverCacheService.Load(path, 480), token);
                    if (image != null) break;
                }
                if (image == null) foreach (string id in coverIds)
                {
                    try
                    {
                        string? path = await Vm.Covers.GetPathAsync(id, Vm.Settings.CoverCacheMb, token);
                        image = await Task.Run(() => CoverCacheService.Load(path, 480), token);
                        if (image != null) break;
                    }
                    catch (ApiException) { }
                }
                if (!token.IsCancellationRequested && ReferenceEquals(item, Vm.BrowseItem) && ReferenceEquals(request, _detailCoverRequest)) HeroCover.Source = image ?? item.Cover;
            }
            catch (OperationCanceledException) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Store.Log("WARNING", "详情封面暂时无法读取"); }
        });
    }
}
