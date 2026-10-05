using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ChiliMusic;

internal static class WindowsTlsCertificates
{
    internal static string? BundlePath()
    {
        try
        {
            var directory = Path.Combine(Store.Root, "cache"); Directory.CreateDirectory(directory); string path = Path.Combine(directory, "windows-trusted-roots.pem");
            if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < TimeSpan.FromHours(6)) return path;
            var bundle = new StringBuilder(); var seen = new HashSet<string>();
            foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
            {
                using var store = new X509Store(StoreName.Root, location); store.Open(OpenFlags.ReadOnly);
                foreach (var certificate in store.Certificates) using (certificate) if (seen.Add(certificate.Thumbprint)) bundle.AppendLine(certificate.ExportCertificatePem());
            }
            if (seen.Count == 0) return null; string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(temp, bundle.ToString(), new UTF8Encoding(false)); File.Move(temp, path, true); } finally { if (File.Exists(temp)) File.Delete(temp); }
            return path;
        }
        catch (Exception error) when (error is CryptographicException or IOException or UnauthorizedAccessException) { return null; }
    }
}
