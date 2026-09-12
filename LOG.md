# CrossGOverlay

Overlay tâm ngắm tuỳ chỉnh cho Windows, tương tự Crosshair X.
WPF · .NET 8 · MVVM · P/Invoke User32/Shcore.

> Tên assembly là `CrossGOverlay`, nhưng namespace và thư mục mã nguồn vẫn giữ `CrosshairOverlay` —
> đổi cả namespace không mang lại lợi ích gì mà lại đụng vào mọi file. Thư mục dữ liệu người dùng
> cũng giữ nguyên `%APPDATA%\CrosshairOverlay` để bản cập nhật không làm mất preset đã lưu.

Thiết kế và lý do đằng sau từng quyết định nằm ở [ARCHITECTURE.md](ARCHITECTURE.md).

---

## 1. Yêu cầu

| | |
|---|---|
| Hệ điều hành | Windows 10 1607+ hoặc Windows 11, kiến trúc x64 |
| Để **chạy** | [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Để **build** | .NET 8 SDK |

Ứng dụng chạy ở quyền `asInvoker` — **không cần và không nên chạy bằng quyền admin**.

## 2. Build

```bash
# Debug
dotnet build Crosshair.sln -c Debug

# Release
dotnet build Crosshair.sln -c Release
```

Kết quả: `src\CrosshairOverlay\bin\x64\<Config>\net8.0-windows\win-x64\CrossGOverlay.exe`

### Đóng gói thành MỘT file .exe portable

```bash
dotnet publish src/CrosshairOverlay/CrosshairOverlay.csproj -c Release
```

Không cần thêm tham số nào — mọi thứ đã khai báo sẵn trong `CrosshairOverlay.csproj`:

```xml
<PropertyGroup>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>

  <!-- Gói luôn .NET Runtime vào trong: máy người dùng KHÔNG cần cài gì thêm -->
  <SelfContained>true</SelfContained>

  <!-- Gộp tất cả thành một file .exe duy nhất -->
  <PublishSingleFile>true</PublishSingleFile>

  <!-- WPF kéo theo nhiều DLL native; thiếu cờ này chúng nằm rời cạnh file exe -->
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>

  <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>

  <!-- Nhúng symbol vào exe, không sinh file .pdb rời -->
  <DebugType>embedded</DebugType>
</PropertyGroup>
```

Kết quả: **một file duy nhất** `src\CrosshairOverlay\bin\Release\net8.0-windows\win-x64\publish\CrossGOverlay.exe` — khoảng **67 MB**, copy sang máy nào cũng chạy.

Muốn file nhẹ (~2 MB) và chấp nhận yêu cầu máy đích cài sẵn .NET 8 Desktop Runtime thì thêm `-p:SelfContained=false`.

## 3. Chạy

Chạy `CrossGOverlay.exe`. Lần đầu ứng dụng sẽ:

1. tạo `%APPDATA%\CrosshairOverlay\` và ghi 6 preset mẫu,
2. hiện overlay ở tâm màn hình,
3. mở cửa sổ Settings và đặt một icon vào khay hệ thống.

Trên Windows 11 icon khay mặc định nằm trong vùng **icon ẩn** (nút mũi tên cạnh đồng hồ). Kéo nó ra thanh taskbar nếu muốn thấy thường xuyên.

### Import / Export mã crosshair (Share Code)

**Nhập:** tab **Crosshair** → nút **Nhập mã** → dán mã vào rồi bấm **Tạo preset**. Ứng dụng tự nhận diện định dạng:

| Game | Dạng mã |
|---|---|
| Counter-Strike 2 | `CSGO-Gj9ry-3QQF3-T78kK-onMAf-6DR7B` |
| Valorant | `0;P;c;5;o;1;d;1;z;3;0t;4;0l;1;0o;2;0a;1` |

Hộp thoại hiện trước kết quả đọc được (hình dạng, màu, kích thước, chấm giữa, viền) để bạn kiểm tra trước khi tạo.

**Về độ chính xác:** cấu trúc — hình dạng, màu, chấm giữa, viền, kiểu chữ T — được chuyển đúng. Kích thước chỉ là **xấp xỉ**: CS2 dùng hệ đơn vị riêng và còn co giãn crosshair theo độ phân giải lẫn FOV, nên sau khi import bạn có thể cần chỉnh lại thanh **Tỉ lệ**. Valorant dùng đơn vị xấp xỉ pixel nên được chuyển 1:1.

Định dạng share code của CS2 không được Valve công bố; bộ giải mã trong ứng dụng theo mô tả cộng đồng và được kiểm chứng bằng test đối chiếu với các mã có công bố kèm giá trị convar (`tests\CrosshairOverlay.Tests\Cs2ShareCodeTests.cs`).

**Xuất:** chọn preset rồi bấm **Xuất mã**. Ứng dụng dựng chuỗi mã kiểu Valorant và chép thẳng vào clipboard:

```
0;P;c;8;u;00FF00FF;h;1;t;1;o;1;d;1;z;2;a;1;0b;1;0t;2;0l;10;0o;4;0a;1;1b;0
```

Màu luôn ghi ở dạng tuỳ chỉnh (`c;8` kèm `u;RRGGBBAA`) chứ không ép về bảng 8 màu dựng sẵn của Valorant — preset ở đây cho phép mọi màu.

**Giới hạn của định dạng:** chuỗi mã chỉ mô tả được chữ thập, chấm giữa và viền. Nếu preset có vòng tròn, khung vuông, nhánh chéo, góc xoay hay tỉ lệ khác 1, ứng dụng **cảnh báo trước** trong hộp thoại rằng những phần đó sẽ mất khi người khác nhập mã — thay vì đưa ra một mã trông có vẻ đúng.

### Phím tắt mặc định

| Tổ hợp | Hành động |
|---|---|
| `Alt + X` | Bật/tắt overlay |
| `Alt + ]` | Preset kế tiếp |
| `Alt + [` | Preset trước đó |
| `Alt + C` | Mở cửa sổ Settings |

Đổi trong tab **Phím tắt**, rồi bấm **Áp dụng** để đăng ký lại với Windows. Tổ hợp đã bị ứng dụng khác chiếm sẽ hiện lỗi ngay cạnh dòng đó.

**Nút chuột phụ** cũng gán được: bấm vào ô nhập rồi bấm Mouse 3 (nút giữa), Mouse 4 hoặc Mouse 5. Chuột trái và phải cố tình không cho gán — gán chúng làm phím tắt toàn cục sẽ khiến bạn không click được gì nữa.

Nút chuột đi qua **Raw Input** (`WM_INPUT` với cờ `RIDEV_INPUTSINK`) chứ không phải hook cấp thấp. Raw Input là kênh **chỉ đọc**: nó báo có nút nào vừa được bấm, và không có cách nào chặn, sửa hay giả lập sự kiện qua đó. `WH_MOUSE_LL` thì ngược lại — nó nằm trên đường đi của mọi sự kiện chuột toàn hệ thống và có quyền nuốt chúng, đúng mẫu hành vi anti-cheat cảnh giác.

## 4. Nơi lưu dữ liệu

| Đường dẫn | Nội dung |
|---|---|
| `%APPDATA%\CrosshairOverlay\settings.json` | Cấu hình chung, phím tắt, profile game |
| `%APPDATA%\CrosshairOverlay\presets\*.json` | Mỗi preset một file — import/export chỉ là copy file |
| `%LOCALAPPDATA%\CrosshairOverlay\logs\` | Log xoay vòng theo ngày, giữ 7 ngày |

Xoá thư mục `%APPDATA%\CrosshairOverlay` để đưa ứng dụng về trạng thái ban đầu.

## 5. Kiểm thử thực tế

Những mục dưới đây cần thao tác tay hoặc cần một game thật.

**Overlay**
- [ ] Crosshair nằm đúng tâm màn hình
- [ ] Click chuột xuyên qua crosshair xuống cửa sổ bên dưới
- [ ] Overlay không cướp focus: gõ phím vào cửa sổ khác vẫn bình thường
- [ ] Overlay không xuất hiện trong Alt-Tab

**Nhiều màn hình và DPI**
- [ ] Đổi chế độ sang "Ghim vào một màn hình cụ thể" và chọn màn hình khác
- [ ] Ở chế độ "Bám theo cửa sổ đang active", click sang cửa sổ ở màn hình khác
- [ ] Đổi Windows scaling (100% → 125% → 150%) và kiểm tra crosshair vẫn ở đúng tâm, nét vẫn sắc
- [ ] Đổi độ phân giải khi app đang chạy
- [ ] Rút/cắm màn hình phụ khi app đang chạy

**Editor**
- [ ] Kéo mọi slider, preview và overlay cùng đổi tức thì
- [ ] Color picker: swatch, slider RGBA, ô nhập hex
- [ ] Tạo / nhân bản / xoá / import / export preset
- [ ] Đổi hình dạng, kiểm tra cả 8 kiểu
- [ ] Tắt app rồi mở lại, cấu hình được giữ nguyên

**Phím tắt và khay hệ thống**
- [ ] Từng phím tắt hoạt động khi cửa sổ Settings đang đóng
- [ ] Phím tắt hoạt động khi **game đang giữ focus**
- [ ] Menu chuột phải trên icon khay: mở Settings, bật/tắt, thoát
- [ ] Chạy `CrossGOverlay.exe` lần thứ hai — instance cũ phải mở cửa sổ Settings, không có instance thứ hai

**Profile game — thử nhanh bằng Notepad (không cần mở game)**
- [ ] Tab **Game** → bấm **Thêm bia thử: notepad.exe**. Nút này tự chọn một preset KHÁC preset đang dùng để thấy rõ sự khác biệt
- [ ] Mở Notepad — crosshair phải đổi sang preset đã gán
- [ ] Đóng Notepad — crosshair phải quay về preset cũ
- [ ] Bảng **ĐANG NHẬN DIỆN** hiển thị đúng tên tiến trình, tiêu đề, chế độ hiển thị và profile khớp

**Profile game — với game thật**
- [ ] Mở game, Alt-Tab về Settings, bấm **Thêm từ cửa sổ vừa dùng** — tên tiến trình phải được điền sẵn
- [ ] Gán preset cho profile đó, quay lại game, preset tự đổi
- [ ] Thoát game, preset tự trả về lựa chọn thủ công trước đó

**Chế độ hiển thị của game**
- [ ] Windowed — crosshair hiện
- [ ] Borderless Fullscreen — crosshair hiện
- [ ] Exclusive Fullscreen — crosshair **không** hiện, app bật thông báo hướng dẫn chuyển sang Borderless

**Tần số quét cao**
- [ ] Ở 144Hz/240Hz, crosshair không nháy, không xé hình

## 6. An toàn với Anti-cheat

Ứng dụng hoạt động như một overlay desktop độc lập. Nó **chỉ** dùng các API cửa sổ và hiển thị công khai của Windows:

`GetForegroundWindow` · `GetWindowThreadProcessId` · `GetWindowRect` · `GetWindowTextW` ·
`QueryFullProcessImageNameW` (với `PROCESS_QUERY_LIMITED_INFORMATION`) ·
`SetWinEventHook` (cờ `WINEVENT_OUTOFCONTEXT`) · `RegisterHotKey` ·
`EnumDisplayMonitors` · `GetDpiForMonitor` · `SetWindowPos` · `SHQueryUserNotificationState`

Ứng dụng **không** làm những việc sau, ở bất kỳ đâu trong mã nguồn:

- inject DLL vào tiến trình khác
- đọc hoặc ghi bộ nhớ tiến trình khác (`ReadProcessMemory` / `WriteProcessMemory`)
- hook API của game
- cài hook bàn phím/chuột cấp thấp (`WH_KEYBOARD_LL` / `WH_MOUSE_LL`)
- mô phỏng thao tác nhập liệu (`SendInput`, `keybd_event`)
- liệt kê module của game, hay dùng driver kernel
- bất kỳ hình thức hỗ trợ gameplay nào: macro, auto-aim, trigger-bot

**Exclusive Fullscreen:** không overlay usermode nào vẽ được lên trên chế độ này. Ứng dụng nhận ra và hiển thị thông báo hướng dẫn chuyển game sang Borderless Windowed. Không có đường bypass nào được cài đặt.

Dù vậy, mỗi anti-cheat có chính sách riêng. Nếu bạn dùng trong giải đấu có luật về phần mềm bên thứ ba, hãy hỏi ban tổ chức trước.

### Quyền Administrator

Ứng dụng mặc định chạy quyền người dùng thường (`asInvoker`), và **không cần** quyền admin cho phần lớn chức năng. Đây là số đo thực tế trên Windows 11:

| Chức năng | Chạy quyền thường, khi game chạy quyền admin |
|---|---|
| Overlay vẽ đè lên cửa sổ game | **hoạt động** — thứ tự z-order không bị phân cấp toàn vẹn chi phối |
| Global hotkey (`RegisterHotKey`) | **hoạt động** — UIPI không chặn việc đăng ký hay nhận `WM_HOTKEY` |
| `GetForegroundWindow`, `GetWindowRect`, tiêu đề cửa sổ | **hoạt động** |
| Đọc TÊN FILE THỰC THI của game (`QueryFullProcessImageName`) | **BỊ CHẶN** — `ERROR_ACCESS_DENIED` |

Chỉ có ô cuối cùng là hạn chế thật. Đo trên máy thử: 177 trong 311 tiến trình từ chối `OpenProcess` khi gọi từ tiến trình quyền thường.

**Hệ quả:** với game chạy quyền admin, rule so khớp theo *tên tiến trình* sẽ không khớp. Ứng dụng phát hiện điều này và tự đề nghị tạo rule theo **tiêu đề cửa sổ** — cách này hoạt động bình thường ở quyền user. Bảng **ĐANG NHẬN DIỆN** trong tab Game hiển thị rõ khi tên tiến trình bị chặn.

**Nếu bạn vẫn muốn so khớp theo tên tiến trình** cho game chạy quyền admin, có hai cách:

1. Chuột phải `CrossGOverlay.exe` → **Run as administrator** (không cần build lại).
2. Sửa `app.manifest` rồi build lại:
   ```xml
   <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
   ```

Cân nhắc trước khi chọn cách 2 — đây là lý do nó **không** phải mặc định:

- hiện hộp thoại UAC **mỗi lần khởi động**;
- **phá vỡ "Khởi động cùng Windows"**: Windows không hiện UAC cho mục trong khoá `Run`, nên ứng dụng sẽ đơn giản là không tự chạy lúc đăng nhập;
- không kéo–thả file từ Explorer vào ứng dụng được nữa;
- một tiến trình đòi nâng quyền nói chung bị anti-cheat soi kỹ hơn, không phải ít hơn.

## 7. Xử lý sự cố

**Không thấy crosshair khi vào game** — gần như luôn là do game đang ở Exclusive Fullscreen. Đổi sang Borderless Windowed trong phần cài đặt đồ hoạ của game.

**Phím tắt không ăn** — tổ hợp đã bị ứng dụng khác chiếm. Tab Phím tắt sẽ ghi rõ lý do cạnh dòng bị lỗi; chọn tổ hợp khác rồi bấm Áp dụng.

**Crosshair lệch tâm** — thường do vừa đổi độ phân giải. Bấm "Quét lại màn hình" trong tab Chung.

**Ứng dụng không khởi động** — xem log mới nhất trong `%LOCALAPPDATA%\CrosshairOverlay\logs\`. Nếu `settings.json` hỏng, ứng dụng tự đổi tên nó thành `.corrupt` và chạy với cấu hình mặc định.

**Khởi động cùng Windows không bật được** — chính sách nhóm có thể chặn ghi khoá
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Ứng dụng sẽ báo ngay dưới ô đánh dấu.

## 8. Kiểm thử tự động

```bash
dotnet test Crosshair.sln
```

145 test, chạy dưới 1 giây. Bao phủ những phần thuần logic — nơi lỗi xảy ra âm thầm:

| Bộ test | Bảo vệ điều gì |
|---|---|
| `GameProfileMatcherTests` | Khớp tên tiến trình / tiêu đề / đường dẫn, thứ tự ưu tiên, rule đã tắt |
| `JsonSerializationTests` | Định dạng file preset — một hợp đồng công khai, đổi lặng lẽ là hỏng preset người khác đã chia sẻ |
| `CrosshairRendererTests` | Bất biến bám lưới pixel (vùng bao luôn là số CHẴN device pixel) và quyết định khử răng cưa |
| `PresetRepositoryTests` | Ghi nguyên tử, file hỏng bị bỏ qua, export/import cấp Id mới |
| `Cs2ShareCodeTests` | Bộ giải mã CS2, đối chiếu với mã thật có công bố giá trị convar |
| `ValorantCrosshairCodeTests` | Bộ mã hoá share code: vòng mã hoá–giải mã, dấu thập phân bất kể vùng miền, và cảnh báo khi preset có phần không biểu diễn được |
| `ValorantCrosshairCodeTests` | Bộ đọc mã Valorant, kể cả token lẻ ở đầu và các khối ADS/sniper |
| `KeyNamesTests` | Tên phím hiển thị cho người dùng (`]` chứ không phải `Oem6`) |
| `LocalizationTests` | Hai bản dịch khớp khoá và khớp chỗ trống `{0}` — thiếu một khoá sẽ không làm sập gì, chỉ hiện chuỗi lạ sau khi đã phát hành |

## 9. Ký số bản phát hành

File `.exe` chưa ký sẽ bị SmartScreen chặn khi người dùng tải về. Dùng script kèm theo:

```powershell
# Thử quy trình bằng chứng chỉ tự ký (chỉ hợp lệ trên máy bạn)
.\build\sign.ps1 -SelfSigned

