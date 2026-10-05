using System.Drawing;
using System.Windows.Forms;
namespace ChiliMusic;
internal sealed class PlayerMenuRenderer : ToolStripProfessionalRenderer
{
    private static System.Drawing.Color Color(string resource) { var c = ((System.Windows.Media.SolidColorBrush)Application.Current.Resources[resource]).Color; return System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B); }
    internal static System.Drawing.Color Surface => Color("SurfaceBrush");
    internal static System.Drawing.Color Text => Color("TextBrush");
    private static System.Drawing.Color Hover => Color("SelectedBrush");
    private static System.Drawing.Drawing2D.GraphicsPath Outline(int width, int height)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath(); float d = 12; path.AddArc(.5f, .5f, d, d, 180, 90); path.AddArc(width - d - 1, .5f, d, d, 270, 90); path.AddArc(width - d - 1, height - d - 1, d, d, 0, 90); path.AddArc(.5f, height - d - 1, d, d, 90, 90); path.CloseFigure(); return path;
    }
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(Surface); using var path = Outline(e.ToolStrip.Width, e.ToolStrip.Height);
        if (e.ToolStrip.Region == null || Math.Abs(e.ToolStrip.Region.GetBounds(e.Graphics).Width - (e.ToolStrip.Width - 1)) > 1 || Math.Abs(e.ToolStrip.Region.GetBounds(e.Graphics).Height - (e.ToolStrip.Height - 1)) > 1) { var previous = e.ToolStrip.Region; e.ToolStrip.Region = new Region(path); previous?.Dispose(); }
    }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) { if (e.Item.Selected && e.Item.Enabled) using (var brush = new SolidBrush(Hover)) e.Graphics.FillRectangle(brush, new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2)); }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Text : Color("MutedBrush"); using var font = NativeFonts.Create(12 * (e.ToolStrip?.DeviceDpi ?? 96) / 96f); TextRenderer.DrawText(e.Graphics, e.Text, font, e.TextRectangle, e.TextColor, e.TextFormat); }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { using var pen = new Pen(Color("BorderBrush")); using var path = Outline(e.ToolStrip.Width, e.ToolStrip.Height); e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; e.Graphics.DrawPath(pen, path); }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) { using var pen = new Pen(Color("BorderBrush")); e.Graphics.DrawLine(pen, 12, e.Item.Height / 2, e.Item.Width - 12, e.Item.Height / 2); }
}
