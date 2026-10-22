using System.IO;
using System.Threading.Channels;
using CrosshairOverlay.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Logging;

/// <summary>
/// Best-effort diagnostics for selected window API transitions. This is not an anti-cheat
/// certification or an exhaustive trace of all OS API calls.
/// </summary>
public sealed class AuditLoggerService(IAppPathProvider paths, ILogger<AuditLoggerService> logger) : IDisposable
{
    private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly string _path = Path.Combine(paths.LogsDirectory, "anticheat_audit.log");
    private Task? _writer;
    private bool _closed;
    private readonly object _gate = new();

    public void Record(string operation, string detail)
    {
        lock (_gate)
        {
            if (_closed) return;
            _writer ??= Task.Run(WriteLoopAsync);
            _lines.Writer.TryWrite($"{DateTimeOffset.Now:O} | {operation} | {detail}");
        }
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await using var file = new FileStream(_path, FileMode.Append, FileAccess.Write,
                FileShare.Read, 4096, FileOptions.Asynchronous);
            await using var writer = new StreamWriter(file) { AutoFlush = true };
            await foreach (var line in _lines.Reader.ReadAllAsync()) await writer.WriteLineAsync(line);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not write optional window API audit log.");
        }
    }

    public void Flush()
    {
        Task? writer;
        lock (_gate)
        {
            if (_closed) return;
            _closed = true;
            _lines.Writer.TryComplete();
            writer = _writer;
        }
        writer?.GetAwaiter().GetResult();
    }

    public void Dispose() => Flush();
}
