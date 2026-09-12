# CrosshairOverlay — Kiến trúc (Phase 1)

Ứng dụng overlay crosshair cho Windows, tương tự Crosshair X.
WPF · .NET 8 · MVVM · P/Invoke User32/Shcore.

---

## 1. Cấu trúc thư mục

```
D:\Crosshair
├─ Crosshair.sln
├─ ARCHITECTURE.md
├─ .gitignore
└─ src\CrosshairOverlay\
   ├─ CrosshairOverlay.csproj
   ├─ app.manifest                    # asInvoker + PerMonitorV2 DPI
   ├─ App.xaml / App.xaml.cs          # trình tự khởi động                    ✅ [P6]
   ├─ Composition\
   │  └─ ServiceRegistration.cs       # đồ thị phụ thuộc DI                   ✅ [P6]
   │
   ├─ Core\                           # ✅ Phase 1 — không phụ thuộc UI
   │  ├─ Models\
   │  │  ├─ Enums.cs                  # CrosshairShape, MonitorSelectionMode, HotkeyAction…
   │  │  ├─ CrosshairParts.cs         # Lines, CenterDot, Outline, Ring, CustomImage
   │  │  ├─ CrosshairProfile.cs       # preset — đơn vị lưu/import/export
   │  │  ├─ AppSettings.cs
   │  │  ├─ GameProfile.cs
   │  │  ├─ HotkeyBinding.cs
   │  │  ├─ MonitorInfo.cs
   │  │  ├─ ForegroundWindowInfo.cs
   │  │  ├─ CrosshairRenderOptions.cs
   │  │  ├─ ProfileNotifications.cs    # nghe PropertyChanged của preset + khối con  [P4]
   │  │  └─ KeyNames.cs                # tên phím người đọc được                     [P5]
   │  └─ Abstractions\
   │     ├─ IOverlayController.cs
   │     ├─ ICrosshairRenderer.cs
   │     ├─ IMonitorService.cs
   │     ├─ IHotkeyService.cs
   │     ├─ IPresetRepository.cs
   │     ├─ IAppSettingsService.cs
   │     ├─ IForegroundWindowWatcher.cs
   │     ├─ IGameProfileMatcher.cs     # + IProfileAutoSwitcher
   │     └─ IPlatformServices.cs       # IAppPathProvider, IStartupService,
   │                                   #   ITrayIconController, IDialogService,
   │                                   #   ISingleInstanceGuard
   │
   ├─ Interop\                        # ✅ Phase 2 — toàn bộ P/Invoke gom ở đây
   │  ├─ NativeMethods.cs             # user32 + shcore: cửa sổ, màn hình, DPI, icon
   │  ├─ NativeMethods.Process.cs     # hotkey, WinEvent, tiến trình, shell   ✅ [P5]
   │  ├─ NativeStructs.cs             # RECT, POINT, MONITORINFOEXW
   │  └─ WindowStyles.cs              # Win32Constants
   │
   ├─ Services\
   │  ├─ Overlay\   OverlayController.cs, OverlayWindow.cs, CrosshairVisualHost.cs  ✅ [P2]
   │  ├─ Display\   MonitorService.cs                                               ✅ [P2]
   │  ├─ Rendering\ CrosshairRenderer.cs, RenderPlan.cs                             ✅ [P3]
   │  ├─ Storage\   AppPathProvider.cs, AtomicFile.cs, AppJson.cs,
   │  │             JsonColorConverter.cs, PresetRepository.cs,
   │  │             AppSettingsService.cs, DefaultPresets.cs                        ✅ [P3]
   │  ├─ Presets\   PresetLibrary.cs                                               ✅ [P4]
   │  ├─ Input\     HotkeyService.cs, HotkeyMessageWindow.cs                      ✅ [P5]
   │  ├─ Process\   ForegroundWindowWatcher.cs, GameProfileMatcher.cs,
   │  │             ProfileAutoSwitcher.cs, FullscreenDetector.cs                  ✅ [P5]
   │  ├─ System\    DialogService.cs                                               ✅ [P4]
   │  │             TrayIconController.cs, TrayIconFactory.cs                      ✅ [P5]
   │  │             StartupService.cs, SingleInstanceGuard.cs                      ✅ [P6]
   │  └─ Logging\   AppLogging.cs                                                  ✅ [P6]
   │
   ├─ ViewModels\   SettingsViewModel.cs, CrosshairEditorViewModel.cs,
   │                GeneralSettingsViewModel.cs                                    ✅ [P4]
   │                HotkeysViewModel.cs, GameProfilesViewModel.cs                  ✅ [P5]
   ├─ Views\        SettingsWindow.xaml (4 tab)                                    ✅ [P5]
   ├─ Controls\     CrosshairPreview.cs, ColorPickerButton.xaml, SliderField.xaml  ✅ [P4]
   │                HotkeyInputBox.cs                                              ✅ [P5]
   ├─ Localization\ TranslationSource.cs, LocExtension.cs, LocalizedOption.cs,
   │                LanguageCatalog.cs                                             ✅
   ├─ Converters\   CommonConverters.cs                                            ✅ [P4]
   └─ Resources\    Theme.xaml                                                     ✅ [P4]
                    Strings.resx (vi, ngôn ngữ gốc), Strings.en.resx               ✅
                    (icon khay hệ thống được vẽ bằng GDI+ lúc chạy, không nhúng .ico)
```

