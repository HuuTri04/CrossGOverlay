using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace CrosshairOverlay.Core.Diagnostics;

/// <summary>
/// Đo thời gian từng bước khởi động và gộp thành MỘT dòng log.
/// </summary>
/// <remarks>
/// "App khởi động chậm" là một câu không sửa được: phải biết chậm ở bước nào. Trước đây log chỉ có
/// hai mốc đầu và cuối, nên mọi phán đoán về nguyên nhân đều là đoán. Dòng tổng kết ở đây cho thấy
/// ngay bước nào chiếm phần lớn thời gian, kể cả trên máy người dùng.
///
/// <para>
/// Chi phí của chính nó không đáng kể: một <see cref="Stopwatch"/> và một chuỗi ghép ở cuối.
/// </para>
/// </remarks>
public sealed class BootTimeline
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private readonly List<(string Step, double Ms)> _steps = [];
    private double _previous;

    /// <summary>Ghi nhận một bước vừa xong, tính từ mốc trước đó.</summary>
    public void Mark(string step)
    {
        var now = _watch.Elapsed.TotalMilliseconds;
        _steps.Add((step, now - _previous));
        _previous = now;
    }

    public double TotalMs => _watch.Elapsed.TotalMilliseconds;

    /// <summary>
    /// Các bước theo thứ tự thời gian, bước NẶNG NHẤT đứng đầu ngoặc đơn để khỏi phải dò.
    /// </summary>
    public string Summary()
    {
        if (_steps.Count == 0) return string.Empty;

        var slowest = _steps.MaxBy(s => s.Ms);
        var builder = new StringBuilder();

        foreach (var (step, ms) in _steps)
        {
            if (builder.Length > 0) builder.Append(", ");
            builder.Append(step).Append(' ').Append(Format(ms)).Append("ms");
        }

        return Format(slowest.Ms) + "ms ở '" + slowest.Step + "' — " + builder;
    }

    private static string Format(double ms) =>
        Math.Round(ms).ToString("0", CultureInfo.InvariantCulture);
}
