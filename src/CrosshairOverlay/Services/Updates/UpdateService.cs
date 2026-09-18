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
    /// <summary>Chủ sở hữu kho phát hành trên GitHub.</summary>
    public const string GithubOwner = "HuuTri04";

    /// <summary>Tên kho phát hành trên GitHub.</summary>
    public const string GithubRepo = "CrossGOverlay";

    /// <summary>
    /// Địa chỉ API của bản phát hành mới nhất.
    /// </summary>
    /// <remarks>
    /// Phải là <c>api.github.com</c>, KHÔNG phải trang web <c>github.com/.../releases/latest</c>: trang web trả về
    /// HTML, không phải JSON có <c>tag_name</c> và <c>assets</c>.
    /// </remarks>
    public static readonly string LatestReleaseUrl =
        $"https://api.github.com/repos/{GithubOwner}/{GithubRepo}/releases/latest";

    /// <summary>GitHub trả 403 nếu request không có User-Agent.</summary>
    public const string UserAgent = "CrossGOverlay-App";

    /// <summary>Đủ để một mạng bình thường trả lời, và đủ ngắn để mạng hỏng không làm chờ lâu.</summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(8);

    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    private readonly ILogger<UpdateService> _logger;
    private readonly HttpClient _http;
    private bool _disposed;

    public UpdateService(ILogger<UpdateService> logger)
        : this(logger, new HttpClientHandler())
    {
    }

    /// <param name="handler">Test truyền handler giả để không đụng tới mạng thật.</param>
    internal UpdateService(ILogger<UpdateService> logger, HttpMessageHandler handler)
    {
        _logger = logger;

        _http = new HttpClient(handler) { Timeout = DownloadTimeout };

        // GitHub API từ chối request không có User-Agent.
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue(UserAgent, CurrentVersion.ToString()));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public Version CurrentVersion { get; } =
        typeof(UpdateService).Assembly.GetName().Version ?? new Version(0, 0, 0, 0);

    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CheckTimeout);

            using var response = await _http.GetAsync(LatestReleaseUrl, timeout.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                // Kho chưa có bản phát hành nào cũng rơi vào đây (404). Không nói với người dùng là "đã mới nhất":
                // ta không biết điều đó, ta chỉ biết là không hỏi được.
                _logger.LogDebug("Kiểm tra cập nhật trả về {Status}.", (int)response.StatusCode);
                return new UpdateCheck(UpdateCheckStatus.Failed);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: timeout.Token)
                .ConfigureAwait(false);

            var (result, reason) = Evaluate(document.RootElement, CurrentVersion);

            // Bản phát hành đặt sai (thẻ không phải số phiên bản, thiếu gói .zip) không được im lặng: người phát
            // hành cần biết vì sao người dùng không nhận được bản cập nhật.
            if (result.Status == UpdateCheckStatus.Failed) _logger.LogWarning("Kiểm tra cập nhật: {Reason}.", reason);
            else _logger.LogInformation("Kiểm tra cập nhật: {Reason}.", reason);

            return result;
        }
        catch (Exception ex)
        {
            // Mất mạng, DNS hỏng, timeout, JSON lạ — tất cả đều chỉ có nghĩa "không kiểm tra
            // được lúc này". Không bao giờ để chuyện này nổi lên thành lỗi cho người dùng.
            _logger.LogDebug(ex, "Không kiểm tra được cập nhật.");
            return new UpdateCheck(UpdateCheckStatus.Failed);
        }
    }

    /// <summary>Chỉ để test đọc nhanh: bản cập nhật, hoặc null nếu vì bất kỳ lý do gì không có.</summary>
    internal static UpdateInfo? ParseRelease(JsonElement release, Version current) => Evaluate(release, current).Result.Update;

    /// <summary>
    /// Xét bản phát hành mới nhất trên GitHub.
    /// </summary>
    /// <returns>Trạng thái, kèm một câu giải thích để ghi log.</returns>
    /// <remarks>
    /// <para>
    /// "Thẻ không đọc được thành số phiên bản" KHÔNG phải "đang ở bản mới nhất": nói câu thứ hai là nói sai với người
    /// dùng. Thẻ phải có dạng <c>v1.2.3</c> (chữ "v" tuỳ chọn); thẻ như <c>First-release</c> thì không so sánh được
    /// với phiên bản đang chạy.
    /// </para>
    /// <para>
    /// Ứng dụng phát hành dạng THƯ MỤC, nên gói cập nhật phải là file <c>.zip</c> chứa cả thư mục.
    /// Cố tình KHÔNG nhận file <c>.exe</c>: một bản phát hành dạng một file mà chép đè lên
    /// <c>CrossGOverlay.exe</c> của bản thư mục thì chỉ thay được file khởi chạy, còn mã của ứng dụng
    /// (CrossGOverlay.dll) vẫn là bản cũ — cập nhật "thành công" mà không có gì đổi.
    /// Có nhiều .zip thì ưu tiên gói ghi rõ <c>win-x64</c>.
    /// </para>
    /// </remarks>
    internal static (UpdateCheck Result, string Reason) Evaluate(JsonElement release, Version current)
    {
        var rawTag = release.TryGetProperty("tag_name", out var tag) ? tag.GetString() : null;
        var latest = ParseVersion(rawTag);

        if (latest is null)
        {
            return (new UpdateCheck(UpdateCheckStatus.Failed),
                $"thẻ phát hành '{rawTag ?? "(không có)"}' không phải số phiên bản (cần dạng v1.2.3)");
        }

        if (latest <= current) return (new UpdateCheck(UpdateCheckStatus.UpToDate), $"đang ở bản mới nhất ({current})");

        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return (new UpdateCheck(UpdateCheckStatus.Failed), $"bản {latest} không có file đính kèm nào");
        }

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

        if (chosen is null)
        {
            return (new UpdateCheck(UpdateCheckStatus.Failed),
                $"bản {latest} không có gói .zip nào (bản phát hành dạng thư mục bắt buộc phải kèm .zip)");
        }

        var page = release.TryGetProperty("html_url", out var h) ? h.GetString() : null;
        var notes = release.TryGetProperty("body", out var b) ? b.GetString() : null;
        var update = new UpdateInfo(latest, chosen, page ?? chosen, (notes ?? string.Empty).Trim());

        return (new UpdateCheck(UpdateCheckStatus.UpdateAvailable, update), $"có bản {latest}");
    }

    /// <summary>Thẻ phát hành thường có tiền tố "v", vd "v1.4.0".</summary>
    private static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;

        var text = tag.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out var version) ? version : null;
    }

    public async Task<bool> DownloadAndApplyAsync(
        UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
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

        await DownloadAsync(update.DownloadUrl, package, progress, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// Tải gói về file tạm, vừa tải vừa báo phần trăm.
    /// </summary>
    /// <remarks>
    /// <c>ResponseHeadersRead</c> để đọc được Content-Length rồi chép theo từng khối; đợi tải xong cả nội dung mới
    /// trả về thì thanh tiến trình chỉ có hai trạng thái 0% và 100%. Máy chủ không gửi Content-Length (hiếm) thì
    /// không có phần trăm để báo, chỉ báo lúc xong.
    /// </remarks>
    internal async Task DownloadAsync(
        string url, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await _http
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;

        // Tải ra file tạm rồi mới đổi tên: tải dở dang không được để lại một gói cụt mà
        // script sau đó lại đem chép đè lên bản đang chạy tốt.
        var partial = destination + ".part";

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = File.Create(partial))
        {
            var buffer = new byte[81920];
            long received = 0;
            var lastPercent = -1;

            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;

                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;

                if (progress is null || total is not > 0) continue;

                // Chỉ báo khi số phần trăm thật sự đổi: gói 60 MB là ~800 khối, không cần 800 lần vẽ lại giao diện.
                var percent = (int)(received * 100 / total.Value);
                if (percent == lastPercent) continue;

                lastPercent = percent;
                progress.Report(percent / 100d);
            }
        }

        File.Move(partial, destination, overwrite: true);
        progress?.Report(1d);
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
