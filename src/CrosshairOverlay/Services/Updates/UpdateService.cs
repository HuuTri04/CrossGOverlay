using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CrosshairOverlay.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.Updates;

/// <inheritdoc cref="IUpdateService"/>
public sealed class UpdateService : IUpdateService, IDisposable
{
    /// <summary>
    /// Kho phát hành trên GitHub, dạng "chu-so-huu/ten-kho".
    /// </summary>
    /// <remarks>
    /// Chưa trỏ tới kho thật. Khi chưa đổi, <see cref="CheckAsync"/> sẽ thất bại và trả null —
    /// đúng hành vi mong muốn: ứng dụng vẫn khởi động bình thường.
    /// </remarks>
    private const string RepositoryPath = "your-account/CrosshairOverlay";

    /// <summary>Đủ để một mạng bình thường trả lời, và đủ ngắn để mạng hỏng không làm chờ lâu.</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    private readonly ILogger<UpdateService> _logger;
    private readonly HttpClient _http;
    private bool _disposed;

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;

        _http = new HttpClient { Timeout = DownloadTimeout };

        // GitHub API từ chối request không có User-Agent.
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(Core.AppInfo.DisplayName, CurrentVersion.ToString()));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public Version CurrentVersion { get; } =
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CheckTimeout);

            var url = $"https://api.github.com/repos/{RepositoryPath}/releases/latest";
            using var response = await _http.GetAsync(url, timeout.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Kiểm tra cập nhật trả về {Status}.", (int)response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token)
                .ConfigureAwait(false);

            return Parse(document.RootElement);
        }
        catch (Exception ex)
        {
            // Mất mạng, DNS hỏng, timeout, JSON lạ — tất cả đều chỉ có nghĩa "không kiểm tra
            // được lúc này". Không bao giờ để chuyện này nổi lên thành lỗi cho người dùng.
            _logger.LogDebug(ex, "Không kiểm tra được cập nhật.");
            return null;
        }
    }

    private UpdateInfo? Parse(JsonElement release)
    {
        if (!release.TryGetProperty("tag_name", out var tag)) return null;

        var latest = ParseVersion(tag.GetString());
        if (latest is null || latest <= CurrentVersion) return null;

        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;

            if (name is null || url is null) continue;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            var page = release.TryGetProperty("html_url", out var h) ? h.GetString() : url;
            return new UpdateInfo(latest, url, page ?? url);
        }

        return null;
    }

    /// <summary>Thẻ phát hành thường có tiền tố "v", vd "v1.4.0".</summary>
    private static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var text = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out var version) ? version : null;
    }

    public async Task<bool> DownloadAndApplyAsync(
        UpdateInfo update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var currentExe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentExe))
        {
            _logger.LogWarning("Không xác định được đường dẫn file thực thi, bỏ qua cập nhật.");
            return false;
        }

        var stagedExe = Path.Combine(
            Path.GetTempPath(), $"CrosshairOverlay-{update.Version}.exe");

        await DownloadAsync(update.DownloadUrl, stagedExe, cancellationToken).ConfigureAwait(false);

        var script = WriteSwapScript(currentExe, stagedExe);

        global::System.Diagnostics.Process.Start(new global::System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{script}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
        });

        _logger.LogInformation("Đã bàn giao cho script cập nhật: {Script}", script);
        return true;
    }

    private async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        // Tải ra file tạm rồi mới đổi tên: tải dở dang không được để lại một file .exe cụt mà
        // script sau đó lại đem chép đè lên bản đang chạy tốt.
        var partial = destination + ".part";

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = File.Create(partial))
        {
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        File.Move(partial, destination, overwrite: true);
    }

    /// <summary>
    /// Sinh script thay thế file. Ứng dụng không thể tự ghi đè lên file .exe đang chạy của
    /// chính nó, nên phải nhờ một tiến trình khác làm việc đó sau khi ta đã thoát.
    /// </summary>
    private static string WriteSwapScript(string currentExe, string stagedExe)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"CrosshairOverlay-update-{Guid.NewGuid():N}.bat");
        var processName = Path.GetFileNameWithoutExtension(currentExe);

        var script = $"""
            @echo off
            rem Chờ ứng dụng thoát hẳn trước khi đụng vào file .exe của nó.
            :wait
            tasklist /fi "imagename eq {processName}.exe" | find /i "{processName}.exe" >nul
            if not errorlevel 1 (
                timeout /t 1 /nobreak >nul
                goto wait
            )

            rem Thử vài lần: bộ quét virus có thể còn giữ file thêm một lúc.
            set /a tries=0
            :swap
            move /y "{stagedExe}" "{currentExe}" >nul 2>&1
            if not errorlevel 1 goto done
            set /a tries+=1
            if %tries% lss 10 (
                timeout /t 1 /nobreak >nul
                goto swap
            )
            goto done

            :done
            start "" "{currentExe}"

            rem Tự xoá: cmd đọc file theo từng dòng nên dòng này chạy được chính nó.
            (goto) 2>nul & del "%~f0"
            """;

        File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
        return scriptPath;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _http.Dispose();
    }
}