## 2. Dependencies

| Package | Ver | Dùng để |
|---|---|---|
| `CommunityToolkit.Mvvm` | 8.4.0 | `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]` |
| `Microsoft.Extensions.DependencyInjection` | 8.0.1 | DI container trong `App.xaml.cs` |
| `Microsoft.Extensions.Logging` | 8.0.1 | `ILogger<T>` khắp các service |
| `Serilog.Extensions.Logging` | 8.0.0 | Provider cho abstraction trên |
| `Serilog.Sinks.File` | 6.0.0 | Log xoay vòng theo ngày |
| `H.NotifyIcon.Wpf` | 2.1.3 | Tray icon thuần WPF, không kéo theo WinForms |

Không dùng thư viện bên thứ ba nào cho color picker, hotkey hay window API — tự viết bằng P/Invoke để kiểm soát hoàn toàn hành vi.

## 3. Luồng dữ liệu

```
        ┌──────────────────── App.xaml.cs (composition root) ───────────────────┐
        │                                                                        │
  AppSettingsService ──┐                                    ┌── PresetRepository │
   (settings.json)     │                                    │   (presets\*.json) │
                       ▼                                    ▼                    │
                    ShellViewModel ◄──────────────► CrosshairEditorViewModel      │
                       │    ▲                              │                      │
                       │    │ hotkey / tray / auto-switch  │ bind 2 chiều         │
                       ▼    │                              ▼                      │
                 OverlayController ◄──────────────── CrosshairProfile             │
                       │                              (ObservableObject)          │
                       │ PropertyChanged → Invalidate        │                    │
                       ▼                                     ▼                    │
              CrosshairRenderer.Build() ──► Drawing (frozen, cached)              │
                       │                                     │                    │
                       ▼                                     ▼                    │
                 OverlayWindow                       CrosshairPreview             │
              (layered, click-through)               (khung preview Settings)     │
```

Điểm mấu chốt: **preset là `ObservableObject`**. Editor bind hai chiều trực tiếp vào nó, overlay lắng nghe `PropertyChanged` và chỉ dựng lại `Drawing` khi có thay đổi thật — thoả yêu cầu "real-time preview" và "chỉ redraw khi cấu hình đổi" mà không cần lớp mapping trung gian. Overlay và preview dùng chung một renderer nên hai bên không bao giờ lệch nhau.

## 4. Quyết định thiết kế và lý do

**Overlay dùng `AllowsTransparency=true`, không dùng thủ thuật DWM.**
Bản nháp Phase 1 định dùng `DwmExtendFrameIntoClientArea` để giữ GPU compositing. Trên Windows 10/11 trick "sheet of glass" đó không còn đáng tin — DWM trả về nền đen trong nhiều cấu hình. `AllowsTransparency` buộc WPF render cửa sổ bằng software và composite qua `UpdateLayeredWindow`, nhưng cửa sổ overlay chỉ ~45×45 px và chỉ vẽ lại khi preset đổi, nên chi phí không đáng kể. Lập luận hiệu năng chỉ đúng với overlay phủ toàn màn hình — kiến trúc này cố tình không làm vậy.

**`WS_EX_NOACTIVATE` + `ShowActivated=false` cho yêu cầu không cướp focus.**
`WS_EX_TRANSPARENT` cho click-through, `WS_EX_TOOLWINDOW` để overlay không xuất hiện trong Alt-Tab, và `WS_EX_APPWINDOW` bị xoá. Mọi `SetWindowPos` đều mang cờ `SWP_NOACTIVATE`.

