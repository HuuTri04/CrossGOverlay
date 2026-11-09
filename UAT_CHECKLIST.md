# CrossGOverlay — UAT trước khi phát hành ZIP

Ghi lại **Windows build, DPI, GPU, phiên bản ZIP, đường dẫn cài đặt và kết quả từng bước**. Các mục dưới đây cần chạy trên máy/VM tương tác thật; kết quả tự động không thay thế kiểm tra bằng mắt. Không dùng dữ liệu benchmark giả.

## 1. ZIP sạch và khởi động

- [ ] Chạy `build\publish.ps1`, lưu tên và SHA-256 của ZIP tạo ra.
- [ ] Chuyển ZIP sang VM Windows 10 và Windows 11 sạch, không cài .NET SDK hoặc Windows App SDK. Giải nén vào thư mục mới, chạy `CrossGOverlay.exe`; xác nhận không báo thiếu DLL hoặc COM `0x8007007E`.
- [ ] Xác nhận cửa sổ Loading WPF đóng trước khi Settings hiện; overlay vẫn xuất hiện. Dùng ảnh nền 4K và kiểm tra không có ảnh hiện trễ sau khi Settings mở.
- [ ] Chạy 5 lần khởi động warm trên mỗi VM. Ghi riêng thời gian `FirstFrame` từ log khi bật `CROSSGOVERLAY_BENCHMARK_TIMELINE=1`; so với mục tiêu **< 1000 ms**, không đánh đồng mốc log với DWM Present.
- [ ] Chạy lần đầu khi chưa có `%APPDATA%\CrosshairOverlay`; kiểm tra preset mặc định được ghi và tải lại sau khi đóng/mở ứng dụng.

## 2. Khay hệ thống và Windows Toast

- [ ] Thu nhỏ/ẩn Settings xuống khay, chờ trên 30 giây, mở lại bằng icon khay; ảnh nền vẫn hiện ngay và chỉ có một cửa sổ Settings.
- [ ] Khi cài đặt cho phép, xác nhận Windows Toast xuất hiện. Click **trực tiếp trên Toast** để mở Settings; thử cả Light/Dark Mode và Do Not Disturb theo chính sách Windows.
- [ ] Chuột phải icon khay, chọn một preset yêu thích; overlay đổi đúng preset mà không mở thêm Settings.
- [ ] Khởi động với ảnh 4K, trong lúc Loading đang hiện chọn **Thoát** ở menu khay. Xác nhận tiến trình `CrossGOverlay.exe` biến mất, không còn task nạp ảnh chạy ngầm.

## 3. Mica, tương phản cao và DPI

- [ ] Trên Windows 11 build 22621+, không đặt ảnh nền custom: kiểm tra Mica hiện sau cửa sổ Settings; màu chữ và card vẫn rõ.
- [ ] Bật ảnh nền custom: Mica tắt, ảnh nền và card hiển thị đúng.
- [ ] Bật High Contrast khi Settings đang mở và sau đó mở lại app: Mica tắt, ảnh nền custom ẩn và nền cửa sổ dùng màu hệ thống. Tắt High Contrast: nền phù hợp được khôi phục.
- [ ] Trên Windows 10: nền màu app hiển thị bình thường, không có lỗi DWM/COM.
- [ ] Đổi Dark/Light Mode, scale 125%, di chuyển cửa sổ giữa hai màn hình DPI khác nhau; kiểm tra chữ, viền, tooltip và nút vẫn đọc/nhấn được.

## 4. Pixel Art và dữ liệu người dùng

- [ ] Nhập lưới 64, 128, 512 và 1024; kiểm tra lưới nét tự ẩn trên 64, đường tâm vẫn bật/tắt. Ở lưới 1024, kéo thanh cuộn nội bộ đến góc cuối rồi vẽ/tẩy tại đó, Undo/Redo và lưu preset. Bấm Sao chép mã ở lưới lớn, xóa ô mã rồi Tải từ Clipboard; thử mã PX1 cũ. Kiểm tra huy hiệu 1.0.1 trên tiêu đề vẫn dễ đọc ở Dark Mode và High Contrast.

