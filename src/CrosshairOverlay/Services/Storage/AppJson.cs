using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace CrosshairOverlay.Services.Storage;

/// <summary>Cấu hình System.Text.Json dùng chung cho mọi file của ứng dụng.</summary>
/// <remarks>
/// Chỉ dùng generated metadata cho model có property viết tường minh. Các model
/// MVVM vẫn dùng reflection metadata vì source generator JSON không nhìn thấy
/// property do CommunityToolkit.Mvvm sinh trong cùng compilation.
/// Cả hai generator chạy trên cùng compilation gốc, nên JSON generator không thấy
/// các property do MVVM Toolkit sinh ra. Dùng nó cho AppSettings hoặc CrosshairProfile
/// sẽ âm thầm bỏ sót dữ liệu; fallback reflection giữ nguyên file người dùng.
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

        // The context covers explicit DTOs; MVVM-generated properties need reflection.
        // Keep the resolver immutable and shared across every settings/preset operation.
        options.TypeInfoResolverChain.Add(AppJsonSerializerContext.Default);
        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());
        options.MakeReadOnly();

        return options;
    }
}