**Khẳng định lại topmost theo chu kỳ 1.5 s.**
Game vào fullscreen hoặc app khác bật topmost có thể đẩy overlay xuống dưới. `DispatcherTimer` gọi `SetWindowPos(HWND_TOPMOST, SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE)` — thao tác thụ động trên cửa sổ của chính mình, không đụng gì tới tiến trình game.

**Kích thước cửa sổ vừa đủ crosshair, không phủ toàn màn hình.**
`ICrosshairRenderer.Measure()` trả về bounding box; overlay chỉ tạo surface bằng đúng kích thước đó rồi đặt tâm nó vào tâm màn hình. Vùng cần composite nhỏ đi hàng nghìn lần.

**Toạ độ: model dùng DIP, đặt cửa sổ dùng physical pixel.**
`MonitorInfo` mang cả `Bounds` (physical) lẫn `DpiScaleX/Y`. Đặt cửa sổ luôn qua `SetWindowPos` với physical pixel — không đụng tới `Window.Left/Top` của WPF vốn diễn giải theo DPI của màn hình chính và sẽ sai ở multi-monitor DPI hỗn hợp.

**`RegisterHotKey` thay vì low-level keyboard hook.**
Hook `WH_KEYBOARD_LL` nhìn thấy mọi phím gõ toàn hệ thống — đúng mẫu hành vi keylogger mà anti-cheat cảnh giác. `RegisterHotKey` chỉ nhận đúng tổ hợp đã đăng ký và vẫn hoạt động khi game giữ focus.

**`SetWinEventHook` với `WINEVENT_OUTOFCONTEXT` để nhận diện game.**
Cờ này khiến callback chạy trong tiến trình của app này; không DLL nào được nạp vào game. Rẻ hơn nhiều so với polling `GetForegroundWindow` theo timer.

**Mỗi preset một file JSON.**
Import/export trở thành thao tác copy file, và một preset hỏng không kéo sập toàn bộ thư viện. Ghi theo kiểu ghi-tạm-rồi-`File.Move` đè, nên mất điện giữa chừng không để lại file cụt.

**Renderer tính toán bằng DEVICE PIXEL, không phải DIP.**
Một nét dày lẻ pixel chỉ sắc khi tâm nét nằm giữa pixel; nét dày chẵn chỉ sắc khi tâm nét nằm trên biên pixel. `RenderPlan` quy mọi kích thước về số nguyên device pixel rồi cộng nửa pixel khi cần (`AlignFor`). Viền được quy về số nguyên pixel *trước*, nên bề dày tổng (lõi + 2×viền) luôn cùng tính chẵn/lẻ với lõi — hai lượt vẽ dùng chung một giá trị căn và luôn đồng tâm. Chấm giữa nhỏ hơn 3 px được vẽ bằng hình vuông: ở cỡ đó mắt không phân biệt được với hình tròn, nhưng hình vuông rasterize thành pixel đặc còn hình tròn bị nhoè.

**`Measure()` trả về kích thước ứng với số CHẴN device pixel.**
Nhờ vậy tâm cửa sổ rơi đúng biên pixel, và `OverlayController` dùng `Math.Round` chứ không phải `Math.Ceiling` khi quy sang physical pixel — làm tròn lên sẽ cộng thừa 1 px do sai số dấu phẩy động và đẩy crosshair lệch nửa pixel.

**`IPresetLibrary` giữ instance, `IPresetRepository` chỉ đọc/ghi đĩa.**
Repository trả về instance MỚI sau mỗi lần đọc. Nếu editor bind vào một instance còn overlay giữ instance khác thì kéo slider sẽ không ra preview. Library là nguồn chân lý duy nhất về các instance đang sống, đồng thời tự lưu xuống đĩa với debounce 600 ms khi người dùng chỉnh.

**Composition root nối `library.ActiveChanged` → overlay, không phải ViewModel.**
Preset có thể đổi khi cửa sổ Settings đang đóng (hotkey, tray, auto-switch ở Phase 5) — lúc đó không có ViewModel nào sống để chuyển tiếp sự kiện. ViewModel chỉ đồng bộ phần UI.

**Preview render ra bitmap ở độ phân giải thiết bị rồi mới phóng bằng `NearestNeighbor`.**
Không phóng hình vector. Người dùng cần nhìn thấy đúng từng pixel thật, kể cả kết quả bám lưới pixel của renderer; scale vector sẽ làm mọi nét mượt đẹp và che mất chính thứ cần kiểm tra. Preview dùng chung `ICrosshairRenderer` với overlay nên hai bên không thể lệch nhau.

