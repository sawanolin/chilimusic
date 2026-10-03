using System.Drawing;
using System.Windows.Forms;
namespace ChiliMusic;
internal sealed class PlayerMenuRenderer : ToolStripProfessionalRenderer
{
    internal static System.Drawing.Color Surface => Theme.IsDark ? System.Drawing.Color.FromArgb(34, 40, 50) : System.Drawing.Color.White;
    internal static System.Drawing.Color Text => Theme.IsDark ? System.Drawing.Color.FromArgb(237, 241, 247) : System.Drawing.Color.FromArgb(24, 35, 50);
    private static System.Drawing.Color Hover => Theme.IsDark ? System.Drawing.Color.FromArgb(48, 60, 77) : System.Drawing.Color.FromArgb(231, 236, 243);
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) => e.Graphics.Clear(Surface);
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) { if (e.Item.Selected && e.Item.Enabled) using (var brush = new SolidBrush(Hover)) e.Graphics.FillRectangle(brush, new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2)); }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) { e.TextColor = e.Item.Enabled ? Text : Theme.IsDark ? System.Drawing.Color.FromArgb(169, 180, 196) : System.Drawing.Color.FromArgb(98, 112, 131); using var font = NativeFonts.Create(12 * (e.ToolStrip?.DeviceDpi ?? 96) / 96f); TextRenderer.DrawText(e.Graphics, e.Text, font, e.TextRectangle, e.TextColor, e.TextFormat); }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { using var pen = new Pen(Theme.IsDark ? System.Drawing.Color.FromArgb(53, 64, 78) : System.Drawing.Color.FromArgb(223, 228, 234)); e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1); }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) { using var pen = new Pen(Theme.IsDark ? System.Drawing.Color.FromArgb(53, 64, 78) : System.Drawing.Color.FromArgb(223, 228, 234)); e.Graphics.DrawLine(pen, 12, e.Item.Height / 2, e.Item.Width - 12, e.Item.Height / 2); }
}