- [ ] Trên Wacom/Huion/Windows Ink: chọn chế độ **Bút vẽ**, vẽ nét bằng đầu bút, kéo nhanh qua nhiều ô, dùng đầu tẩy và nút barrel để xóa; chạm lòng bàn tay hoặc nhấn chuột không được tạo nét. Chọn lại **Chuột**: chuột trái/phải vẽ/tẩy, còn bút không được vẽ. Kiểm tra không có nét lặp do sự kiện chuột giả, không mở menu khi nhấn giữ, và Undo/Redo tính mỗi nét là một bước.

- [ ] Vẽ 3 nét màu khác nhau; Undo 2 lần, Redo 1 lần; kiểm tra nét thứ hai trở lại, nét thứ ba vẫn bị hoàn tác. Thử Ctrl+Z, Ctrl+Y và Ctrl+Shift+Z.
- [ ] Đánh dấu preset yêu thích, đóng/mở app; trạng thái sao vẫn còn trong Thư viện và menu khay.
- [ ] Sao lưu `%APPDATA%\CrosshairOverlay\settings.json` và `presets\` trước khi thử cập nhật. Nếu đã chọn vị trí dữ liệu khác trong app, sao lưu vị trí đó.
- [ ] Dùng **bản phát hành thử có phiên bản cao hơn trên GitHub Releases**, tên asset `CrossGOverlay-<version>-win-x64.zip` và có `digest` SHA-256, bấm Kiểm tra cập nhật và cài. Kiểm tra script giải nén ZIP, `robocopy /E /IS /IT` ghi đè file ứng dụng; `settings.json` và `presets\` của người dùng không bị thay đổi. Thử gói hỏng hoặc sai hash: ứng dụng phải báo lỗi trước khi thoát, không thay đổi thư mục cài đặt. Chỉ đặt ZIP local mà không có nguồn phát hành hợp lệ sẽ không kích hoạt Check Update.

## Biên bản

| Mục | Môi trường/phiên bản | Kết quả PASS/FAIL | Bằng chứng (log/ảnh) | Ghi chú |
| --- | --- | --- | --- | --- |
| ZIP sạch | | | | |
| 5 lần warm startup | | | | |
| Tray và Toast | | | | |
| Mica/High Contrast/DPI | | | | |
| Undo/Redo và Favorite | | | | |
| Cập nhật ZIP, bảo toàn dữ liệu | | | | |

## 5. RC2 — kiểm thử thiết bị thật và tài nguyên

- [ ] Kết nối Wacom hoặc Huion và chuột gaming. Chọn lưới 1024×1024, vẽ liên tục ít nhất 5 phút ở chế độ Bút; thử đầu tẩy, nút barrel và thao tác lật bút. Chuyển sang Chuột, lặp lại bằng chuột. Ghi tên thiết bị, driver, polling rate, độ trễ nét vẽ quan sát được, nét bị bỏ/lặp và CPU của CrossGOverlay. Không dùng thao tác giả lập để kết luận về độ trễ phần cứng.
- [ ] Vẽ 60 nét riêng biệt trên lưới 1024×1024; Undo 60 lần, Redo 60 lần. Xác nhận 50 bước lịch sử mới nhất hoạt động đúng theo giới hạn thiết kế; 10 bước cũ nhất không còn trong Undo. Theo dõi Private Bytes, Managed Heap và GDI/USER handles trước/sau để phát hiện tăng liên tục hoặc OOM.
- [ ] Trên Windows 11, bật High Contrast khi Settings đang mở và khi đang ẩn xuống tray. Xác nhận Mica tắt, nền hệ thống đặc và chữ/card vẫn đọc được; tắt High Contrast để kiểm tra phục hồi. Gửi Windows Notification, bấm trực tiếp vào Toast để mở lại Settings và kiểm tra ảnh nền cùng focus.
- [ ] Chạy stress Release x64 bằng `powershell -NoProfile -ExecutionPolicy Bypass -File benchmarks\Invoke-Stress.ps1 -Scenario preset-multistage`; lưu CSV và so GDI/Private Bytes sau warmup với ba vòng tiếp theo. Nếu vượt ngưỡng RC2 (GDI +2 hoặc Private Bytes +1 MiB sau GC), giữ nguyên kết quả FAIL và thu allocation/native stack trước khi thay đổi renderer. Script trả mã lỗi khi một ngưỡng FAIL nhưng vẫn ghi CSV.
- [ ] Nếu Private Bytes FAIL, mở PowerShell bằng **Run as Administrator** và chạy `powershell -NoProfile -ExecutionPolicy Bypass -File benchmarks\Collect-RC2VirtualAlloc.ps1`; lưu ETL/CSV cùng phiên. Trong WPA, lọc VirtualAlloc theo PID và mốc bước nhảy bộ nhớ để xác định allocation stack. Script dừng WPR dù stress FAIL.

| Hạng mục RC2 | Thiết bị/driver/Windows build | Kết quả PASS/FAIL | Bằng chứng (CSV, ảnh, video) | Ghi chú |
| --- | --- | --- | --- | --- |
| Bút + đầu tẩy + chuột, lưới 1024 | | | | |
| 60 nét Undo/Redo, RAM/handles | | | | |
| High Contrast/Mica/Toast/Tray | | | | |
| Preset nhiều vòng, GDI/Private Bytes | | | | |

## 6. Release pipeline — rollback và thiết bị thật

- [ ] Trên bản sao thư mục cài đặt (không dùng bản đang làm việc), tạo `presets/user.json`, `userdata/notes.txt` và `settings.json`; cập nhật bằng ZIP có các tên trùng nhưng nội dung khác. Xác nhận ba tệp người dùng giữ nguyên và file ứng dụng được thay.
- [ ] Trên bản sao đó, mô phỏng file DLL mới bị khóa khi deploy. Xác nhận script không khởi chạy bản chép dở, khôi phục DLL cũ từ backup và giữ nguyên dữ liệu người dùng. Kiểm tra thông báo lỗi/đường dẫn backup nếu rollback cũng thất bại.
- [ ] Chạy ZIP self-contained trên VM Windows 10 và 11 sạch, không cài .NET SDK hay Windows App SDK. Thu nhỏ Settings để thử Toast; nếu Windows App Notification không đăng ký được, xác nhận Balloon Tip native hoạt động và app không crash.
- [ ] Trước khi chạy bản Release, đặt `CROSSGOVERLAY_INPUT_DIAGNOSTICS=1` rồi quan sát `Trace` bằng DebugView khi vẽ trên Wacom/Huion ở lưới 1024. Ghi lại cảnh báo `StylusMove` > 8 ms cùng model/driver; đây là thời gian handler, chưa gồm thời gian WPF render hay thiết bị.
- [ ] Chuyển High Contrast bật/tắt khi Settings đang mở; kiểm tra log ứng dụng ghi `HighContrast Fallback: Active` và `Mica State: Active` (Windows 11 hỗ trợ, không dùng ảnh nền custom). Chụp lại cả giao diện và log để đối chiếu.
- [ ] Trên máy UAT, vẽ một lưới 24×24 có ít nhất hai màu, bấm Sao chép mã PX1, dán vào Notepad để xác nhận tiền tố `PX1|24|`, xóa canvas rồi dán vào ô Mã chia sẻ và bấm Tải từ mã. Đối chiếu từng pixel và kích thước lưới sau khi tải.
- [ ] Lặp lại với lưới 1024×1024 để nhận PX2: copy mã đầy đủ vào Clipboard, dán vào ô có giới hạn hiển thị 32.768 ký tự, bấm Tải từ mã và xác nhận phần còn lại được lấy từ Clipboard, hình và lưới khôi phục đúng. Thử mã PX2 bị cắt, sai checksum và mã `CGO-CH2;...` trong ô Pixel Art: phải hiện lỗi rõ ràng và giữ nguyên hình đang vẽ.
- [ ] Trong hộp thoại Nhập preset chung, dán mã `CGO-CH2;...` vừa xuất từ preset có màu/kích thước khác mặc định; xác nhận preview và preset sau nhập giữ đúng thuộc tính. Không dùng ô Pixel Art để nhập mã preset chung.
- [ ] Đo A/B ảnh nền Settings ngoài game bằng `powershell -NoProfile -ExecutionPolicy Bypass -File benchmarks\Measure-BackgroundCacheGpu.ps1`. Giữ cùng ảnh, độ phân giải và màn hình; đối chiếu Shared/Dedicated GPU Process Memory với `CacheMode` bật/tắt. Counter này là toàn tiến trình, không thay thế ETW DWM frame timing.