**`SHQueryUserNotificationState` để phát hiện Exclusive Fullscreen.**
Đây là API công khai duy nhất cho biết có ứng dụng Direct3D nào đang chạy toàn màn hình độc quyền (`QUNS_RUNNING_D3D_FULL_SCREEN`). Chỉ đọc trạng thái shell, không đụng tới tiến trình game. Vì là tín hiệu toàn hệ thống chứ không gắn với một cửa sổ cụ thể nên kết quả mang tên `LikelyExclusive` — app hiển thị thông báo hướng dẫn chuyển sang Borderless, và không cài đặt bất kỳ đường bypass nào.

**Tray icon gán thẳng vào `TaskbarIcon.Icon`, không qua `IconSource`.**
H.NotifyIcon chỉ chuyển đổi được vài kiểu `ImageSource` nhất định; với kiểu khác nó ném `NotImplementedException` bên trong một continuation async — tức là giết tiến trình, không hộp thoại, không log. Dựng `System.Drawing.Icon` bằng GDI+ rồi gán thẳng thì bỏ qua hoàn toàn tầng đó. `TrayIconFactory` nằm riêng một file vì gần như mọi kiểu của `System.Drawing` đều trùng tên với kiểu WPF.

**Bộ bắt lỗi toàn cục được kéo lên từ Phase 6.**
Sự cố trên biến mất không để lại dấu vết nào ngoài Event Log của Windows. `DispatcherUnhandledException`, `TaskScheduler.UnobservedTaskException` và `AppDomain.UnhandledException` được cài ngay trong `OnStartup`, trước mọi khởi tạo khác.

**Đồ thị phụ thuộc nằm ở `ServiceRegistration`, trình tự khởi động nằm ở `App`.**
Hai thứ này thay đổi vì lý do khác nhau: thêm một service là sửa đồ thị, còn đổi thứ tự khởi tạo là sửa trình tự. `ValidateOnBuild = true` khiến một đăng ký thiếu bị phát hiện ngay lúc khởi động chứ không phải lúc người dùng bấm vào tính năng đó.

**Chống chạy hai instance bằng mutex + named event.**
Mutex chỉ trả lời được "đã có instance nào chưa". Việc đánh thức instance cũ cần một kênh riêng; `EventWaitHandle` có tên nhẹ hơn nhiều so với dựng pipe hay socket. Hai instance cùng chạy sẽ có hai overlay chồng nhau và tranh nhau đăng ký cùng bộ hotkey — instance thứ hai luôn thua.

**Khởi động cùng Windows: registry là nguồn chân lý, không phải `settings.json`.**
Người dùng có thể tắt mục khởi động qua Task Manager mà ứng dụng không hề hay biết. Ghi vào `HKCU` (không phải `HKLM`) nên không cần quyền admin, khớp với việc app chạy ở mức `asInvoker`.

**Mức log điều khiển qua `LoggingLevelSwitch`, không cố định lúc tạo.**
Bài toán con-gà-quả-trứng: log phải sẵn sàng trước khi đọc `settings.json` (bản thân việc đọc file đó cũng ghi log), nhưng mức log lại nằm trong chính file ấy.

**File log ghi kèm BOM UTF-8.**
Không có BOM thì Notepad cũ và PowerShell 5.1 đọc file theo bảng mã ANSI và mọi thông điệp tiếng Việt thành ký tự rác — đúng lúc người dùng mở log ra tìm nguyên nhân sự cố.

**GC workstation + background, `SustainedLowLatency`.**
Một đợt thu gom gen2 gây khựng sẽ thành micro-stutter nhìn thấy được trong game. ServerGC thì hoàn toàn sai cho ứng dụng desktop — nó tạo một heap và một luồng cho mỗi nhân CPU. Overlay chỉ cấp phát khi người dùng đổi preset, nên đánh đổi heap lớn hơn đôi chút lấy độ trễ thấp là đúng.

**Tắt khử răng cưa có ĐIỀU KIỆN, không bật cứng.**
`EdgeMode.Aliased` cho hình toàn nét thẳng đứng/ngang đã bám lưới pixel thì cho tâm ngắm sắc tuyệt đối. Nhưng bật cờ đó cho vòng tròn, nhánh chéo hay hình đã xoay thì đường cong và đường xiên bị răng cưa bậc thang — xấu hơn hẳn. `ICrosshairRenderer.PrefersAliasedEdges` quyết định theo nội dung preset, và khung preview dùng đúng quyết định đó để không nói dối về độ sắc.