# Ký bằng chứng chỉ thật
.\build\sign.ps1 -PfxPath C:\certs\company.pfx -PfxPassword (Read-Host -AsSecureString)
```

Cần nói rõ ký số giải quyết được gì:

| Loại chứng chỉ | Bỏ dòng "Nhà phát hành không xác định" | Hết cảnh báo SmartScreen |
|---|---|---|
| Tự ký | chỉ trên máy đã cài chứng chỉ gốc | **không** |
| OV (do CA cấp) | có | chỉ sau khi tích luỹ đủ uy tín qua lượt tải |
| EV | có | **có, ngay lượt tải đầu** |

Nói cách khác: **chỉ chứng chỉ EV mới loại bỏ cảnh báo SmartScreen ngay lập tức.** Chứng chỉ tự ký không có tác dụng gì với máy người khác — nó chỉ để kiểm tra quy trình ký chạy đúng.

Script luôn đóng dấu thời gian. Thiếu nó, chữ ký hết hiệu lực ngay khi chứng chỉ hết hạn; có nó thì những bản đã ký trước lúc hết hạn vẫn hợp lệ vĩnh viễn.

## 10. Ngôn ngữ giao diện

Hỗ trợ **tiếng Việt** và **tiếng Anh**. Chọn trong tab **Chung → Ngôn ngữ**; mặc định là *Theo hệ thống*, lấy theo ngôn ngữ hiển thị của Windows.

Đổi ngôn ngữ **có hiệu lực ngay**, không cần khởi động lại và cũng không cần đóng cửa sổ Settings.

### Thêm một ngôn ngữ mới

1. Chép `src\CrosshairOverlay\Resources\Strings.resx` thành `Strings.<mã>.resx` (vd `Strings.ja.resx`) rồi dịch phần `<value>`. Giữ nguyên mọi `name` và mọi chỗ trống `{0}`, `{1}`.
2. Thêm mã đó vào `SatelliteResourceLanguages` trong `CrosshairOverlay.csproj` — thiếu bước này, satellite assembly sẽ bị loại khỏi bản build.
3. Thêm một dòng vào `LanguageCatalog.All`, với tên viết bằng chính ngôn ngữ đó.
4. Chạy `dotnet test` — `LocalizationTests` chỉ so bản tiếng Anh với bản gốc, nên hãy mở rộng nó cho ngôn ngữ mới nếu muốn được bảo vệ tương đương.

Tiếng Việt là ngôn ngữ gốc (`NeutralResourcesLanguage`), nhúng thẳng vào assembly chính; các ngôn ngữ khác nằm ở satellite assembly trong thư mục con theo mã ngôn ngữ.

## 11. Tự động cập nhật

Tab **Chung** có ô **Tự kiểm tra cập nhật khi khởi động** và nút **Kiểm tra cập nhật** để chạy thủ công.

Cách hoạt động:

1. Gọi GitHub Releases API (`/releases/latest`), timeout **8 giây**. Chạy trên thread pool, không bao giờ chạm luồng giao diện.
2. So `tag_name` (vd `v1.4.0`) với phiên bản đang chạy. Cũ hơn hoặc bằng thì im lặng bỏ qua.
3. Có bản mới → hiện hộp thoại hỏi. Đồng ý thì tải file `.exe` về `%TEMP%`.
4. Sinh một script `.bat` rồi bàn giao: script chờ ứng dụng thoát hẳn → thay file `.exe` (thử lại tối đa 10 lần, phòng trường hợp phần mềm diệt virus còn giữ file) → mở lại ứng dụng → tự xoá chính nó.

**Mất mạng, DNS hỏng, máy chủ treo, hay chưa cấu hình kho phát hành đều chỉ dẫn tới "không có bản mới"** — ghi một dòng log mức Debug rồi thôi. Ứng dụng khởi động và vào game bình thường.

### Trỏ tới kho phát hành của bạn

Sửa hằng số trong `src\CrosshairOverlay\Services\Updates\UpdateService.cs`:

```csharp
private const string RepositoryPath = "your-account/CrosshairOverlay";
```

Bản phát hành phải đính kèm một file `.exe`; ứng dụng lấy asset `.exe` đầu tiên tìm thấy. Khi chưa đổi hằng số này, tính năng cập nhật luôn trả về "không có bản mới" — đúng như thiết kế.

## 12. Ngôn ngữ thiết kế giao diện

Toàn bộ giao diện theo **MongoDB LeafyGreen** (dark mode):

- nền `#001E2B`, thẻ `#1C2D38`, viền `#3D4F58`;
- màu nhấn là xanh lá đặc trưng `#00A35C` / `#00ED64`, chữ trên nền xanh dùng màu tối vì xanh LeafyGreen quá chói để đặt chữ trắng;
- font Segoe UI Variable, góc bo `4px` cho ô nhập và nút, `6px` cho thẻ;
- **không dùng `MessageBox` của Windows** — mọi thông báo và xác nhận đi qua hộp thoại riêng: lớp phủ làm tối đúng cửa sổ ứng dụng, thẻ có đổ bóng, nút chính nền xanh đặc và nút phụ dạng viền.

