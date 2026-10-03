using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Text;
namespace ChiliMusic;
internal static class NativeFonts
{
    [DllImport("gdi32.dll")] private static extern IntPtr AddFontMemResourceEx(IntPtr font, uint size, IntPtr reserved, ref uint count);
    [DllImport("gdi32.dll")] private static extern bool RemoveFontMemResourceEx(IntPtr handle);
    private static readonly PrivateFontCollection Collection = new(); private static IntPtr _data, _registration; private static System.Drawing.FontFamily? _family;
    public static void Initialize() { try { using var stream = Application.GetResourceStream(new Uri("pack://application:,,,/Resources/Fonts/MiSans-Regular.ttf")).Stream; using var memory = new MemoryStream(); stream.CopyTo(memory); var bytes = memory.ToArray(); _data = Marshal.AllocHGlobal(bytes.Length); Marshal.Copy(bytes, 0, _data, bytes.Length); uint count = 0; _registration = AddFontMemResourceEx(_data, (uint)bytes.Length, IntPtr.Zero, ref count); Collection.AddMemoryFont(_data, bytes.Length); _family = Collection.Families.FirstOrDefault(); } catch (IOException) { } _family ??= new System.Drawing.FontFamily("Microsoft YaHei UI"); }
    public static Font Create(float pixels) => new(_family!, pixels, FontStyle.Regular, GraphicsUnit.Pixel);
    public static void Dispose() { _family?.Dispose(); _family = null; Collection.Dispose(); if (_registration != IntPtr.Zero) RemoveFontMemResourceEx(_registration); if (_data != IntPtr.Zero) Marshal.FreeHGlobal(_data); _data = _registration = IntPtr.Zero; }
}
