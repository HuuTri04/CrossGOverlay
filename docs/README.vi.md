# CrossGOverlay

> Ứng dụng tâm ngắm tùy chỉnh siêu nhẹ, hiệu năng cao và an toàn tuyệt đối với Anti-cheat dành cho môi trường Windows.

[![Platform: Windows](https://img.shields.io/badge/Nền%20tảng-Windows%2010%20%7C%2011%20(x64)-0078D6?logo=windows&logoColor=white)](#yêu-cầu-hệ-thống)
[![Runtime: .NET 8](https://img.shields.io/badge/Runtime-.NET%208.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Architecture: MVVM](https://img.shields.io/badge/Kiến%20trúc-MVVM%20%2B%20WPF-brightgreen)](#4-kiến-trúc-tổng-thể)
[![Safety: Anti--Cheat Safe](https://img.shields.io/badge/Anti--Cheat-100%25%20Bị%20động%20%26%20An%20toàn-success)](#cam-kết-an-toàn-với-anti-cheat)
[![License: MIT](https://img.shields.io/badge/Giấy%20phép-MIT-blue.svg)](LICENSE)

---

## 1. Tiêu đề & Giới thiệu chung

**CrossGOverlay** là giải pháp tâm ngắm máy tính chuyên nghiệp được tối ưu hóa phần cứng, phát triển chuyên biệt cho môi trường gaming cạnh tranh đòi hỏi độ chuẩn xác cao và độ trễ tối thiểu. Được xây dựng trên nền tảng **.NET 8** và **WPF**, ứng dụng kết xuất tâm ngắm sắc nét đến từng pixel đè lên các tựa game chạy chế độ Borderless Windowed mà không kích hoạt hệ thống cảnh báo chống gian lận (Anti-cheat) hay gây giật/trễ khung hình.

Được thiết kế như một giải pháp mã nguồn mở, độc lập thay thế cho các phần mềm thương mại như Crosshair X, CrossGOverlay kết hợp các Win32 hook tầng hệ điều hành tiêu tốn ít tài nguyên cùng khả năng tự động chuyển đổi hồ sơ game theo thời gian thực, tùy biến tâm linh hoạt và hỗ trợ đồng bộ mã chia sẻ tâm đa tựa game (Counter-Strike 2 và Valorant).

---

## 2. Bối cảnh & Mục tiêu

Trong thi đấu thể thao điện tử, tính nhất quán về mặt thị giác là yếu tố quyết định phản xạ. Tuy nhiên, tâm ngắm mặc định trong game thường gặp các hạn chế: bị co giãn/giật rung khi di chuyển, khả năng tương phản kém trước các bối cảnh bản đồ phức tạp, hoặc bị giới hạn bởi engine đồ họa của từng game. Giải pháp tâm ngắm tích hợp sẵn trên màn hình phần cứng thì thường bị lệch tâm, hình dạng thô sơ và thao tác nút bấm vật lý (OSD) rất bất tiện.

**CrossGOverlay** giải quyết triệt để vấn đề này bằng cách thiết lập một cửa sổ overlay không viền, trong suốt nổi trên màn hình. Thông qua việc áp dụng các thuộc tính composition chuẩn của Windows (`WS_EX_TRANSPARENT`, `WS_EX_LAYERED`, `WS_EX_NOACTIVATE`), CrossGOverlay đảm bảo toàn bộ thao tác chuột và phím được chuyển thẳng trực tiếp xuống game mà không bị chặn lại. Ứng dụng chạy hoàn toàn ở quyền người dùng thông thường (`asInvoker`), tuyệt đối không can thiệp bộ nhớ hay hàm API của game, hoạt động an toàn bên cạnh các hệ thống anti-cheat cấp kernel như Riot Vanguard, Easy Anti-Cheat (EAC) hay BattlEye.

---

## 3. Tính năng cốt lõi

- **An toàn tuyệt đối với Anti-cheat**: Hoạt động hoàn toàn thụ động như một cửa sổ desktop thông thường. Cam kết 100% không inject DLL, không nạp kernel driver, không mô phỏng thao tác phần cứng (`SendInput`) và không đọc/ghi bộ nhớ game.
- **Bộ chuyển đổi mã tâm hai chiều (Bi-directional Share Code)**:
  - **Counter-Strike 2**: Giải mã luồng bitpack chuẩn Base64 (`CSGO-xxxxx-xxxxx-...`).
  - **Valorant**: Khởi tạo và dịch ngược chuỗi định dạng chuẩn 1:1 (`0;P;c;...`).
  - **Xem trước trực quan**: Dialog kiểm tra hình dạng, độ mờ, viền và chấm tâm trước khi áp dụng nhằm loại bỏ lỗi sai cú pháp.
- **Hiển thị hiệu năng cao**: Tận dụng khả năng tăng tốc phần cứng DirectX của WPF với tần suất vẽ dưới mili-giây, mượt mà trên các màn hình tần số quét cao (144Hz, 240Hz, 360Hz+).
- **Nhận diện Per-Monitor V2 DPI**: Tự động cân chỉnh tỷ lệ và giữ tâm chuẩn xác tuyệt đối khi kéo thả qua các màn hình có tỉ lệ thu phóng khác nhau (100% vs 150%) hoặc khi cắm/rút màn hình.
- **Tự động kích hoạt Profile Game**: Tự nhận diện và đổi tâm ngắm ngay khi cửa sổ game được focus nhờ cơ chế lắng nghe sự kiện WinEvent nhẹ nhàng.
- **Phím tắt toàn cục & Raw Input an toàn**: Quản lý phím tắt qua `RegisterHotKey` và hỗ trợ gán nút chuột phụ (Mouse 3, 4, 5) bằng cơ chế `WM_INPUT` (`RIDEV_INPUTSINK`) chỉ đọc, không nuốt sự kiện click.
- **Giao diện MongoDB LeafyGreen**: Ngôn ngữ thiết kế Dark mode hiện đại (`#001E2B`, viền `#3D4F58`, điểm nhấn xanh neon `#00ED64`), hỗ trợ chuyển đổi song ngữ Anh - Việt tức thì.

---

## 4. Kiến trúc tổng thể

CrossGOverlay tuân thủ nghiêm ngặt mô hình kiến trúc **Model-View-ViewModel (MVVM)**, tách bạch giữa giao diện, trạng thái dữ liệu và lớp tương tác cấp thấp với Windows Subsystem.

### Luồng tương tác hệ thống

```mermaid
flowchart TD
    subgraph OS_Layer [Tầng Hệ điều hành / Win32 Subsystem]
        User32[User32.dll / Shcore.dll]
        DisplayMgr[Quản lý Hiển thị & DPI Context]
        GameWindow[Cửa sổ Game mục tiêu - Borderless]
    end

    subgraph Service_Tier [Tầng Dịch vụ & Xử lý nền]
        WinEventHook[WinEvent Hook\nSetWinEventHook]
        RawInputSink[Bộ thu Raw Input\nWM_INPUT Sink]
        HotkeyMgr[Điều phối Phím tắt\nRegisterHotKey]
        ProfileMatcher[GameProfileMatcher]
        SettingsRepo[Preset & Config Repository]
    end

    subgraph ViewModel_Tier [Tầng Quản lý trạng thái (MVVM)]
        AppVM[App / Main ViewModel]
        OverlayVM[Overlay ViewModel]
        SettingsVM[Settings & Editor ViewModel]
    end

    subgraph View_Tier [Giao diện Tăng tốc phần cứng]
        SettingsView[Cửa sổ Cấu hình Settings]
        OverlayWindow[Cửa sổ Layered Trong suốt\nWS_EX_TRANSPARENT]
        ReticleCanvas[Bề mặt vẽ DirectX]
    end

    %% Điều phối sự kiện từ OS
    User32 -->|Thay đổi cửa sổ Foreground| WinEventHook
    User32 -->|Raw Input nút chuột phụ| RawInputSink
    User32 -->|Tổ hợp phím tắt toàn cục| HotkeyMgr
    DisplayMgr -->|WM_DPICHANGED| OverlayWindow

    %% Định tuyến dịch vụ
    WinEventHook -->|HWND / Process ID| ProfileMatcher
    ProfileMatcher -->|Profile tương ứng| SettingsRepo
    SettingsRepo -->|Nạp Preset| AppVM
    RawInputSink -->|Lệnh kích hoạt| AppVM
    HotkeyMgr -->|Lệnh Bật/Tắt / Đổi tâm| AppVM

    %% ViewModels tới Views
    AppVM --> OverlayVM
    AppVM --> SettingsVM
    SettingsVM <==> SettingsView
    OverlayVM -->|Đẩy thông số Vector / Render| OverlayWindow
    OverlayWindow --> ReticleCanvas
    ReticleCanvas -.->|Hiển thị đè không cướp focus| GameWindow
```

### Nguyên tắc kiến trúc & An toàn hệ thống

#### Cam kết an toàn với Anti-cheat
CrossGOverlay chỉ sử dụng các Win32 User API công khai được Microsoft tài liệu hóa:
- Quá trình quét thông tin tiến trình chỉ dừng ở mức gọi `GetWindowThreadProcessId` và `QueryFullProcessImageNameW` để so khớp tên profile.
- Cửa sổ tâm ngắm được định danh cờ `WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE`. Windows DWM sẽ tự động chuyển tiếp toàn bộ thao tác click chuột xuyên thẳng xuống game.
- Tuyệt đối không cài hook cấp thấp (`WH_KEYBOARD_LL`, `WH_MOUSE_LL`) – các kỹ thuật luôn bị thuật toán Heuristic của Anti-cheat giám sát chặt chẽ.

#### Cơ chế Single-Instance Mutex
Quản lý tiến trình đơn nhất thông qua `System.Threading.Mutex` cấp hệ thống. Khi người dùng mở shortcut lần 2, instance cũ sẽ tự động nhận tín hiệu và đưa cửa sổ Settings hiện tại lên trên cùng.

---

## 5. Hướng dẫn cài đặt

### Yêu cầu hệ thống

| Thành phần | Yêu cầu tối thiểu | Khuyến nghị |
|---|---|---|
| **Hệ điều hành** | Windows 10 (Bản 1607+) x64 | Windows 11 x64 (Bản mới nhất) |
| **Khi chạy** | Đã tích hợp sẵn trong bản publish | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **Khi build mã nguồn** | [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | Visual Studio 2022 (v17.8 trở lên) |
| **Quyền thực thi** | Quyền Người dùng thường (`asInvoker`) | Quyền Người dùng thường (`asInvoker`) |

### Lựa chọn A: Bản dựng sẵn (Khuyên dùng)

1. Vào mục **[Releases](../../releases)** của kho mã nguồn.
2. Tải file nén mới nhất: `CrossGOverlay-<version>-win-x64.zip`.
3. Giải nén vào một thư mục bất kỳ (ví dụ: `C:\Tools\CrossGOverlay`).
4. Khởi chạy trực tiếp file `CrossGOverlay.exe`.

> **Lưu ý:** Bản release chính thức được đóng gói ở định dạng độc lập (Self-contained) kèm ReadyToRun. Máy tính người dùng không cần cài thêm .NET Runtime.

### Lựa chọn B: Tự biên dịch từ mã nguồn

Clone kho mã nguồn về máy:
```bash
git clone https://github.com/your-username/CrossGOverlay.git
cd CrossGOverlay
```

Khôi phục các gói phụ thuộc và build:
```bash
dotnet restore Crosshair.sln
dotnet build Crosshair.sln -c Release
```

---

## 6. Khởi chạy & Vận hành

### Khởi chạy qua .NET CLI
```bash
# Chạy chế độ Debug có xuất log chi tiết
dotnet run --project src/CrosshairOverlay/CrosshairOverlay.csproj -c Debug

# Chạy bản Release tối ưu hóa
dotnet run --project src/CrosshairOverlay/CrosshairOverlay.csproj -c Release
```

### Đóng gói phát hành tự động
Để tạo bản build hoàn chỉnh, dọn dẹp thư mục tạm, nhúng runtime và nén zip:

```powershell
# Đóng gói thông thường
.\build\publish.ps1

# Đóng gói kèm ký số chứng chỉ Authenticode
.\build\publish.ps1 -PfxPath "C:\certs\code_signing.pfx" -PfxPassword (Read-Host -AsSecureString)
```
Kết quả đóng gói sẽ được xuất tại thư mục: `artifacts\CrossGOverlay-<version>-win-x64.zip`.

### Phím tắt mặc định

| Tổ hợp | Hành động | Mô tả |
|---|---|---|
| `Alt + X` | **Bật/Tắt Overlay** | Ẩn hoặc hiện bề mặt tâm ngắm trên màn hình. |
| `Alt + ]` | **Preset kế tiếp** | Chuyển tới preset tiếp theo trong danh sách. |
| `Alt + [` | **Preset trước đó** | Quay lại preset liền trước. |
| `Alt + C` | **Mở Cài đặt** | Bật giao diện điều khiển và chỉnh sửa tâm. |
| `Mouse 3 / 4 / 5` | **Tự chọn gán** | Hỗ trợ bắt phím chuột phụ qua Raw Input. |

---

## 7. Cấu hình môi trường & Lưu trữ dữ liệu

Ứng dụng lưu trữ dữ liệu hoàn toàn dưới dạng tệp tin cục bộ theo chuẩn cấu trúc thư mục của Windows, không ghi vào Windows Registry.

### Cấu trúc đường dẫn dữ liệu

```
%APPDATA%\CrosshairOverlay\
├── settings.json              <-- Cấu hình chung, phím tắt, danh sách profile game
└── presets\                   <-- Thư mục chứa các preset tâm (mỗi preset là 1 file json)
    ├── default.json
    ├── dot.json
    └── competitive_cross.json

%LOCALAPPDATA%\CrosshairOverlay\
└── logs\                      <-- Log hệ thống luân phiên (tự dọn dẹp sau 7 ngày)
```

### Mẫu định dạng `settings.json`
```json
{
  "General": {
    "Language": "vi-VN",
    "StartWithWindows": false,
    "HideInExclusiveFullscreenWarning": true
  },
  "Hotkeys": {
    "ToggleOverlay": "Alt + X",
    "NextPreset": "Alt + OemCloseBrackets",
    "PreviousPreset": "Alt + OemOpenBrackets",
    "OpenSettings": "Alt + C"
  },
  "GameProfiles": [
    {
      "ProfileName": "Counter-Strike 2",
      "ProcessName": "cs2.exe",
      "WindowTitle": "Counter-Strike 2",
      "PresetId": "competitive_cross",
      "MatchByTitleOnly": false
    }
  ]
}
```

---

## 8. Cấu trúc thư mục dự án

```
CrossGOverlay/
├── .github/                      # Quy trình CI/CD Workflows, issue templates
├── build/                        # Script tự động hóa đóng gói và phát hành
│   ├── publish.ps1               # Pipeline build sạch và nén file phát hành
│   └── sign.ps1                  # Tiện ích ký số chứng chỉ Authenticode
├── src/
│   └── CrosshairOverlay/         # Mã nguồn chính của ứng dụng (WPF / .NET 8)
│       ├── Common/               # Các hằng số, Enum, tiện ích chung
│       ├── Interop/              # Khai báo Win32 P/Invoke (User32, Shcore)
│       ├── Models/               # Lớp thực thể dữ liệu, cấu hình, xử lý share code
│       ├── Native/               # Bộ thu Raw Input và xử lý WinEvent hook
│       ├── Resources/            # Style LeafyGreen XAML, tài nguyên hình ảnh
│       │   ├── Localization/     # Strings.resx, Strings.vi.resx
│       │   └── Theme.xaml        # Bảng màu và khuôn mẫu giao diện UI
│       ├── Services/             # Logic match profile, kho preset, hotkey
│       ├── ViewModels/           # Lớp ViewModel điều phối trạng thái (MVVM)
│       ├── Views/                # Các cửa sổ giao diện XAML (Overlay, Settings)
│       ├── App.xaml              # Điểm khởi tạo ứng dụng và Dependency Injection
│       └── CrosshairOverlay.csproj
├── tests/
│   └── CrosshairOverlay.Tests/   # Bộ kiểm thử tự động xUnit
│       ├── Cs2ShareCodeTests.cs
│       ├── ValorantCodeTests.cs
│       ├── GameProfileMatcherTests.cs
│       └── PresetRepositoryTests.cs
├── ARCHITECTURE.md               # Bản thiết kế chi tiết về mặt kỹ thuật
├── Crosshair.sln                 # Solution file Visual Studio
├── LICENSE                       # Giấy phép mã nguồn mở MIT
└── README.md                     # Tài liệu giới thiệu dự án
```

---

## 9. Hướng dẫn đóng góp (Contribution Guidelines)

Cộng đồng lập trình viên luôn được hoan nghênh tham gia đóng góp. Để giữ chất lượng mã nguồn luôn đồng nhất, vui lòng tuân thủ quy chuẩn kỹ thuật sau:

### Quy trình phát triển
1. **Fork & Phân nhánh**: Tạo nhánh làm việc mới từ `main` với tiền tố rõ ràng:
   ```bash
   git checkout -b feat/dynamic-spread-indicator
   # hoặc: git checkout -b fix/multimonitor-dpi-offset
   ```
2. **Quy chuẩn lập trình**:
   - Tuân thủ cú pháp C# 12 hiện đại và các nguyên tắc thiết kế của Microsoft .NET.
   - Các chữ ký P/Invoke phải được cấu hình kiểu dữ liệu an toàn (`CharSet = CharSet.Unicode`).
   - Duy trì chuẩn MVVM: Không lồng ghép xử lý giao diện trực tiếp vào Model; ưu tiên Command, Data Binding và Service Injection.
3. **Đa ngôn ngữ**:
   - Tuyệt đối không hardcode text hiển thị lên màn hình. Thêm chuỗi văn bản tương ứng vào cả hai file tài nguyên `Strings.resx` (mặc định) và `Strings.vi.resx` (Tiếng Việt).
4. **Kiểm thử tự động**:
   - Chạy toàn bộ unit test trước khi gửi Pull Request. Đảm bảo 100% kiểm thử thành công:
   ```bash
   dotnet test Crosshair.sln -c Release
   ```
5. **Tiêu chuẩn Pull Request**:
   - Đặt tiêu đề rõ ràng, mô tả chi tiết nội dung thay đổi và liên kết với Issue liên quan. Đính kèm ảnh chụp hoặc video minh họa nếu có thay đổi về giao diện.

---

## 10. Giấy phép mã nguồn (License)

CrossGOverlay được phát hành dưới điều khoản của giấy phép mã nguồn mở **[MIT License](LICENSE)**.

```
Copyright (c) 2026 CrossGOverlay Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.
```

---

## 11. Kế hoạch phát triển (Roadmap)

- [x] **v1.0.0 — Nền tảng cốt lõi**
  - [x] Cửa sổ overlay trong suốt WPF .NET 8 tăng tốc phần cứng.
  - [x] Trình phân tích và chuyển đổi mã chia sẻ tâm giữa CS2 và Valorant.
  - [x] Tự nhận diện game profile theo thời gian thực qua Windows Hook.
  - [x] Giao diện MongoDB LeafyGreen Dark mode cùng bản dịch song ngữ Việt - Anh.
- [ ] **v1.1.0 — Mở rộng engine đồ họa**
  - [ ] Hỗ trợ DirectComposition / Direct2D SwapChain để tối ưu hóa hiệu năng render.
  - [ ] Mô phỏng độ giãn tâm động (Dynamic Spread) theo bước di chuyển/thời gian.
  - [ ] Cho phép tải trực tiếp file đồ họa vector SVG hoặc ảnh làm tâm ngắm.
- [ ] **v1.2.0 — Hệ sinh thái & Cộng đồng**
  - [ ] Thư viện tâm ngắm cộng đồng trực tuyến tích hợp sẵn (tải về với 1 cú click).
  - [ ] Nhận diện định dạng mã tâm từ Apex Legends và Overwatch 2.
  - [ ] Đồng bộ hồ sơ cá nhân qua GitHub Gist hoặc đám mây cá nhân.

---

## Lời cảm ơn (Acknowledgements)

Trân trọng gửi lời cảm ơn đến **[HuuwxLoiwf (HuuLoii)](https://github.com/HuuwxLoiwf)** vì những đóng góp quan trọng trong việc thiết kế kiến trúc, kiểm thử hồ sơ game và xây dựng hệ sinh thái cho dự án.