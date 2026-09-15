using System.Reflection;

namespace CrosshairOverlay.Core;

/// <summary>
/// Tên và phiên bản hiển thị của ứng dụng.
/// </summary>
/// <remarks>
/// Gom về một chỗ để tên thương hiệu không bị rải rác thành chuỗi literal khắp nơi — đổi tên
/// lần sau chỉ phải sửa ở đây.
///
/// <para>
/// Tên hiển thị KHÔNG nằm trong file resource ngôn ngữ: tên riêng thì không dịch.
/// </para>
/// </remarks>
public static class AppInfo
{
    public const string DisplayName = "CrossGOverlay";

    /// <summary>Slogan dưới tên ứng dụng. Là một phần thương hiệu nên cũng không dịch.</summary>
    public const string Tagline = "Overlay for every gamer";

    /// <summary>Phiên bản dạng "v1.2.3", bỏ phần revision vì nó luôn là 0.</summary>
    public static string DisplayVersion { get; } = BuildVersion();

    public static Version Version { get; } =
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    private static string BuildVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null ? "v0.0.0" : $"v{version.Major}.{version.Minor}.{version.Build}";
    }
}
