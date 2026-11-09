using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace CrosshairOverlay.Services.System;

/// <summary>One small on-disk cache for the Settings backdrop after blur.</summary>
internal static class AppBackgroundCache
{
    internal static string CachePath(string logsDirectory, string sourcePath, long length,
        DateTime lastWriteUtc, double blurRadius, double opacity)
    {
        var parent = Path.GetDirectoryName(logsDirectory) ?? logsDirectory;
        var input = string.Join('|', "v1", Path.GetFullPath(sourcePath).ToUpperInvariant(),
            length.ToString(CultureInfo.InvariantCulture), lastWriteUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            blurRadius.ToString("R", CultureInfo.InvariantCulture), opacity.ToString("R", CultureInfo.InvariantCulture));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        return Path.Combine(parent, "cache", $"app-background-{hash}.png");
    }

    internal static async Task SaveAsync(string cachePath, BitmapSource bitmap, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(cachePath)!;
            Directory.CreateDirectory(directory);
            var temporary = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    encoder.Save(stream);
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(temporary, cachePath, overwrite: true);

                // Keep the latest few variants when blur/opacity changes repeatedly.
                foreach (var old in Directory.EnumerateFiles(directory, "app-background-*.png")
                             .OrderByDescending(File.GetLastWriteTimeUtc).Skip(8))
                    File.Delete(old);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }, cancellationToken);
    }
}
