using System.IO;
using System.IO.Compression;
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
    private const string RepositoryPath = "HuuTri04/CrosshairOverlay";

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

    private UpdateInfo? Parse(JsonElement release) => ParseRelease(release, CurrentVersion);

    /// <summary>
    /// Đọc bản phát hành mới nhất trên GitHub; null nếu không mới hơn hoặc không có gói dùng được.
    /// </summary>
    /// <remarks>
    /// Ứng dụng phát hành dạng THƯ MỤC, nên gói cập nhật phải là file <c>.zip</c> chứa cả thư mục.
    /// Cố tình KHÔNG nhận file <c>.exe</c>: một bản phát hành dạng một file mà chép đè lên
    /// <c>CrossGOverlay.exe</c> của bản thư mục thì chỉ thay được file khởi chạy, còn mã của ứng dụng
    /// (CrossGOverlay.dll) vẫn là bản cũ — cập nhật "thành công" mà không có gì đổi.
    /// Có nhiều .zip thì ưu tiên gói ghi rõ <c>win-x64</c>.
    /// </remarks>
    internal static UpdateInfo? ParseRelease(JsonElement release, Version current)
    {
        if (!release.TryGetProperty("tag_name", out var tag)) return null;

        var latest = ParseVersion(tag.GetString());
        if (latest is null || latest <= current) return null;

        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        string? chosen = null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;

            if (name is null || url is null) continue;
            if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;

            if (name.Contains("win-x64", StringComparison.OrdinalIgnoreCase))
            {
                chosen = url;
                break;
            }

            chosen ??= url;
        }

        if (chosen is null) return null;

        var page = release.TryGetProperty("html_url", out var h) ? h.GetString() : null;
        return new UpdateInfo(latest, chosen, page ?? chosen);
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
        var installDirectory = Path.GetDirectoryName(currentExe);
        if (string.IsNullOrWhiteSpace(currentExe) || string.IsNullOrWhiteSpace(installDirectory))
        {
            _logger.LogWarning("Không xác định được thư mục cài đặt, bỏ qua cập nhật.");
            return false;
        }

        var exeName = Path.GetFileName(currentExe);
        var package = Path.Combine(Path.GetTempPath(), $"CrosshairOverlay-{update.Version}.zip");
        var staging = Path.Combine(Path.GetTempPath(), $"CrosshairOverlay-update-{update.Version}-{Guid.NewGuid():N}");

        await DownloadAsync(update.DownloadUrl, package, cancellationToken).ConfigureAwait(false);

        // Giải nén TRƯỚC khi thoát: gói hỏng thì báo lỗi ngay khi ứng dụng còn chạy, thay vì để
        // script chép dở một thư mục hỏng đè lên bản đang dùng tốt. ExtractToDirectory tự chặn
        // mục nén trỏ ra ngoài thư mục đích (zip slip).
        await Task.Run(() => ZipFile.ExtractToDirectory(package, staging), cancellationToken).ConfigureAwait(false);

        var appRoot = FindAppRoot(staging, exeName);
        if (appRoot is null)
        {
            TryDeleteDirectory(staging);
            TryDeleteFile(package);
            throw new InvalidDataException(Localization.Tr.Format("Update_ErrPackage", exeName));
        }

        var script = WriteSwapScript(installDirectory, appRoot, exeName, staging, package);

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

    /// <summary>
    /// Thư mục chứa file thực thi trong gói đã giải nén: ngay ở gốc, hoặc trong ĐÚNG MỘT thư mục con
    /// (kiểu nén "chuột phải thư mục → Compress" sinh ra). Không tìm thấy thì null.
    /// </summary>
    internal static string? FindAppRoot(string extracted, string exeName)
    {
        if (File.Exists(Path.Combine(extracted, exeName))) return extracted;

        var children = Directory.GetDirectories(extracted);
        if (children.Length == 1 && Directory.GetFiles(extracted).Length == 0
            && File.Exists(Path.Combine(children[0], exeName)))
        {
            return children[0];
        }

        return null;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dọn dẹp thư mục tạm; không xoá được thì Windows cũng tự dọn %TEMP% về sau.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Như trên.
        }
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
    /// Sinh script chép bản mới đè lên thư mục cài đặt. Ứng dụng không ghi đè được file của chính
    /// nó khi đang chạy, nên phải nhờ một tiến trình khác làm việc đó sau khi ta đã thoát.
    /// </summary>
    private static string WriteSwapScript(
        string installDirectory, string appRoot, string exeName, string staging, string package)
    {
        var scriptPath = Path.Combine(Path.GetTempPath(), $"CrosshairOverlay-update-{Guid.NewGuid():N}.bat");
        File.WriteAllText(
            scriptPath,
            BuildSwapScript(installDirectory, appRoot, exeName, staging, package),
            new UTF8Encoding(false));
        return scriptPath;
    }

    /// <remarks>
    /// <list type="bullet">
    ///   <item><c>chcp 65001</c>: đường dẫn có thể chứa tiếng Việt (vd thư mục người dùng có dấu);
    ///         file script là UTF-8, cmd phải đọc đúng bảng mã đó.</item>
    ///   <item><c>robocopy /E /IS /IT</c>: chép cả thư mục con và GHI ĐÈ mọi file, kể cả file trùng
    ///         kích thước và thời gian. KHÔNG dùng <c>/MIR</c>: nó xoá mọi thứ không có trong gói, mà
    ///         người dùng có thể đã để file của họ trong thư mục cài đặt.</item>
    ///   <item>robocopy trả mã nhỏ hơn 8 là thành công. Thất bại thì thử lại vài lần: bộ quét virus có
    ///         thể còn giữ file mới giải nén thêm một lúc.</item>
    ///   <item>Xuống dòng BẮT BUỘC là CRLF. Chuỗi bên dưới mang kiểu xuống dòng của file mã nguồn (LF),
    ///         mà cmd.exe tìm nhãn cho <c>goto</c> sai với file .bat chỉ có LF: đã chạy thử, script bỏ
    ///         qua bước chép mà không báo lỗi gì.</item>
    /// </list>
    /// </remarks>
    internal static string BuildSwapScript(
        string installDirectory, string appRoot, string exeName, string staging, string package)
    {
        var processName = Path.GetFileNameWithoutExtension(exeName);

        // robocopy hiểu dấu \ cuối trong "C:\dir\" là thoát ký tự ngoặc kép — phải bỏ đi.
        var source = appRoot.TrimEnd('\\');
        var target = installDirectory.TrimEnd('\\');

        return $"""
            @echo off
            chcp 65001 >nul
            rem Chờ ứng dụng thoát hẳn trước khi đụng vào file của nó.
            :wait
            tasklist /fi "imagename eq {processName}.exe" | find /i "{processName}.exe" >nul
            if not errorlevel 1 (
                timeout /t 1 /nobreak >nul
                goto wait
            )

            set /a tries=0
            :copy
            robocopy "{source}" "{target}" /E /IS /IT /R:2 /W:1 /NFL /NDL /NJH /NJS /NP >nul
            if not errorlevel 8 goto launch
            set /a tries+=1
            if %tries% geq 10 goto launch
            timeout /t 1 /nobreak >nul
            goto copy

            :launch
            start "" "{target}\{exeName}"

            rmdir /s /q "{staging}" >nul 2>&1
            del "{package}" >nul 2>&1

            rem Tự xoá: cmd đọc file theo từng dòng nên dòng này chạy được chính nó.
            (goto) 2>nul & del "%~f0"
            """.ReplaceLineEndings("\r\n");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _http.Dispose();
    }
}
