using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrosshairOverlay.Services.Storage;

/// <summary>Cấu hình System.Text.Json dùng chung cho mọi file của ứng dụng.</summary>
/// <remarks>
/// Cố tình dùng đường reflection thay vì <c>JsonSerializerContext</c> sinh sẵn lúc biên dịch.
/// Đã thử và phải bỏ, vì hai lý do độc lập nhau, mỗi lý do đều đủ để loại:
///
/// <list type="number">
///   <item>
///     Các model ở đây dùng <c>[ObservableProperty]</c>. Mọi source generator đều chạy trên
///     CÙNG một bản compilation gốc và không thấy output của nhau, nên bộ sinh mã JSON không
///     hề biết những property mà CommunityToolkit.Mvvm sinh ra. Kết quả không phải lỗi biên
///     dịch mà là mất dữ liệu im lặng: file preset ghi ra thiếu hẳn Name, Shape, Color và toàn
///     bộ cờ bool. Đã kiểm chứng bằng cách chạy thật — chỉ những property viết tay còn sót lại.
///   </item>
///   <item>
///     Lợi ích chính của source generator là bỏ reflection để trim được, mà ứng dụng WPF thì
///     không trim được (NETSDK1168, xem .csproj). Nên cái giá phải trả không đổi lấy được gì.
///   </item>
/// </list>
///
/// Muốn quay lại hướng đó thì phải viết tay toàn bộ property của lớp model trước đã.
/// </remarks>
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
