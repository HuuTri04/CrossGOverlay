using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrosshairOverlay.Services.Storage;

/// <summary>Cấu hình System.Text.Json dùng chung cho mọi file của ứng dụng.</summary>
internal static class AppJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            // File cấu hình là thứ người dùng mở ra sửa tay và gửi cho nhau.
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,

            // Bỏ qua property lạ thay vì ném exception: preset của phiên bản mới hơn vẫn
            // mở được ở phiên bản cũ, chỉ mất phần không hiểu.
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        options.Converters.Add(new JsonColorConverter());

        // Enum ghi bằng tên ("TShape") thay vì số — sửa tay và diff git đều dễ hơn.
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