**Không dùng `WINEVENT_SKIPOWNPROCESS`.**
Bỏ qua cửa sổ của chính mình nghe có vẻ gọn, nhưng khi người dùng đóng game rồi focus quay về cửa sổ Settings thì không có sự kiện nào bắn ra, và profile game bị kẹt lại mãi — lỗi này lộ ra đúng lúc thử tính năng mock Notepad. Nay watcher nhận mọi sự kiện; `ProfileAutoSwitcher` tự phân biệt: focus về cửa sổ của app thì GIỮ profile game (người dùng cần thấy crosshair của game khi đang chỉnh), trừ khi tiến trình game đã thoát — kiểm tra một lần bằng `Process.GetProcessById`, không polling.

**ViewModel của cửa sổ Settings phải `Dispose`.**
Chúng đăng ký sự kiện lên các service singleton (thư viện preset, màn hình, theo dõi foreground), còn bản thân thì được tạo mới mỗi lần mở cửa sổ. Không gỡ đăng ký thì mỗi lần mở lại là một ViewModel nữa bị giữ sống vĩnh viễn. `SettingsWindow.Closed` gọi `Dispose` trên `DataContext`.

**`{loc:Loc Key}` trả về một `Binding`, không phải chuỗi.**
Đó là lý do đổi ngôn ngữ có hiệu lực ngay trên cửa sổ đang mở. XAML bind tới bộ chỉ mục `[Key]` của singleton `TranslationSource`; khi ngôn ngữ đổi, phát `PropertyChanged` với tên rỗng là WPF làm mới toàn bộ binding đang sống. Nếu `ProvideValue` trả chuỗi tĩnh, mọi nhãn sẽ đứng im cho tới khi cửa sổ được dựng lại.

**Mục ComboBox tự phát `PropertyChanged`, không dựng lại cả danh sách.**
Cách làm ngây thơ — thay `ItemsSource` khi đổi ngôn ngữ — có hai vấn đề, và lần thử đầu đã dính đúng cả hai: ComboBox mất lựa chọn hiện tại, và mục đang chọn vẫn hiện nhãn cũ vì WPF không có lý do gì gọi lại `ToString()`. `LocalizedOption<T>` giữ nguyên danh sách và chỉ báo `Label` đã đổi, dùng kèm `DisplayMemberPath="Label"`.

**Tiếng Việt là `NeutralResourcesLanguage`, tiếng Anh nằm ở satellite assembly.**
Ngôn ngữ gốc nhúng thẳng vào assembly chính nên luôn có mặt kể cả khi satellite bị loại khỏi bản build. `SatelliteResourceLanguages` phải liệt kê mọi mã ngôn ngữ muốn kèm theo — quên là satellite bị cắt và ngôn ngữ đó âm thầm quay về bản gốc.

**Không dùng `MessageBox` của Windows.**
Hộp thoại riêng là một `Window` trong suốt đặt TRÙNG KHÍT lên cửa sổ chủ bằng `SetWindowPos` với toạ độ physical pixel lấy từ HWND — nhờ vậy lớp phủ tối che đúng ứng dụng chứ không che cả màn hình nền. Dùng `Window.Left/Top` sẽ sai khi cửa sổ chủ đang phóng to hoặc nằm trên màn hình DPI khác.

**Nút chuột phụ đi qua Raw Input, không qua `WH_MOUSE_LL`.**
`RegisterHotKey` chỉ nhận phím bàn phím, nên nút chuột cần đường riêng. Raw Input (`WM_INPUT` + `RIDEV_INPUTSINK`) là kênh CHỈ ĐỌC — không chặn, không sửa, không giả lập được sự kiện nào. Hook cấp thấp thì nằm trên đường đi của mọi sự kiện chuột toàn hệ thống và có quyền nuốt chúng. Struct `RAWMOUSE` phải khai báo trường đệm tường minh: bố cục gốc có union 4 byte ngay sau `usFlags`, thiếu 2 byte đệm là mọi cờ nút bấm đọc ra thành rác.

