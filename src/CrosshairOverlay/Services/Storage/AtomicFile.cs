using System.IO;
using System.Text.Json;

namespace CrosshairOverlay.Services.Storage;

/// <summary>Writes JSON through a temporary file and keeps the previous valid generation.</summary>
internal static class AtomicFile
{
    // The temporary and backup names are shared by every service instance writing a path.
    // One gate also keeps readers from observing a replacement between primary and backup.
    private static readonly SemaphoreSlim FileLock = new(1, 1);

    public static async Task WriteJsonAtomicAsync<T>(
        string filePath, T data, CancellationToken cancellationToken = default) where T : class
    {
        await FileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await WriteCoreAsync(filePath, data, cancellationToken).ConfigureAwait(false); }
        finally { FileLock.Release(); }
    }

    private static async Task WriteCoreAsync<T>(
        string filePath, T data, CancellationToken cancellationToken) where T : class
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var tempPath = filePath + ".tmp";
        var backupPath = filePath + ".bak";
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write,
                FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, data, AppJson.Options, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(filePath))
            {
                // Preserve a good backup if the primary was already corrupt.
                if (await IsReadableAsync<T>(filePath, cancellationToken).ConfigureAwait(false))
                    File.Replace(tempPath, filePath, backupPath);
                else
                    File.Move(tempPath, filePath, overwrite: true);
            }
            else
            {
                File.Move(tempPath, filePath);
            }
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                global::System.Diagnostics.Trace.WriteLine($"[AtomicFile] Could not remove temporary file: {ex}");
            }
        }
    }

    public static async Task<T?> LoadJsonAsync<T>(
        string filePath, CancellationToken cancellationToken = default) where T : class
    {
        await FileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var primary = await TryReadAsync<T>(filePath, cancellationToken).ConfigureAwait(false);
            if (primary is not null) return primary;
            return await TryReadAsync<T>(filePath + ".bak", cancellationToken).ConfigureAwait(false);
        }
        finally { FileLock.Release(); }
    }

    public static async Task<bool> DeleteJsonAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await FileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                var backupPath = filePath + ".bak";
                var existed = File.Exists(filePath) || File.Exists(backupPath);
                if (File.Exists(filePath)) File.Delete(filePath);
                if (File.Exists(backupPath)) File.Delete(backupPath);
                return existed;
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { FileLock.Release(); }
    }

    public static void RunExclusive(Action action)
    {
        FileLock.Wait();
        try { action(); }
        finally { FileLock.Release(); }
    }

    private static async Task<bool> IsReadableAsync<T>(string path, CancellationToken cancellationToken) where T : class =>
        await TryReadAsync<T>(path, cancellationToken).ConfigureAwait(false) is not null;

    private static async Task<T?> TryReadAsync<T>(string path, CancellationToken cancellationToken) where T : class
    {
        try
        {
            if (!File.Exists(path)) return default;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<T>(stream, AppJson.Options, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            global::System.Diagnostics.Trace.WriteLine($"[AtomicFile] Could not read JSON; trying backup: {ex}");
            return default;
        }
    }

    public static void CleanupTemporaries(string directory)
    {
        FileLock.Wait();
        try
        {
            if (!Directory.Exists(directory)) return;
            foreach (var file in Directory.EnumerateFiles(directory, "*.tmp"))
            {
                try { File.Delete(file); }
                catch (IOException ex) { global::System.Diagnostics.Trace.WriteLine($"[AtomicFile] Temporary cleanup: {ex}"); }
                catch (UnauthorizedAccessException ex) { global::System.Diagnostics.Trace.WriteLine($"[AtomicFile] Temporary cleanup: {ex}"); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            global::System.Diagnostics.Trace.WriteLine($"[AtomicFile] Could not enumerate temporary files: {ex}");
        }
        finally { FileLock.Release(); }
    }
}
