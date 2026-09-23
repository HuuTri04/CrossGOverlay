# CrossGOverlay

Overlay tâm ngắm tuỳ chỉnh cho Windows, tương tự Crosshair X.
**Công nghệ:** WPF · .NET 8 · MVVM · P/Invoke User32/Shcore.

> **Lưu ý về tên gọi:** Tên assembly là `CrossGOverlay`, nhưng namespace và thư mục mã nguồn vẫn giữ `CrosshairOverlay`. Việc đổi cả namespace không mang lại lợi ích gì mà lại đụng vào mọi file. Thư mục dữ liệu người dùng cũng giữ nguyên `%APPDATA%\CrosshairOverlay` để bản cập nhật không làm mất preset đã lưu.

Thiết kế và lý do đằng sau từng quyết định được ghi chú chi tiết tại [`ARCHITECTURE.md`](ARCHITECTURE.md).

---

## 📑 Mục lục
1. [Yêu cầu hệ thống](#1-yêu-cầu-hệ-thống)
2. [Hướng dẫn Build](#2-hướng-dẫn-build)
3. [Cách sử dụng & Phím tắt](#3-cách-sử-dụng--phím-tắt)
4. [Lưu trữ dữ liệu](#4-lưu-trữ-dữ-liệu)
5. [An toàn với Anti-cheat](#5-an-toàn-với-anti-cheat)
6. [Xử lý sự cố](#6-xử-lý-sự-cố)
7. [Kiểm thử (Manual & Auto)](#7-kiểm-thử)
8. [Ký số bản phát hành (Code Signing)](#8-ký-số-bản-phát-hành)
9. [Đa ngôn ngữ](#9-ngôn-ngữ-giao-diện)
10. [Thiết kế](#11-ngôn-ngữ-thiết-kế)

---

## 1. Yêu cầu hệ thống

| Thành phần | Yêu cầu |
|---|---|
| **Hệ điều hành** | Windows 10 1607+ hoặc Windows 11, kiến trúc x64 |
| **Để chạy** | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Để build** | .NET 8 SDK |

⚠️ **Quan trọng:** Ứng dụng chạy ở quyền `asInvoker` — **không cần và không nên chạy bằng quyền admin**.

## 2. Hướng dẫn Build

### Build cơ bản
```bash
# Cấu hình Debug
dotnet build Crosshair.sln -c Debug

# Cấu hình Release
dotnet build Crosshair.sln -c Release
```
Kết quả được xuất ra tại: `src\CrosshairOverlay\bin\x64\<Config>\net8.0-windows\win-x64\CrossGOverlay.exe`.

### Đóng gói bản phát hành (thư mục + file .zip)
```powershell
.\build\publish.ps1
```
Script publish **sạch** (xoá `obj\Release`, `bin\Release` trước), rồi nén cả thư mục thành `artifacts\CrossGOverlay-<phiên bản>-win-x64.zip` — khoảng **63 MB**, bên trong là một thư mục cùng tên (~256 file, ~149 MB khi giải nén). Người dùng giải nén ra đâu cũng chạy được, máy **không** cần cài .NET.
Muốn ký số trước khi nén thì thêm `-PfxPath ...` (hoặc `-SelfSigned` để thử quy trình).

Chỉ cần thư mục (không nén) thì chạy thẳng:
```bash
dotnet publish src/CrosshairOverlay/CrosshairOverlay.csproj -c Release
```
Kết quả nằm ở `src\CrosshairOverlay\bin\Release\net8.0-windows\win-x64\publish\`. Cấu hình trong `CrosshairOverlay.csproj`:
```xml
<PropertyGroup>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <!-- Gói luôn .NET Runtime vào trong: máy người dùng KHÔNG cần cài gì thêm -->
  <SelfContained>true</SelfContained>
  <!-- KHÔNG gộp thành một file .exe: gói một file tốn ~800 ms ở MỖI lần mở app -->
  <PublishSingleFile>false</PublishSingleFile>
  <PublishReadyToRun>true</PublishReadyToRun>
  <DebugType>embedded</DebugType>
</PropertyGroup>
```
## 3. Cách sử dụng & Phím tắt

Chạy `CrossGOverlay.exe`. Ở lần khởi động đầu tiên, ứng dụng sẽ:
1. Tạo thư mục `%APPDATA%\CrosshairOverlay\` và ghi 6 preset mẫu.
2. Hiển thị overlay ngay tại tâm màn hình.
3. Mở cửa sổ Settings và đặt một icon vào khay hệ thống (System Tray).
*(Trên Windows 11, icon mặc định nằm trong vùng icon ẩn; bạn có thể kéo nó ra thanh taskbar để dễ nhìn thấy).*

### Import / Export mã Crosshair (Share Code)
Ứng dụng tự nhận diện định dạng mã của 2 tựa game phổ biến:

| Game | Định dạng mã ví dụ |
|---|---|
| **Counter-Strike 2** | `CSGO-Gj9ry-3QQF3-T78kK-onMAf-6DR7B` |
| **Valorant** | `0;P;c;5;o;1;d;1;z;3;0t;4;0l;1;0o;2;0a;1` |

* **Nhập mã:** Vào tab **Crosshair** → **Nhập mã** → dán mã và bấm **Tạo preset**. Một hộp thoại xem trước (hình dạng, màu, kích thước, chấm giữa, viền) sẽ hiện ra để bạn kiểm tra.
  * *CS2:* Kích thước chỉ mang tính xấp xỉ do hệ đơn vị riêng và sự phụ thuộc vào độ phân giải/FOV. Bạn có thể cần chỉnh lại thanh **Tỉ lệ** sau khi nhập. Bộ giải mã CS2 được xây dựng dựa trên mô tả cộng đồng và kiểm chứng qua file test (`Cs2ShareCodeTests.cs`).
  * *Valorant:* Dùng đơn vị xấp xỉ pixel nên được chuyển đổi tỉ lệ 1:1.
* **Xuất mã:** Chọn preset → **Xuất mã**. Ứng dụng xuất mã chuẩn định dạng Valorant vào thẳng clipboard. Màu sắc được ghi ở dạng tuỳ chỉnh (`c;8` kèm `u;RRGGBBAA`). Nếu preset có các tính năng không hỗ trợ (vòng tròn, khung vuông, nhánh chéo, góc xoay), ứng dụng sẽ **cảnh báo trước** thay vì tạo ra một mã sai lệch.

### Phím tắt mặc định
Đổi phím tắt trong tab **Phím tắt** và bấm **Áp dụng**.

| Tổ hợp | Hành động |
|---|---|
| `Alt + X` | Bật/tắt overlay |
| `Alt + ]` | Preset kế tiếp |
| `Alt + [` | Preset trước đó |
| `Alt + C` | Mở cửa sổ Settings |

* Nếu tổ hợp đã bị chiếm bởi ứng dụng khác, phần mềm sẽ hiện lỗi ngay cạnh dòng đó.
* **Gán nút chuột:** Hỗ trợ nút phụ (Mouse 3, 4, 5). Chuột trái/phải cố tình bị vô hiệu hóa để tránh lỗi không click được trên toàn hệ thống. Chuột sử dụng **Raw Input** (`WM_INPUT` với `RIDEV_INPUTSINK`) chỉ đọc, không chặn/nuốt sự kiện, giúp an toàn hơn với anti-cheat (thay vì dùng hook cấp thấp `WH_MOUSE_LL`).

## 4. Lưu trữ dữ liệu
Xoá thư mục `%APPDATA%\CrosshairOverlay` nếu muốn đưa ứng dụng về trạng thái ban đầu.

| Đường dẫn | Chứa nội dung |
|---|---|
| `%APPDATA%\CrosshairOverlay\settings.json` | Cấu hình chung, phím tắt, profile game |
| `%APPDATA%\CrosshairOverlay\presets\*.json` | Các preset (mỗi preset 1 file, import/export bằng cách copy) |
| `%LOCALAPPDATA%\CrosshairOverlay\logs\` | Log xoay vòng theo ngày (giữ 7 ngày) |

## 5. An toàn với Anti-cheat
CrossGOverlay là một overlay desktop hoàn toàn độc lập.

**✔ Những API Windows công khai được sử dụng:**
`GetForegroundWindow`, `GetWindowThreadProcessId`, `GetWindowRect`, `GetWindowTextW`, `QueryFullProcessImageNameW`, `SetWinEventHook`, `RegisterHotKey`, `EnumDisplayMonitors`, `GetDpiForMonitor`, `SetWindowPos`, `SHQueryUserNotificationState`.

**❌ KHÔNG BAO GIỜ thực hiện:**
- Inject DLL vào tiến trình khác.
- Đọc/ghi bộ nhớ (`ReadProcessMemory` / `WriteProcessMemory`).
- Hook API của game.
- Cài hook phím/chuột cấp thấp (`WH_KEYBOARD_LL` / `WH_MOUSE_LL`).
- Mô phỏng thao tác (`SendInput`, `keybd_event`).
- Liệt kê module game hoặc dùng driver kernel.
- Các tính năng hỗ trợ như macro, auto-aim, trigger-bot.

*Lưu ý:* Ứng dụng không thể vẽ đè lên chế độ **Exclusive Fullscreen**. Sẽ có thông báo hướng dẫn bạn chuyển sang Borderless Windowed. Không có bất kỳ cơ chế bypass nào.

### Quyền Administrator
Ứng dụng mặc định không cần quyền Admin. Khi game chạy bằng quyền Admin, đa số tính năng của overlay vẫn hoạt động (vẽ đè, hotkey, lấy tiêu đề). Tuy nhiên, việc **đọc tên file thực thi sẽ bị chặn (Access Denied)**.
* **Giải pháp ưu tiên:** Chuyển sang so khớp profile game theo **tiêu đề cửa sổ** (hoạt động tốt ở quyền User).
* **Nếu bắt buộc so khớp theo tên tiến trình:** Run as Administrator hoặc sửa `app.manifest` (`<requestedExecutionLevel level="requireAdministrator" ... />`). *(Không khuyến khích vì: hiện UAC mỗi lần chạy, mất tính năng khởi động cùng Windows, không kéo thả file được, và dễ bị anti-cheat soi xét)*.

## 6. Xử lý sự cố
* **Không thấy crosshair vào game:** Game đang ở chế độ Exclusive Fullscreen. Đổi sang Borderless Windowed.
* **Phím tắt không nhận:** Bị trùng với app khác. Kiểm tra tab Phím tắt để xem lỗi và đổi tổ hợp khác.
* **Crosshair lệch tâm:** Do đổi độ phân giải. Bấm "Quét lại màn hình" trong tab Chung.
* **App không khởi động:** Check log ở `%LOCALAPPDATA%\CrosshairOverlay\logs\`. Nếu `settings.json` hỏng, app sẽ đổi tên thành `.corrupt` và chạy cấu hình mặc định.
* **Không khởi động cùng Windows:** Có thể do Group Policy chặn ghi key `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Sẽ có thông báo dưới ô check.

## 7. Kiểm thử

<details>
<summary><b>Kiểm thử thực tế (Manual Testing)</b></summary>
<br>

**Overlay:**
- [ ] Crosshair đúng tâm.
- [ ] Click chuột xuyên qua crosshair.
- [ ] Không cướp focus.
- [ ] Không hiện trong Alt-Tab.

**Màn hình & DPI:**
- [ ] Test chế độ ghim màn hình cụ thể / bám theo cửa sổ.
- [ ] Đổi Windows scaling (100% → 150%) crosshair vẫn nét và chuẩn tâm.
- [ ] Đổi độ phân giải / Rút cắm màn hình khi app đang chạy.

**Editor & Giao diện:**
- [ ] Kéo slider áp dụng tức thì, color picker hoạt động tốt.
- [ ] Tắt/mở app giữ nguyên cấu hình.
- [ ] Test tạo/nhân bản/xoá/import/export.

**Phím tắt & Khay hệ thống:**
- [ ] Phím tắt hoạt động cả khi game giữ focus.
- [ ] Menu chuột phải khay hệ thống hoạt động.
- [ ] Chạy instance thứ 2 sẽ mở Settings của instance đầu.

**Profile Game (nhận diện tự động):**
- [ ] Thử bằng notepad.exe (tự động đổi crosshair khi mở/đóng).
- [ ] "Thêm từ cửa sổ vừa dùng" nhận đúng tiến trình game.

**Khác:**
- [ ] Borderless Windowed (hiện), Exclusive Fullscreen (không hiện, có báo lỗi).
- [ ] Không xé hình ở 144Hz/240Hz.
</details>

### Kiểm thử tự động (Unit Tests)
Chạy bằng lệnh: `dotnet test Crosshair.sln` (Gồm 145 bài test, hoàn thành dưới 1 giây).
Các bài test bao gồm: `GameProfileMatcherTests`, `JsonSerializationTests`, `CrosshairRendererTests`, `PresetRepositoryTests`, `Cs2ShareCodeTests`, `ValorantCrosshairCodeTests`, `KeyNamesTests`, và `LocalizationTests`.

## 8. Ký số bản phát hành
File `.exe` chưa ký sẽ bị SmartScreen cảnh báo. Dùng script PowerShell kèm theo:
```powershell
# Ký bằng chứng chỉ tự ký (chỉ hợp lệ nội bộ để test)
.\build\sign.ps1 -SelfSigned

# Ký bằng chứng chỉ thật
.\build\sign.ps1 -PfxPath C:\certs\company.pfx -PfxPassword (Read-Host -AsSecureString)
```
*Lưu ý:* Chỉ có chứng chỉ EV mới lập tức gỡ bỏ cảnh báo SmartScreen ngay lượt tải đầu. Chứng chỉ tự ký không có tác dụng với máy người dùng. Script có tích hợp đóng dấu thời gian (timestamping) để chữ ký hợp lệ vĩnh viễn dù chứng chỉ hết hạn.

## 9. Ngôn ngữ giao diện
Hỗ trợ **Tiếng Việt** và **Tiếng Anh**. Mặc định lấy theo ngôn ngữ hệ thống. Đổi ngôn ngữ trong app sẽ có hiệu lực ngay lập tức mà không cần khởi động lại.

**Thêm ngôn ngữ mới:**
1. Chép `Strings.resx` thành `Strings.<mã>.resx` (vd `Strings.ja.resx`) và dịch các giá trị `<value>`. Giữ nguyên `{0}`, `{1}`.
2. Thêm mã vào `SatelliteResourceLanguages` trong file `.csproj`.
3. Khai báo vào `LanguageCatalog.All`.
4. Cập nhật bài test `LocalizationTests`.

## 10. Ngôn ngữ thiết kế
Sử dụng bộ UI **MongoDB LeafyGreen (Dark mode)**:
- Nền `#001E2B`, thẻ `#1C2D38`, viền `#3D4F58`.
- Điểm nhấn xanh lá `#00A35C` / `#00ED64`.
- Font Segoe UI Variable, góc bo 4px (nút) và 6px (thẻ).
- Không dùng `MessageBox` gốc của Windows, mọi dialog được custom đồng bộ thiết kế.
Mọi định dạng nằm ở `src\CrosshairOverlay\Resources\Theme.xaml`.

## Contributors
Cảm ơn @HuuwxLoiwf đã đồng hành và đóng góp cho project này