**`SliderField` đồng bộ `Text` và `Value` bằng tay, không bind thẳng ô nhập vào số.**
Bind trực tiếp kèm định dạng số sẽ viết đè lên thứ người dùng đang gõ — gõ "0." biến ngay thành "0" — và đẩy con trỏ về cuối sau mỗi phím. Một cờ `_syncing` chặn vòng lặp giữa hai chiều, và `CoerceValueCallback` kẹp giá trị vào khoảng cho phép kể cả khi người dùng gõ số ngoài khoảng.

**Tự cập nhật bằng script `.bat` rời.**
Tiến trình không thể ghi đè lên file `.exe` đang chạy của chính nó. Script chờ ứng dụng thoát hẳn, thử thay file tối đa 10 lần (phần mềm diệt virus có thể còn giữ file), mở lại ứng dụng rồi tự xoá bằng thủ thuật `(goto) 2>nul & del "%~f0"` — cmd đọc file theo từng dòng nên dòng đó xoá được chính nó. Toàn bộ luồng kiểm tra chạy trên thread pool với timeout 8 giây: mất mạng phải dẫn tới "không có bản mới", không bao giờ tới treo giao diện.

**Icon nhúng bằng build action `Resource`, không phải `Content`.**
`Content` để file nằm rời cạnh `.exe`, nên bản publish một file sẽ thiếu icon. `Resource` nhúng thẳng vào assembly, pack URI hoạt động cả trong bundle. Và file `.ico` phải là ICO container thật — PNG đổi đuôi làm `<ApplicationIcon>` hỏng build với `CS7065` và `System.Drawing.Icon` cũng không đọc được; `build\make-icon.ps1` dựng ICO đa kích thước đúng chuẩn từ PNG.

**Tên assembly là `CrossGOverlay`, namespace giữ `CrosshairOverlay`.**
Đổi namespace sẽ đụng vào mọi file mà không đem lại lợi ích nào. Quan trọng hơn: tên manifest của resource ngôn ngữ suy ra từ `RootNamespace` chứ không phải `AssemblyName`, nên `ResourceManager("CrosshairOverlay.Resources.Strings")` vẫn đúng. Thư mục dữ liệu người dùng cũng giữ nguyên, để bản cập nhật không làm mất preset đã lưu.

**Không dùng WPF Effect, và `Freeze()` mọi Freezable.**
`DropShadowEffect`/`BlurEffect` trên cửa sổ có `AllowsTransparency=true` buộc render bằng CPU. Viền crosshair được tạo bằng cách vẽ chồng nét dày hơn ở lượt dưới, không dùng effect. Mọi `SolidColorBrush`, `Pen`, `Drawing` đều được `Freeze()` ngay sau khi dựng để WPF khỏi theo dõi thay đổi và để dùng lại được trên nhiều thread.

## 5. Ranh giới an toàn với Anti-cheat

Được phép — chỉ đọc metadata cửa sổ/tiến trình công khai:
`GetForegroundWindow`, `GetWindowThreadProcessId`, `GetWindowRect`, `GetWindowTextW`,
`QueryFullProcessImageNameW` (`PROCESS_QUERY_LIMITED_INFORMATION`),
`SetWinEventHook(WINEVENT_OUTOFCONTEXT)`, `RegisterHotKey`,
`EnumDisplayMonitors`, `GetDpiForMonitor`, `SetWindowPos`, `SetLayeredWindowAttributes`.

Tuyệt đối không: inject DLL · `ReadProcessMemory`/`WriteProcessMemory` · hook API của game ·
`WH_KEYBOARD_LL`/`WH_MOUSE_LL` · mô phỏng input (`SendInput`) · `PROCESS_VM_READ` ·
enumerate module của game · driver kernel · macro/auto-aim dưới mọi hình thức.

Exclusive Fullscreen: không có overlay usermode nào vẽ được lên trên chế độ này.
`FullscreenDetector` phát hiện và app **hiển thị thông báo** hướng dẫn người dùng chuyển game
sang Borderless Windowed. Không có đường bypass nào được cài đặt.

## 6. Lộ trình

| Phase | Nội dung | Trạng thái |
|---|---|---|
| 1 | Kiến trúc, project, Core models + interfaces | ✅ xong |
| 2 | OverlayWindow, Interop, MonitorService, DPI | ✅ xong |
| 3 | CrosshairRenderer, lưu/đọc JSON | ✅ xong |
| 4 | Settings UI, MVVM, real-time preview | ✅ xong |
| 5 | Hotkeys, tray, process detection | ✅ xong |
| 6 | Start with Windows, error handling, logging, build/test | ✅ xong |
