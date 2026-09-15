using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Threading;

namespace CrosshairOverlay.Services.Rendering;

/// <summary>Chạy và dừng GIF động trong các Drawing do renderer dựng.</summary>
/// <remarks>
/// Mỗi GIF đang hiện có một bộ phát khung riêng: một <see cref="DispatcherTimer"/> hẹn giờ ĐÚNG
/// bằng delay của khung hiện tại, tới giờ thì đổi <see cref="ImageDrawing.ImageSource"/> sang khung
/// kế tiếp đã giải mã sẵn.
///
/// <para>
/// Cố tình không dùng hệ animation của WPF. Đo thực tế trên overlay: đồng hồ animation, kể cả khi
/// đã hạ nhịp bằng <c>DesiredFrameRate</c>, vẫn tick nhiều hơn số khung thật và mỗi tick kéo theo
/// một vòng xử lý đầy đủ của WPF. Bộ phát khung chỉ đánh thức luồng giao diện đúng số lần GIF đổi
/// hình. Và vì không có đồng hồ nào gắn vào cây thời gian toàn cục, dừng là dừng hẳn — không có
/// đồng hồ mồ côi chờ GC như trước.
/// </para>
///
/// <para>
/// Mọi Drawing có GIF PHẢI được <see cref="Stop"/> khi hết dùng; bộ phát đang chạy giữ Drawing
/// sống. Overlay dừng khi nhận hình mới hoặc khi ẩn; preview dừng khi dựng lại hoặc khi cửa sổ ẩn.
/// </para>
/// </remarks>
internal static class DrawingAnimations
{
    private static readonly ConditionalWeakTable<ImageDrawing, FramePlayer> Players = new();

    /// <summary>
    /// Số bộ phát đang chạy trên luồng hiện tại. Bộ phát thuộc về luồng giao diện đã tạo ra nó, nên
    /// đếm theo luồng mới đúng — và giữ cho các test chạy song song không đếm lẫn của nhau.
    /// </summary>
    [ThreadStatic] private static int t_active;

    /// <summary>
    /// Khoảng tối thiểu giữa hai khung GIF, theo giới hạn FPS của overlay. GIF khai delay ngắn hơn
    /// (vài GIF ghi 10 ms = 100 khung/giây) sẽ bị giãn ra; không giới hạn thì theo đúng GIF.
    /// </summary>
    internal static TimeSpan MinFrameInterval { get; set; } = TimeSpan.FromMilliseconds(1000d / 60);

    internal static int ActiveCount => t_active;

    internal static bool IsPlaying(ImageDrawing image) => Players.TryGetValue(image, out _);

    public static void Start(ImageDrawing image, AnimatedImage animation)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(animation);
        if (animation.Frames.Count < 2) return;

        Players.AddOrUpdate(image, new FramePlayer(image, animation));
        t_active++;
    }

    public static void Stop(Drawing? drawing)
    {
        switch (drawing)
        {
            case null:
                return;

            // Nhóm đã đóng băng không thể chứa GIF đang chạy — bỏ qua cả nhánh.
            case DrawingGroup { IsFrozen: true }:
                return;

            case DrawingGroup group:
                foreach (var child in group.Children) Stop(child);
                return;

            case ImageDrawing image when Players.TryGetValue(image, out var player):
                Players.Remove(image);
                player.Stop();
                t_active--;
                return;
        }
    }

    private sealed class FramePlayer
    {
        private readonly ImageDrawing _image;
        private readonly AnimatedImage _animation;
        private readonly DispatcherTimer _timer;
        private int _index;

        public FramePlayer(ImageDrawing image, AnimatedImage animation)
        {
            _image = image;
            _animation = animation;
            _image.ImageSource = animation.Frames[0];

            // Ưu tiên Render: đổi khung cùng lượt với việc vẽ, không chen trước input của người dùng.
            _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = Clamp(animation.Delays[0]) };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        private void OnTick(object? sender, EventArgs e)
        {
            _index = (_index + 1) % _animation.Frames.Count;
            _image.ImageSource = _animation.Frames[_index];

            // Mỗi khung GIF có delay riêng; đọc giới hạn mỗi nhịp để đổi FPS có hiệu lực ngay.
            _timer.Interval = Clamp(_animation.Delays[_index]);
        }

        private static TimeSpan Clamp(TimeSpan delay) => delay < MinFrameInterval ? MinFrameInterval : delay;

        public void Stop()
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
        }
    }
}
