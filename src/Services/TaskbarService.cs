using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DColor = System.Drawing.Color;
namespace ChiliMusic;

public sealed class TaskbarService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr p, IntPtr after, string cls, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int height, uint flags);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] private struct ScreenPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(ScreenPoint point);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    private readonly PlayerViewModel _vm; private readonly App _app; private readonly DispatcherTimer _timer;
    private TaskbarControl? _control; private IntPtr _taskbar; private System.Drawing.Rectangle _bounds; private bool _disposed, _renderPending;
    public bool Embedded => _control != null && !_control.IsDisposed && GetParent(_control.Handle) == _taskbar;
    public TaskbarService(PlayerViewModel vm, App app) { _vm = vm; _app = app; _vm.PropertyChanged += Changed; _timer = new() { Interval = TimeSpan.FromSeconds(2) }; _timer.Tick += (_, _) => Layout(); Layout(); _timer.Start(); }
    private void Changed(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == "Cover") _control?.UpdateCover(); if (e.PropertyName is "Title" or "Artist" or "Cover" or "PlayGlyph" or "Position" or "Duration" or "ModeText") { if (_renderPending) return; _renderPending = true; Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { _renderPending = false; if (!_disposed) _control?.RenderLayer(); }); } }
    private void Layout()
    {
        if (_disposed) return; var shell = FindWindow("Shell_TrayWnd", null); if (shell == IntPtr.Zero) return; bool created = false;
        if (shell != _taskbar || _control == null || _control.IsDisposed || !_control.IsHandleCreated) { _control?.Dispose(); _taskbar = shell; _control = new(shell, _vm, _app); _ = _control.Handle; created = true; Store.Log("INFO", Embedded ? "已嵌入 Shell_TrayWnd 原生任务栏子窗口" : "任务栏嵌入未成功"); }
        if (!GetClientRect(shell, out var client) || !GetWindowRect(shell, out var screen)) return;
        double scale = GetDpiForWindow(shell) / 96.0; if (scale <= 0) scale = 1; int w = (int)(316 * scale), h = Math.Max(28, Math.Min((int)(40 * scale), client.Bottom - 2));
        var tray = FindWindowEx(shell, IntPtr.Zero, "TrayNotifyWnd", null); int right = client.Right - (int)(220 * scale); if (tray != IntPtr.Zero && GetWindowRect(tray, out var nr)) right = nr.Left - screen.Left - (int)(6 * scale);
        var bounds = new System.Drawing.Rectangle(Math.Max(0, right - w), Math.Max(0, (client.Bottom - h) / 2), w, h);
        bool changed = created || bounds != _bounds; _bounds = bounds; if (!changed) return; SetWindowPos(_control.Handle, IntPtr.Zero, bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010 | 0x0040); _control.RenderLayer();
    }
    public object Diagnostics() => new { Embedded, Visible = _control != null && IsWindowVisible(_control.Handle), Handle = _control?.Handle.ToInt64(), Parent = _control == null ? 0 : GetParent(_control.Handle).ToInt64(), Taskbar = _taskbar.ToInt64(), Width = _bounds.Width, Height = _bounds.Height, X = _bounds.X, Y = _bounds.Y };
    public void Capture(string path) { if (_control == null) return; using var bitmap = new System.Drawing.Bitmap(_bounds.Width, _bounds.Height); _control.DrawToBitmap(bitmap, new System.Drawing.Rectangle(0, 0, _bounds.Width, _bounds.Height)); bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
    public void CaptureDesktop(string path) { if (!GetWindowRect(_taskbar, out var rect)) return; int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top; if (width <= 0 || height <= 0) return; using var image = new System.Drawing.Bitmap(width, height); using (var graphics = System.Drawing.Graphics.FromImage(image)) graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new System.Drawing.Size(width, height)); image.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
    public void Refresh() => _control?.RenderLayer();
    public object HitDiagnostics() { GetWindowRect(_taskbar, out var rect); double scale = _bounds.Width / 316.0; var checks = new List<object>(); foreach (int x in new[] { 186, 212, 218, 245, 251, 276, 282, 313 }) { var point = new ScreenPoint { X = rect.Left + _bounds.X + (int)(x * scale), Y = rect.Top + _bounds.Y + 5 }; checks.Add(new { X = x, Hit = WindowFromPoint(point) == _control?.Handle }); } return checks; }
    public void Dispose() { if (_disposed) return; _disposed = true; _timer.Stop(); _vm.PropertyChanged -= Changed; _control?.Dispose(); }
    private sealed class TaskbarControl : System.Windows.Forms.Control
    {
        private readonly IntPtr _parent; private readonly PlayerViewModel _vm; private readonly App _app; private System.Drawing.Bitmap? _cover; private readonly System.Windows.Forms.ToolTip _tips = new(); private string _lastTip = ""; private int _hover = -1;
        public TaskbarControl(IntPtr parent, PlayerViewModel vm, App app) { _parent = parent; _vm = vm; _app = app; Text = "chilimusic任务栏播放器"; AccessibleName = "chilimusic任务栏播放器"; SetStyle(System.Windows.Forms.ControlStyles.UserPaint | System.Windows.Forms.ControlStyles.AllPaintingInWmPaint | System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer, true); Cursor = System.Windows.Forms.Cursors.Hand; UpdateCover(); }
        protected override System.Windows.Forms.CreateParams CreateParams { get { var cp = base.CreateParams; cp.Parent = _parent; cp.Style = unchecked((int)0x50000000); cp.ExStyle = 0x08080080; cp.Width = 316; cp.Height = 40; return cp; } }
        public void UpdateCover() { _cover?.Dispose(); _cover = null; if (_vm.Cover is BitmapSource source) { var enc = new PngBitmapEncoder(); enc.Frames.Add(BitmapFrame.Create(source)); using var stream = new MemoryStream(); enc.Save(stream); stream.Position = 0; using var image = new System.Drawing.Bitmap(stream); _cover = new(image); } }
        protected override void OnResize(EventArgs e) { base.OnResize(e); if (Width > 0 && Height > 0) { using var shape = Rounded(new(0, 0, Width, Height), 6 * Width / 316f); var old = Region; Region = new System.Drawing.Region(shape); old?.Dispose(); } }
        protected override void OnMouseUp(System.Windows.Forms.MouseEventArgs e) { if (e.Button == System.Windows.Forms.MouseButtons.Right) { _app.ShowTrayMenu(); return; } if (e.Button != System.Windows.Forms.MouseButtons.Left) return; double x = e.X / (Width / 316.0); if (x >= 184 && x < 215) _vm.Run(_vm.PreviousAsync); else if (x >= 215 && x < 248) _vm.Run(_vm.ToggleAsync); else if (x >= 248 && x < 279) _vm.Run(() => _vm.NextAsync(false)); else if (x >= 279) _vm.CycleMode(); else _app.ShowMini(); }
        protected override void OnMouseDoubleClick(System.Windows.Forms.MouseEventArgs e) { if (e.Button == System.Windows.Forms.MouseButtons.Left && e.X / (Width / 316.0) < 184) _app.ShowMain(); }
        protected override void OnMouseWheel(System.Windows.Forms.MouseEventArgs e) => _vm.Volume += Math.Sign(e.Delta) * 5;
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeSize { public int Width, Height; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr h, IntPtr dst, IntPtr pos, ref NativeSize size, IntPtr source, ref NativePoint origin, uint key, ref Blend blend, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        public void RenderLayer() { if (IsDisposed || !IsHandleCreated || Width <= 0 || Height <= 0) return; using var bitmap = new System.Drawing.Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb); using (var g = System.Drawing.Graphics.FromImage(bitmap)) Draw(g); var screen = GetDC(IntPtr.Zero); var dc = CreateCompatibleDC(screen); var dib = bitmap.GetHbitmap(DColor.FromArgb(0)); var previous = SelectObject(dc, dib); try { var size = new NativeSize { Width = Width, Height = Height }; var origin = new NativePoint(); var blend = new Blend { Alpha = 255, Format = 1 }; if (!UpdateLayeredWindow(Handle, screen, IntPtr.Zero, ref size, dc, ref origin, 0, ref blend, 2)) Store.Log("WARNING", "任务栏绘制失败 " + Marshal.GetLastWin32Error()); } finally { SelectObject(dc, previous); DeleteObject(dib); DeleteDC(dc); ReleaseDC(IntPtr.Zero, screen); } }
        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e) => Draw(e.Graphics);
        private void Draw(System.Drawing.Graphics g)
        {
            float scale = Width / 316f; g.ScaleTransform(scale, scale); float h = Height / scale; float cy = h / 2; bool dark = TaskbarDark();
            var bg = dark ? DColor.FromArgb(36, 42, 51) : DColor.FromArgb(238, 243, 250); var fg = dark ? DColor.FromArgb(239, 244, 252) : DColor.FromArgb(24, 37, 55); var muted = dark ? DColor.FromArgb(165, 180, 200) : DColor.FromArgb(103, 119, 141);
            g.Clear(DColor.FromArgb(1, 0, 0, 0)); g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit; g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_hover >= 0) { using var hoverBrush = new System.Drawing.SolidBrush(DColor.FromArgb(24, dark ? 255 : 0, dark ? 255 : 0, dark ? 255 : 0)); using var hoverShape = Rounded(new System.Drawing.RectangleF(new[] { 184, 215, 248, 279 }[_hover] + 1, cy - 16, 30, 32), 4); g.FillPath(hoverBrush, hoverShape); }
            var coverRect = new System.Drawing.RectangleF(7, cy - 14, 28, 28);
            if (_cover != null) { g.DrawImage(_cover, coverRect); }
            else
            {
                g.FillEllipse(System.Drawing.Brushes.RoyalBlue, coverRect);
                using var note = new System.Drawing.Font("Segoe UI", 20, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Pixel);
                using var glyph = new GraphicsPath(); using var format = (System.Drawing.StringFormat)System.Drawing.StringFormat.GenericTypographic.Clone();
                glyph.AddString("♪", note.FontFamily, (int)note.Style, note.Size, new System.Drawing.PointF(0, 0), format);
                var bounds = glyph.GetBounds(); using var center = new Matrix(); center.Translate(coverRect.X + coverRect.Width / 2 - (bounds.X + bounds.Width / 2), coverRect.Y + coverRect.Height / 2 - (bounds.Y + bounds.Height / 2)); glyph.Transform(center);
                g.FillPath(System.Drawing.Brushes.White, glyph);
            }
            using var titleFont = NativeFonts.Create(12); using var artistFont = NativeFonts.Create(10); using var textBrush = new System.Drawing.SolidBrush(fg); using var mutedBrush = new System.Drawing.SolidBrush(muted); using var sf = new System.Drawing.StringFormat { Trimming = System.Drawing.StringTrimming.EllipsisCharacter, FormatFlags = System.Drawing.StringFormatFlags.NoWrap };
            g.DrawString(_vm.Current?.Title ?? "chilimusic", titleFont, textBrush, new System.Drawing.RectangleF(43, cy - 15, 134, 18), sf); g.DrawString(_vm.Current?.Artist ?? "选择音乐", artistFont, mutedBrush, new System.Drawing.RectangleF(43, cy + 2, 134, 15), sf);
            using var pen = new System.Drawing.Pen(fg, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round }; Previous(g, pen, 198, cy, false); Previous(g, pen, 263, cy, true);
            using var white = new System.Drawing.Pen(fg, 2.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (_vm.PlayGlyph == "▶") g.FillPolygon(textBrush, new[] { new System.Drawing.PointF(228, cy - 5), new System.Drawing.PointF(228, cy + 5), new System.Drawing.PointF(236, cy) }); else { g.DrawLine(white, 228, cy - 4, 228, cy + 4); g.DrawLine(white, 234, cy - 4, 234, cy + 4); }
            Mode(g, pen, textBrush, artistFont, 296, cy, _vm.Settings.Mode);
            if (_vm.Duration > 0) { using var progress = new System.Drawing.Pen(DColor.FromArgb(92, 157, 248), 1.5f); g.DrawLine(progress, 8, h - 2, 8 + (float)Math.Clamp(_vm.Position / _vm.Duration, 0, 1) * 169, h - 2); }
        }
        private static bool TaskbarDark() { try { using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return (int?)key?.GetValue("SystemUsesLightTheme") == 0; } catch { return Theme.IsDark; } }
        protected override void OnMouseMove(System.Windows.Forms.MouseEventArgs e) { base.OnMouseMove(e); double x = e.X / (Width / 316.0); int hover = x >= 279 ? 3 : x >= 248 ? 2 : x >= 215 ? 1 : x >= 184 ? 0 : -1; if (hover != _hover) { _hover = hover; RenderLayer(); } string tip = x >= 279 ? _vm.ModeText : x >= 248 ? "下一首" : x >= 215 ? (_vm.PlayGlyph == "▶" ? "播放" : "暂停") : x >= 184 ? "上一首" : $"{_vm.Title} · {_vm.Artist}"; if (tip != _lastTip) { _lastTip = tip; _tips.SetToolTip(this, tip); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = -1; RenderLayer(); }
        private static void Mode(System.Drawing.Graphics g, System.Drawing.Pen pen, System.Drawing.Brush brush, System.Drawing.Font font, float x, float y, PlayMode mode)
        {
            if (mode == PlayMode.Sequential) { g.DrawLine(pen, x - 7, y, x + 7, y); g.DrawLines(pen, new System.Drawing.PointF[] { new(x + 3, y - 4), new(x + 7, y), new(x + 3, y + 4) }); }
            else if (mode == PlayMode.Shuffle) { g.DrawLines(pen, new System.Drawing.PointF[] { new(x - 7, y - 5), new(x - 4, y - 5), new(x + 4, y + 5), new(x + 7, y + 5) }); g.DrawLines(pen, new System.Drawing.PointF[] { new(x - 7, y + 5), new(x - 4, y + 5), new(x + 4, y - 5), new(x + 7, y - 5) }); g.DrawLines(pen, new System.Drawing.PointF[] { new(x + 4, y - 8), new(x + 7, y - 5), new(x + 4, y - 2) }); g.DrawLines(pen, new System.Drawing.PointF[] { new(x + 4, y + 2), new(x + 7, y + 5), new(x + 4, y + 8) }); }
            else { g.DrawArc(pen, x - 8, y - 6, 16, 12, 205, 145); g.DrawArc(pen, x - 8, y - 6, 16, 12, 25, 145); g.DrawLines(pen, new System.Drawing.PointF[] { new(x + 4, y - 8), new(x + 8, y - 4), new(x + 8, y - 9) }); g.DrawLines(pen, new System.Drawing.PointF[] { new(x - 4, y + 8), new(x - 8, y + 4), new(x - 8, y + 9) }); if (mode == PlayMode.RepeatOne) g.DrawString("1", font, brush, x - 4, y - 7); }
        }
        private static GraphicsPath Rounded(System.Drawing.RectangleF r, float radius) { var p = new GraphicsPath(); float d = radius * 2; p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p; }
        private static void Previous(System.Drawing.Graphics g, System.Drawing.Pen pen, float x, float y, bool next) { float sign = next ? -1 : 1; g.DrawLine(pen, x - 5 * sign, y - 5, x - 5 * sign, y + 5); g.DrawPolygon(pen, new[] { new System.Drawing.PointF(x - 3 * sign, y), new System.Drawing.PointF(x + 4 * sign, y - 5), new System.Drawing.PointF(x + 4 * sign, y + 5) }); }
        protected override void Dispose(bool disposing) { if (disposing) { _cover?.Dispose(); _tips.Dispose(); } base.Dispose(disposing); }
    }
}