Toàn bộ màu và `ControlTemplate` nằm trong một file duy nhất: `src\CrosshairOverlay\Resources\Theme.xaml`.

## 13. Logo và icon

Tài nguyên nằm ở `src\CrosshairOverlay\images\`:

| File | Dùng cho |
|---|---|
| `app.ico` | Icon ứng dụng — ICO thật, 7 khung từ 16×16 đến 256×256 |
| `app.source.png` | Ảnh gốc, dùng để dựng lại `app.ico` |
| `BrandingLogo.ico` | Logo trong góc thương hiệu ở đầu giao diện |

Khai báo trong `.csproj`:

```xml
<ApplicationIcon>images\app.ico</ApplicationIcon>
...
<ItemGroup>
  <Resource Include="images\app.ico" />
  <Resource Include="images\BrandingLogo.ico" />
</ItemGroup>
```

Build action **`Resource`** nhúng file thẳng vào assembly, nên truy cập được qua `pack://application:,,,/images/...` và **không mất khi publish một file**. Dùng `Content` thì file nằm rời cạnh `.exe` và bản portable sẽ thiếu icon.

Icon được dùng ở bốn nơi:

| Vị trí | Cách lấy |
|---|---|
| File `.exe` (Explorer, Alt-Tab) | `<ApplicationIcon>` |
| Cửa sổ và nút trên taskbar | `Icon="pack://application:,,,/images/app.ico"` |
| Khay hệ thống | `TrayIconFactory` nạp `app.ico`, và dựng biến thể **xám** khi overlay đang tắt |
| Góc thương hiệu | `<Image Source="pack://application:,,,/images/BrandingLogo.ico"/>` |

### Đổi logo

File `.ico` bạn cung cấp **phải là ICO container thật**, không phải PNG đổi đuôi. PNG đổi đuôi sẽ làm build hỏng với `CS7065: Icon stream is not in the expected format`, và `System.Drawing.Icon` cũng không đọc được.

Đưa ảnh PNG vuông vào rồi chạy:

```powershell
.\build\make-icon.ps1 -Source src\CrosshairOverlay\images\app.source.png `
                      -Destination src\CrosshairOverlay\images\app.ico
```

Script dựng ICO đa kích thước với các khung DIB 32-bit — định dạng mọi phiên bản Windows và mọi trình biên dịch đều chấp nhận.

`BrandingLogo.ico` thì không cần chuyển: nó chỉ được dùng làm `<Image>`, và bộ giải mã ảnh của WPF nhận diện định dạng theo nội dung file chứ không theo phần mở rộng.
