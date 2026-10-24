# CrossGOverlay

> English documentation

[🇻🇳 Tiếng Việt](README.vi.md)

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

### Đóng gói bản phát hành (thư mục + file .zip)

```powershell
.\build\publish.ps1
```

Script publish **sạch** (xoá `obj\Release`, `bin\Release` trước), rồi nén cả thư mục thành
`artifacts\CrossGOverlay-<phiên bản>-win-x64.zip` — khoảng **63 MB**, bên trong là một thư mục cùng tên
(~256 file, ~149 MB khi giải nén). Người dùng giải nén ra đâu cũng chạy được, máy **không** cần cài .NET.
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

**Vì sao không dùng một file .exe** — đo trên cùng mã nguồn, dựng sạch từng biến thể, chạy xen kẽ nhiều vòng:

| Kiểu đóng gói | .NET nạp xong | Cửa sổ có hình đầu tiên | Tải về |
|---|---|---|---|
| Một file .exe có nén (cách cũ) | ~960 ms | 1407–1921 ms | 69 MB |
| **Thư mục (hiện tại)** | **~110–175 ms** | **~650–760 ms** | **63 MB (.zip)** |

Chi phí nằm ở chính gói một file (nén hay không, để DLL native ra ngoài hay không đều như nhau).

**Lưu ý khi đo tốc độ:** lần mở đầu tiên ngay sau khi publish chậm bất thường (5–11 giây) vì Windows Defender
quét file mới; từ lần thứ hai trở đi mới là con số thật. Luôn publish sạch trước khi đo — publish tăng dần sau
nhiều lần sửa-hoàn-tác nhanh từng đóng gói nhầm mã cũ mà không báo lỗi gì.

Muốn gói nhẹ (~2 MB) và chấp nhận yêu cầu máy đích cài sẵn .NET 8 Desktop Runtime thì thêm `-p:SelfContained=false`.

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
`EnumDisplayMonitors` · `GetDpiForMonitor` · `SetWindowPos` · `SetWindowLongPtr` ·
`SetWindowDisplayAffinity` · `SHQueryUserNotificationState`

Ứng dụng **không** làm những việc sau, ở bất kỳ đâu trong mã nguồn:

- inject DLL vào tiến trình khác
- đọc hoặc ghi bộ nhớ tiến trình khác (`ReadProcessMemory` / `WriteProcessMemory`)
- hook API của game
- cài hook chuột cấp thấp (`WH_MOUSE_LL`)
- mô phỏng thao tác nhập liệu (`SendInput`, `keybd_event`)
- liệt kê module của game, hay dùng driver kernel
- bất kỳ hình thức hỗ trợ gameplay nào: macro, auto-aim, trigger-bot

**Exclusive Fullscreen:** không overlay usermode nào vẽ được lên trên chế độ này. Ứng dụng nhận ra và hiển thị thông báo hướng dẫn chuyển game sang Borderless Windowed. Không có đường bypass nào được cài đặt.

Dù vậy, mỗi anti-cheat có chính sách riêng. Nếu bạn dùng trong giải đấu có luật về phần mềm bên thứ ba, hãy hỏi ban tổ chức trước.
Khi người dùng gán preset cho phím vũ khí, ứng dụng tạm cài `WH_KEYBOARD_LL` ở tiến trình của chính nó để quan sát phím; hook được gỡ khi rời game khớp profile. Sự kiện luôn được chuyển tiếp cho game. Tính năng này có thể bị một số anti-cheat hạn chế; không có cam kết tương thích hay độ trễ dưới 1 ms. `anticheat_audit.log` chỉ là nhật ký chẩn đoán các lần chuyển trạng thái API cửa sổ đã chọn, không phải bằng chứng chứng nhận an toàn.

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

Bản phát hành phải đính kèm **file `.zip`** do `build\publish.ps1` tạo ra. Ứng dụng chọn asset `.zip`, ưu tiên tên có chữ `win-x64`; asset `.exe` bị bỏ qua có chủ đích — chép một file `.exe` đè lên bản dạng thư mục chỉ thay được file khởi chạy, còn mã ứng dụng (`CrossGOverlay.dll`) vẫn là bản cũ. Khi cập nhật, ứng dụng tải và giải nén gói **trước khi thoát** (gói hỏng thì báo lỗi ngay), rồi một script chờ app thoát và dùng `robocopy /E` chép đè thư mục cài đặt — không dùng `/MIR`, nên file người dùng để trong thư mục đó không bị xoá — rồi mở lại app. Khi chưa đổi hằng số này, tính năng cập nhật luôn trả về "không có bản mới" — đúng như thiết kế.

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

## 14. Nhật ký thay đổi

### 2026-10-22 — Cải tiến trước phát hành

- **Lưu trữ và khởi động:** Ghi JSON bằng file tạm, thay thế nguyên tử và đọc bản `.bak` khi file chính hỏng; tách trình tự khởi động và dọn dẹp tập trung; khóa single instance để lần mở thứ hai đánh thức cửa sổ đang chạy.
- **Overlay và nhận diện game:** Kết hợp WinEvent foreground với cơ chế kiểm tra dự phòng, đưa cập nhật giao diện về Dispatcher, khẳng định lại topmost khi chuyển vào game và canh tâm theo tọa độ pixel vật lý trên màn hình có DPI khác nhau. Thông báo khi game có thể đang ở Exclusive Fullscreen.
- **Dịch vụ nền:** Quản lý vòng đời RGB, âm thanh, Smart Contrast và OLED Pixel Shift; dừng các tác vụ lấy mẫu hoặc dịch chuyển khi overlay ẩn; dọn tray icon khi thoát bình thường hoặc gặp lỗi không được xử lý.
- **Dữ liệu và giao diện:** Thêm sao lưu/khôi phục ZIP không cần thư viện ngoài; thay thông báo khay mặc định bằng toast mang logo app, căn lề và bo góc theo giao diện Windows 11 Dark; hiển thị gợi ý quyền Administrator bằng cùng style với các gợi ý khác.
- **Màn hình và hiệu năng:** Nút **Quét lại màn hình** đọc lại độ phân giải, tần số quét và DPI khi bấm, áp dụng mức FPS 60/144 và tỉ lệ kích thước chung 100/125/150%, lưu cài đặt an toàn và hiện toast kết quả. Tỉ lệ chung không ghi đè tỉ lệ riêng của preset.
- **Điều khiển bằng bàn phím:** Thanh **Tỉ lệ kích thước chung** nhận focus khi click hoặc Tab, hỗ trợ phím mũi tên với bước 0,01; ô số bên cạnh vẫn cho nhập trực tiếp. Cột nhãn rộng hơn để hiện đủ tên thông số.
- **Focus qua nhãn slider:** Click nhãn bên trái đưa focus vào thanh trượt tương ứng trong editor, zoom preview, bảng màu R/G/B/A và Tab Chung. Slider hiện viền xanh mờ khi có focus; bước phím giữ theo loại thông số (0,01 cho tỉ lệ chung, 1 cho zoom và kênh màu).

Khi hoàn thành một tính năng hoặc bản sửa lỗi tiếp theo, thêm một mục ngắn có ngày vào phần nhật ký này cùng lần cập nhật mã nguồn.

### 2026-10-22 — Nâng cấp giao diện 5 tab và trạng thái Administrator

- **Tâm ngắm:** Ô chọn màu hiển thị và nhận mã RGB `#RRGGBB`; bỏ thanh alpha trong bảng màu, giữ độ mờ ở các điều khiển Opacity riêng. Định dạng JSON cũ vẫn đọc và ghi được.
- **Game:** Trạng thái chờ được viết rõ ràng thay dấu gạch ngang, kèm chấm nhấp nháy chỉ khi tab Game đang mở; nút thêm mẫu ghi rõ Profile Notepad.
- **Phím tắt:** Bổ sung lưu ý tránh dùng nút chuột phụ cho lệnh thoát. ADS/Fire tiếp tục dùng Raw Input chỉ đọc trên luồng riêng, không chặn hoặc sửa sự kiện chuột trong game.
- **Chung:** Thêm tooltip GPU cạnh switch, chỉ báo quyền Administrator dạng switch chỉ đọc cùng mô tả giới hạn của Anti-Cheat; Pixel Shift dùng một timer với chu kỳ ngẫu nhiên quanh 2 phút, lệch tối đa 1 pixel vật lý.
- **Thư viện:** Thêm bộ lọc màu kết hợp với tìm kiếm/danh mục và toast khay hệ thống sau khi thêm preset. Bổ sung kiểm thử phân loại màu.

### 2026-10-22 — Tinh chỉnh chỉ báo Administrator

- Bỏ chữ “Đã bật/Chưa bật” cạnh switch quyền Administrator; trạng thái chỉ thể hiện bằng switch chỉ đọc, căn nhãn và mô tả giống các mục khác trong nhóm Ứng dụng.

### 2026-10-22 — Nền Preview và theo dõi tài nguyên

- **Preview:** Chọn ảnh PNG/JPEG/WebP làm nền khung xem trước, lưu đường dẫn trong `settings.json`, giải mã ảnh một lần ở kích thước giới hạn và dùng nền mặc định nếu ảnh bị mất hoặc không đọc được; nút Khôi phục xóa lựa chọn.
- **Overlay:** Xác nhận cửa sổ đã đặt `IsHitTestVisible = false` và áp `WS_EX_TRANSPARENT | WS_EX_LAYERED` khi tạo HWND; không thêm hook hay vòng lặp Win32.
- **Hiệu năng:** Badge trong Tab Chung hiển thị CPU đã chuẩn hóa theo số lõi và RAM Working Set; lấy mẫu mỗi 3 giây bằng một timer, dừng khi cửa sổ Settings ẩn và giải phóng khi đóng.

### 2026-10-22 — Chuyển badge hiệu năng vào Thông tin hệ thống

- Đặt CPU/RAM trong thẻ Thông tin hệ thống của Tab Phím tắt, cùng kiểu thẻ và nhãn với các chỉ số phần cứng. Timer 3 giây chỉ chạy khi tab này và cửa sổ Settings đang hiện; rời tab hoặc ẩn xuống khay thì dừng lấy mẫu.

### 2026-10-22 — Chuẩn hóa thẻ Tài nguyên ứng dụng

- Đóng gói CPU/RAM thành thẻ thứ sáu trong panel Thông tin hệ thống, dùng cùng `InnerCard`, padding và khoảng cách với năm thẻ phía trên; bổ sung tiêu đề dịch Việt/Anh và chia hai cột chỉ số rõ ràng.

### 2026-10-22 — Tùy chỉnh nâng cao, Stream Mode, Preset theo vũ khí và Thống kê

- **Tâm ngắm:** thêm độ dày độc lập cho nhánh dọc và hiệu ứng bóng tùy chọn với màu, độ mờ, bán kính; giữ tương thích preset cũ. Các điều khiển viền, khoảng hở và chấm tâm đã có sẵn được giữ nguyên.
- **Game:** mỗi profile có thể gán preset cho phím 1–4, Q, E. Hook bàn phím chỉ tồn tại khi game đã khớp và có ít nhất một gán phím; callback không chặn phím của game và chuyển cập nhật preset về Dispatcher.
- **Stream:** thêm công tắc ở Tab Chung và dịch vụ OBS WebSocket v5 chỉ kết nối khi bật. Có thể nhập mật khẩu OBS cho phiên hiện tại; khi OBS báo đang stream hoặc record, overlay tạm ẩn. Ứng dụng yêu cầu Windows loại cửa sổ khỏi các API capture có hỗ trợ; Discord/Game Capture có thể không tuân theo yêu cầu đó và không có API Win32 chung để nhận biết Discord đang quay.
- **Thư viện:** thêm sáu mẫu hỗ trợ thị giác dựa trên hình dạng, kích thước lớn và viền tương phản trong danh mục riêng. Mẫu chỉ là tùy chọn hiển thị, không thay thế đánh giá khả năng tiếp cận cho từng người.
- **Thống kê:** Tab Thống kê hiển thị thời gian overlay hiện trong ngày, số lần đổi preset, preset hiện tại và biểu đồ tỉ lệ thời gian; dữ liệu lưu JSON nguyên tử, cập nhật giao diện mỗi 3 giây chỉ khi tab đang mở.
- **Chẩn đoán:** `anticheat_audit.log` ghi timestamp cho một số lần đổi foreground và gọi API cửa sổ quan trọng theo sự kiện; không ghi mỗi lượt polling để tránh tăng tải. Log không chứng minh ứng dụng được anti-cheat nào chấp thuận. Bộ kiểm thử tự động: 738 bài đạt (trừ một bài Raw Input phụ thuộc thiết bị thật).

### 2026-10-22 — Lịch sử thống kê và dọn dẹp dữ liệu

- Tách Tab Thống kê thành `StatsTab.xaml` và `StatsViewModel.cs`, đặt ngay sau Thư viện. Bổ sung tổng quan hôm nay, biểu đồ thời gian overlay hiển thị theo 7 ngày, 30 ngày hoặc 12 tháng, cùng tỷ lệ sử dụng từng preset trong khoảng đã chọn.
- Lưu lịch sử theo ngày trong `stats_history.json` bằng ghi JSON nguyên tử; ghi tối đa một lần mỗi 5 phút khi có thay đổi và ghi khi thoát. Dịch vụ chỉ nghe sự kiện overlay/preset, không lấy mẫu liên tục khi overlay ẩn; dữ liệu cũ `usage-stats.json` được chuyển sang định dạng mới khi cần.
- Hiển thị dung lượng file, cho bật/tắt tự động xóa dữ liệu cũ hơn 30 ngày. Nút xóa yêu cầu xác nhận, ghi lại file rỗng và cập nhật toàn bộ biểu đồ ngay. Bổ sung kiểm thử ghi nhận theo sự kiện, dọn dữ liệu cũ, lưu lựa chọn và xóa; 741 bài kiểm thử đạt (trừ một bài Raw Input phụ thuộc thiết bị thật).

### 2026-10-22 — Rà soát vòng đời tài nguyên và tối ưu UI

- Giảm debounce lưu `settings.json` từ 750 xuống 300 ms; tiếp tục dùng `SemaphoreSlim`, timer một lần và ghi JSON nguyên tử. Các lựa chọn nền preview ít thay đổi vẫn lưu ngay như trước.
- Chỉ tạo lại HICON khay khi trạng thái bật/tắt overlay thay đổi, thay vì mỗi lần đổi tên preset hoặc ngôn ngữ; giải phóng cả `TaskbarIcon` và HICON hiện tại trong đường thoát thường hoặc bất thường.
- Cửa sổ Settings dùng layout rounding và snap theo pixel; Overlay kiểm tra, khôi phục `WS_EX_TRANSPARENT | WS_EX_LAYERED` sau lần hiện lại, không kiểm tra trong vòng render. Brush nền thông báo fullscreen được đóng băng và dùng lại.
- Dừng timer CPU/RAM và timer làm mới thống kê khi Settings bị ẩn hoặc thu nhỏ; chuyển thao tác xóa gán preset vũ khí sang `RelayCommand` của Game ViewModel. Rà soát hook, GDI/DC và bộ giải mã ảnh: các đường dùng hiện có đã gỡ hook khi dừng, hủy icon/bitmap, gọi `ReleaseDC`, `Freeze()` và dùng `OnLoad` cho ảnh đã giải mã.
- Build Release không có cảnh báo/lỗi; 741 bài kiểm thử đạt (trừ một bài Raw Input cần thiết bị thật). Chưa đo CPU hoặc thời gian khởi động trên máy chơi game thực tế.

### 2026-10-22 — Ảnh nền ứng dụng và cá nhân hóa

- Thêm panel **CÁ NHÂN HÓA** vào Tab Chung của `SettingsWindow`: chọn PNG/JPEG/WebP, khôi phục nền gốc, chỉnh độ mờ 0–100% và blur 0–30 px. Hai ô số hiện `%`/`px`; click nhãn hoặc dùng phím mũi tên để chỉnh slider như các mục khác.
- Lưu `CustomAppBgPath`, `AppBgOpacity` và `AppBgBlurRadius` vào `settings.json` qua cơ chế ghi nguyên tử/debounce hiện có. Mặc định 18% và 10 px; giá trị JSON không hợp lệ được kẹp an toàn. Ảnh mất hoặc đọc lỗi tự dùng nền tối mặc định.
- Đặt lớp ảnh riêng phía dưới nội dung cửa sổ, chỉ làm mờ lớp ảnh; Card, InnerCard và khung Tab chuyển sang nền tối bán trong suốt khi có ảnh để chữ và nút vẫn rõ. Ảnh được nạp ngoài luồng UI, giới hạn kích thước giải mã, dùng `OnLoad` và `Freeze()` để không giữ khóa file; giải phóng tham chiếu khi đóng Settings.
- Bổ sung kiểm thử vòng đời chọn/khôi phục, lưu JSON, kẹp giá trị, định dạng slider và ảnh dọc/ngang. Release build sạch cảnh báo/lỗi; 747 bài kiểm thử đạt (trừ một bài Raw Input cần thiết bị thật).

### 2026-10-22 — Cache ảnh nền và tối ưu vùng cuộn

- Thêm `BitmapCache` cho riêng lớp ảnh nền đã áp dụng `BlurEffect`, tách khỏi nội dung cuộn để các thay đổi ở tab không buộc vẽ lại ảnh nền; dùng chế độ scale nhẹ cho ảnh mờ. Khi thay ảnh, đổi blur/opacity hoặc đổi kích thước cửa sổ, cache vẫn được cập nhật theo thay đổi đó.
- Giữ bộ nạp ảnh nền `OnLoad` + `Freeze()` và giới hạn cạnh ảnh giải mã tối đa 1920 pixel, tránh giữ file nguồn hoặc giải mã nguyên kích thước ảnh 4K/8K.
- Bật logical scrolling và virtualization cho `ListBox`; bỏ `ScrollViewer` bọc ngoài hai danh sách Game để virtualization có hiệu lực. Giữ vùng cuộn riêng của Preset vì thao tác kéo thả đang dựa vào vùng đó.
- Build Release thành công với 0 cảnh báo/lỗi; 6 bài kiểm thử ảnh nền đạt. Chưa đo thời gian khung hình khi cuộn trên thiết bị thực tế.

### 2026-10-22 — Tiền xử lý blur ảnh nền ngoài luồng UI

- Bỏ `BlurEffect` và cache bitmap XAML khỏi lớp ảnh nền của `SettingsWindow`. Lớp này chỉ hiển thị bitmap đã xử lý, đóng băng và không bắt sự kiện chuột.
- Thêm `ImageBlurHelper` làm mờ hai lượt trên mảng pixel premultiplied trong `Task.Run`; dùng tổng trượt theo hàng/cột để chi phí tuyến tính theo số pixel. Ảnh nguồn chỉ giải mã một lần và giới hạn cạnh ở 1280 px cho cửa sổ Settings; ảnh kết quả được `Freeze()` trước khi gán cho UI.
- Slider blur chờ 200 ms sau lần thay đổi cuối, hủy tác vụ cũ khi tiếp tục kéo hoặc đổi/khôi phục ảnh; opacity chỉ cập nhật lớp hiển thị, không tính lại blur. Danh sách `ListBox` dùng recycling khi virtualization có hiệu lực; không gắn virtualization giả cho `ScrollViewer` chứa nội dung tĩnh.
- Build Release 0 cảnh báo/lỗi; 749 bài kiểm thử đạt khi loại một bài Raw Input cần thiết bị thật. Chưa đo FPS thực tế, nên không cam kết cố định 60 FPS trên mọi máy.

### 2026-10-22 — Rà soát lớp nền và vùng cuộn panel

- Kiểm tra toàn bộ XAML của cửa sổ và UserControl: không có `Opacity` trên `Border`/`Grid` trong panel cuộn cần chuyển đổi; Card, InnerCard và khung tab đã dùng màu nền ARGB cố định (`#C0...`/`#A8...`). Giữ độ trong của riêng ảnh nền để slider vẫn chỉnh được tức thì.
- Gán `Focusable="False"` và `IsHitTestVisible="False"` trực tiếp cho ảnh nền và container nằm ngoài các `ScrollViewer`; lớp ảnh không tham gia xử lý chuột hoặc focus.
- Thêm style `PanelScrollViewer` cho các vùng cuộn nội dung ở Settings, Thư viện, Thống kê và hộp thoại: cuộn dọc, tắt cuộn ngang, không trì hoãn, bật layout rounding/snap pixel. Không áp đặt style này lên vùng cuộn nội bộ của TextBox/ComboBox, cũng không bật ClearType hint trên nền bán trong suốt. Danh sách Thư viện tiếp tục dùng `VirtualizingStackPanel` với chế độ tái sử dụng phần tử.
- Build Release thành công với 0 cảnh báo/lỗi. Chưa đo độ trễ cuộn trên phần cứng thực tế nên chưa thể xác nhận đã loại bỏ hoàn toàn giật hình.

### 2026-10-22 — Cuộn panel theo pixel và cố định bước lăn chuột

- Đổi style `PanelScrollViewer` của các tab Crosshair, Chung, Phím tắt, Game, Thống kê và hộp thoại sang `CanContentScroll=False`, `Focusable=False`; các panel tĩnh dùng offset vật lý để thanh cuộn không đổi theo chiều cao từng phần tử.
- `WheelScrollAssist` cuộn các vùng cấp trang 48 DIP cho mỗi nấc 120 đơn vị, có hỗ trợ delta nhỏ của thiết bị cuộn độ phân giải cao và chặn ở hai đầu. Danh sách không dùng style này vẫn giữ số dòng cuộn của Windows.
- Lưới Thư viện giữ `CanContentScroll=True` cùng `VirtualizingStackPanel`/`ScrollUnit=Pixel` để không mất ảo hóa; vùng cuộn riêng của danh sách Preset vẫn chuyển cú lăn tới panel bao ngoài khi danh sách bên trong đã tới mép.
- Không ép đo trước các `Expander` đang đóng vì sẽ làm tab tải chậm và thay đổi nội dung ẩn; chiều cao nội dung tĩnh được WPF tính sau layout đầu tiên, còn việc mở/đóng panel sẽ thay đổi kích thước thanh cuộn theo đúng nội dung mới.
- Build Release sạch cảnh báo/lỗi; 749 bài kiểm thử đạt khi loại một bài Raw Input cần thiết bị thật. Chưa xác minh độ ổn định kích thước thumb bằng thao tác chuột trên máy thực tế.

### 2026-10-22 — Giảm lớp alpha và chi phí Slider trên ảnh nền

- Khi dùng ảnh nền, nền khung Tab chuyển sang trong suốt; Card ngoài cùng dùng một lớp ARGB `#D9111B22`, còn `InnerCard` chỉ giữ viền và nền trong suốt. Các panel mặc định không có ảnh vẫn giữ màu gốc. Không áp `Background="Transparent"` lên mọi nhãn/control vì việc đó không xóa được các lớp vẽ và có thể đổi vùng bắt chuột.
- Thu gọn template thumb của Slider còn một `Ellipse`; track vẫn là đường 4 px và giữ `Track`/`RepeatButton` tối thiểu cần cho thao tác chuột. Bỏ `Opacity` cấp toàn Slider ở trạng thái disabled, đổi màu thumb thay thế; bật layout rounding, snap pixel và tắt snap-to-tick, giữ focus ring cho bàn phím.
- Đánh dấu 37 `SolidColorBrush` tĩnh trong Theme là frozen và dùng lại tài nguyên chung. `ColorPickerButton` dùng một brush mutable cho preview RGB, không cấp phát brush mới mỗi lần kéo; các swatch bất biến được chia sẻ và đã frozen.
- Giữ binding Slider cập nhật ngay: `Delay=10` không gom được phần lớn sự kiện kéo ở 60 Hz (cách nhau khoảng 16 ms), trong khi preview WPF đã gom `InvalidateVisual` theo khung hình; lưu settings và blur ảnh nền có debounce riêng. Không thêm hiệu ứng render vào Label/Slider.
- Build Release 0 cảnh báo/lỗi; 749 bài kiểm thử đạt khi loại một bài Raw Input cần thiết bị thật. Chưa đo độ trễ khung hình trên máy có ảnh nền, nên chưa thể kết luận hiệu năng bằng nền mặc định.

### 2026-10-22 — Giữ khung panel và thêm độ trễ binding slider 10 ms

- Rà soát lại các tab trong `SettingsWindow` và `StatsTab`: giữ nguyên toàn bộ thẻ `Border`, đường viền, thứ tự lồng và các vùng bắt chuột. Card đã dùng ARGB `#D9111B22` khi có ảnh nền, không có `Opacity` trên khung Card; `InnerCard` vẫn giữ đường viền và nền trong suốt, còn nền mặc định không đổi.
- Bổ sung `Delay=10` cho 27 binding `Value` từ các slider Crosshair/Chung và thanh zoom preview tới ViewModel, đúng yêu cầu mới. Giá trị số trong `SliderField` vẫn đồng bộ trực tiếp với slider; cập nhật model được hoãn ngắn. Slider màu RGB tiếp tục dùng sự kiện trực tiếp vì không có binding `Value` và cần phản hồi màu ngay.
- Kiểm tra lại vùng cuộn cấp trang đang dùng pixel scroll/48 DIP mỗi nấc, Thư viện giữ virtualization; 37 brush tĩnh trong Theme đã frozen. Không ép mọi control con dùng `Background="Transparent"` vì có thể làm mất trạng thái hover/nhấn hoặc đổi hit testing.
- Build Release 0 cảnh báo/lỗi; 749 bài kiểm thử đạt khi loại một bài Raw Input cần thiết bị thật. `Delay=10` không chứng minh đạt 60 FPS hoặc hết lag trên mọi máy; chưa có đo đạc khung hình thực tế.

### 2026-10-22 — Đồng bộ thẻ Đang nhận diện trong Tab Game

- Chuyển khung chẩn đoán “ĐANG NHẬN DIỆN” từ `InnerCard` sang style `Card` dùng chung với `PROFILE GAME` và `CHI TIẾT`. Nhờ vậy nền ARGB, đường viền, bo góc và padding tự đồng bộ theo theme, kể cả khi bật ảnh nền ứng dụng; giữ nguyên bốn binding trạng thái, cảnh báo quyền và chấm chờ.
- Căn lại khoảng cách tiêu đề trong thẻ; không thêm nền `Transparent` cho `TextBlock` vì nhãn vốn không vẽ nền và việc thêm brush không mang lại lợi ích render.
- Build Release thành công với 0 cảnh báo/lỗi.

### 2026-10-22 — Giải phóng ảnh nền khi cửa sổ Settings không hiển thị

- Chỉ bắt đầu nạp ảnh nền sau khi cửa sổ Settings hiển thị. Khi cửa sổ bị ẩn hoặc thu nhỏ, hủy tác vụ xử lý ảnh đang chạy, bỏ tham chiếu đến bitmap nguồn/ảnh đã làm mờ và thu gọn lớp nền; khi mở lại, nạp và xử lý ảnh bất đồng bộ từ đường dẫn đã lưu. Việc đổi ảnh mới hủy tác vụ render cũ sau khi giải mã thành công, tránh để kết quả cũ ghi đè ảnh mới và giữ ảnh cũ nếu tệp mới bị lỗi.
- Chỉ dùng `BitmapScalingMode="LowQuality"` trên ảnh nền; giữ `UseLayoutRounding`/`SnapsToDevicePixels` có sẵn trên cửa sổ để không làm giảm chất lượng icon, chữ và control. Ảnh được giải mã tối đa 1280 px nên thay đổi kích thước cửa sổ không giải mã lại; không thêm timer resize hay ép `GC.Collect()` gây khựng giao diện.
- Build Release thành công với 0 cảnh báo và 0 lỗi; 63 bài kiểm thử liên quan đạt (bộ kiểm thử có 1 cảnh báo analyzer tồn tại sẵn). Chưa đo VRAM/FPS trên máy thật; WPF/DWM quyết định thời điểm thu hồi bộ nhớ đồ họa sau khi tham chiếu được giải phóng.

### 2026-10-22 — Tắt nền phụ trong Preview khi dùng ảnh tùy chỉnh

- Preview trong dự án nằm ở `SettingsWindow` và `CrosshairEditorViewModel`. Khi ảnh tùy chỉnh đã nạp thành công, hai checkbox “Nền ca-rô” và “Nền tối” bị khóa; các cờ hiển thị thực tế gửi tới `CrosshairPreview` đều tắt. Bộ vẽ tiếp tục chỉ vẽ ảnh tùy chỉnh một lần, không vẽ thêm ca-rô hoặc nền tối.
- Giữ nguyên các lựa chọn nền đã lưu trong settings, nên khi bấm “Khôi phục”, hai checkbox và nền cũ hoạt động lại đúng trạng thái trước đó. Nút khôi phục vẫn dùng được nếu đường dẫn ảnh đã lưu bị mất hoặc ảnh lỗi; kết quả nạp ảnh cũ không thể ghi đè thao tác khôi phục/chọn ảnh mới.
- Build Release thành công với 0 cảnh báo/lỗi; 7 kiểm thử hiện có về lựa chọn nền Preview đạt. Chưa đo FPS trên thiết bị thực tế.

### 2026-10-22 — Hoãn dựng tab và giảm chi phí tài nguyên giao diện

- Chuyển nội dung Tab Chung, Phím tắt, Game và Thống kê của `SettingsWindow` sang `DataTemplate`: cây visual chỉ được tạo khi tab được chọn. Tab Crosshair mặc định vẫn hiện ngay; cột chỉnh sửa và Tab Thư viện tiếp tục dùng cơ chế nạp trễ có sẵn. Badge CPU/RAM chuyển sang binding với service của cửa sổ để không phụ thuộc vào trường XAML được tạo sớm; nút kết nối OBS lấy PasswordBox trong cùng template.
- Chỉ tạo `SystemInfoViewModel` và bắt đầu đọc phần cứng khi người dùng mở Tab Phím tắt lần đầu. Theme/brush/style đã được chia sẻ qua `App.xaml` từ trước; không có MergedDictionaries lặp trong UserControl cần xóa. Chuyển text formatting của cửa sổ và TextBlock mặc định sang `Display`, bật hinting `Fixed`; không ép ClearType trên mọi bề mặt bán trong suốt để tránh viền màu trên chữ.
- `CrosshairPreview` gỡ đăng ký sự kiện Profile, dừng animation và bỏ bitmap preview đã cache khi rời cây visual; khi được nạp lại sẽ đăng ký và dựng lại. Các ViewModel phục vụ tính năng nền tiếp tục sống đến lúc đóng Settings, nơi chúng đã được Dispose và hủy đăng ký sự kiện với service.
- Build Release 0 cảnh báo/lỗi; 749 kiểm thử đạt (loại một bài Raw Input cần thiết bị thật). Chưa đo thời gian khởi động, RAM hay chất lượng chữ trên máy thật, nên không khẳng định mức cải thiện phần trăm cố định.

### 2026-10-22 — Khôi phục kiểu dựng chữ mượt ban đầu

- Bỏ `TextOptions.TextFormattingMode="Display"` và `TextOptions.TextHintingMode="Fixed"` khỏi cửa sổ Settings; `App.xaml` không có thiết lập `TextOptions` cấp ứng dụng cần gỡ. Đưa style `TextBlock` dùng chung trong `Theme.xaml` về `TextFormattingMode="Ideal"` như trước đợt tối ưu tab.
- Giữ nguyên toàn bộ FontFamily, FontSize, FontWeight và màu chữ của các tab. Build Release thành công với 0 cảnh báo và 0 lỗi.

### 2026-10-22 — Giảm polling nhận diện game và chi phí callback phím vũ khí

- Renderer Overlay đã dùng một `CrosshairVisualHost` vẽ `Drawing` đóng băng qua `DrawingContext`; Preview đã dùng `DrawingVisual` để chụp tâm. Không chuyển sang cây Visual mới vì hiện không có Canvas/Line/Ellipse UI để loại bỏ. Giữ phép dịch tâm tạo theo lần vẽ lại hiếm hoi để tránh đổi một `Freezable` đang được WPF dùng trong lệnh vẽ trước đó.
- `ForegroundWindowWatcher` tiếp tục bắt `EVENT_SYSTEM_FOREGROUND`, bổ sung hook `EVENT_OBJECT_LOCATIONCHANGE` và `EVENT_OBJECT_NAMECHANGE` cho cửa sổ đang active. Timer quét mỗi giây chỉ bật nếu không cài đủ hook; vẫn giữ lần đọc ban đầu và nhịp thử lại ngắn khi tên/tiến trình game chưa sẵn sàng. Các hook mới được gỡ khi dừng hoặc dispose.
- `WeaponAutoSwitchService` chỉ ghi nhận phím vũ khí mới nhất trong callback hook, gom nhiều phím liên tiếp vào một tác vụ Dispatcher dùng lại delegate, rồi trả `CallNextHookEx`; việc tìm và kích hoạt Preset chạy sau callback. `HotkeyService` vốn dùng `RegisterHotKey` và Raw Input, không dùng low-level hook nên không chuyển sang `Task.Run` cho mỗi gói chuột.
- Cấu hình JSON dùng chung được đóng băng bằng `MakeReadOnly` để tái sử dụng metadata cache. Chưa bật `JsonSerializerContext`: các property do CommunityToolkit.Mvvm sinh ra không được generator JSON nhìn thấy trong cùng lượt biên dịch, từng gây mất dữ liệu Preset khi thử trước đây.
- Build Release 0 cảnh báo/lỗi; 749 kiểm thử đạt (loại một bài Raw Input cần chuột thật). Chưa đo CPU, input latency hoặc draw calls trên phần cứng thực tế; không cam kết 0.0% CPU/0 ms lag.

### 2026-10-22 — Giảm cập nhật thừa ở slider khi dùng ảnh nền

- Rà soát các tab trong `SettingsWindow` và `StatsTab`: Card ngoài đã dùng một lớp nền ARGB khi có ảnh nền; `InnerCard` giữ viền nhưng chuyển nền sang trong suốt. Các vùng cuộn cấp trang đã dùng pixel scroll, template Slider đã gọn và 37 brush tĩnh trong Theme đã được freeze. Giữ nguyên cấu trúc panel, vùng bắt chuột và toàn bộ thiết lập chữ gốc.
- Thêm `Delay=10` cho binding `Value` bên trong `SliderField`, bổ sung cho độ trễ đã có ở các binding slider của tab. Ô nhập số chỉ được gán lại khi chuỗi hiển thị thực sự đổi, giảm thông báo binding và lần vẽ chữ không cần thiết khi kéo.
- Chuyển style cố định của nhãn slider, ô số và nhãn RGB sang `StaticResource` để tránh theo dõi thay đổi tài nguyên động trong mỗi control; giao diện và typography giữ nguyên. Slider RGB không có binding `Value`, tiếp tục phản hồi màu trực tiếp.
- Build Release thành công với 1 cảnh báo analyzer xUnit2029 có sẵn trong test; 587 kiểm thử đạt khi lọc nhóm Raw Input cần phần cứng. Chưa đo FPS/độ trễ kéo slider trên máy có ảnh nền nên không khẳng định đã loại bỏ hoàn toàn lag.

### 2026-10-22 — Ô mật khẩu OBS đồng bộ theme và có nút hiện/ẩn

- Thay `PasswordBox` mặc định trong Tab Chung của `SettingsWindow` bằng khung nhập tối dùng cùng brush nền, viền và bo góc với ComboBox “Mức độ ưu tiên”. `PasswordBox` và `TextBox` trong khung có nền trong suốt, không còn ô trắng mặc định; viền khung đổi màu khi focus.
- Thêm nút mắt dạng vector có hover/focus và nhãn trợ năng song ngữ. Mật khẩu được đồng bộ giữa ô ẩn, ô hiện và `GeneralSettingsViewModel` khi gõ hoặc đổi chế độ; nút “Kết nối OBS” gửi giá trị đang hiển thị. Giá trị chỉ nằm trong phiên và được xóa khỏi ViewModel khi đóng cửa sổ, không lưu vào `settings.json`.
- Build Release thành công với 1 cảnh báo analyzer xUnit2029 có sẵn trong test; 587 kiểm thử đạt khi lọc nhóm Raw Input cần phần cứng. Chưa kiểm tra trực quan trên desktop thật.

### 2026-10-22 — Hoãn tạo tab phụ và giảm chi phí khởi động nền

- `SettingsViewModel` chỉ tạo ViewModel của tab Phím tắt, Game và Thống kê khi tab tương ứng được truy cập lần đầu; khi đóng cửa sổ chỉ giải phóng những ViewModel đã tạo. Bộ đo CPU/RAM và timer làm mới thống kê cũng chỉ được dựng khi mở tab liên quan, và tiếp tục dừng khi tab/cửa sổ không hiển thị.
- `AppBootstrapper` bỏ các lần resolve lặp dịch vụ update, RGB và âm thanh trong bước khởi động nền; RGB/âm thanh được lấy khi áp dụng preset, còn RGB vẫn được ngắt trước khi giải phóng container. `UpdateService` vẫn có thể được tạo khi dựng ViewModel của cửa sổ Settings, nhưng không gọi mạng trước bước kiểm tra cập nhật. Hook phím và nhận diện game vẫn khởi chạy ngay sau khung hình đầu để không làm trễ việc hiện overlay.
- Giữ `PublishReadyToRun` đã có và khai báo rõ `TieredCompilation`/`TieredPGO` cho bản phát hành. Xác nhận bằng MSBuild cả ba thuộc tính là `true`. Không ép `SetProcessWorkingSetSize`: thu gọn working set không giải phóng VRAM và có thể gây page fault/khựng khi khôi phục cửa sổ.
- Rà soát: ảnh nền đã tự ẩn và nhả bitmap khi thu nhỏ; card dùng ARGB, panel con giữ nền trong suốt, slider có `Delay=10`, vùng cuộn cấp trang theo pixel, brush tĩnh đã frozen. Không đổi `TextOptions`, phông hay màu chữ; không gán `Background=Transparent` hàng loạt vì có thể đổi vùng bắt chuột mà không giảm draw call.
- Build Release thành công (1 cảnh báo xUnit2029 có sẵn); 594 kiểm thử đạt, loại một bài Raw Input cần chuột thật. Một bài đo thời gian lệch 1 ms ở lượt đầu và đạt khi chạy lại. Chưa đo cold/warm startup hay FPS trên phần cứng thật, nên chưa kết luận đạt “instant startup”, 60 FPS hoặc 0% CPU.

### 2026-10-22 — Tab Tạo Crosshair bằng pixel art

- Thêm tab “Tạo Crosshair” sau Thư viện. `PixelArtTab` và `PixelArtViewModel` chỉ được khởi tạo ở lần mở tab đầu tiên, không tăng chi phí dựng giao diện lúc khởi động.
- Bảng vẽ dùng một `WriteableBitmap` cho lưới 16×16, 24×24 hoặc 32×32; kéo chuột trái để vẽ liên tục, chuột phải để xóa, có công cụ bút/tẩy, xóa sạch, đường tâm, tám màu mẫu và mã HEX tùy chỉnh. Đường lưới và đường tâm chỉ nằm trong editor, không xuất vào ảnh PNG.
- Mã chia sẻ `PX1` dùng RLE + Base64, có kiểm tra kích thước và dữ liệu trước khi tải. Có thể sao chép, dán để khôi phục bản vẽ; preset ảnh PNG được lưu qua kho ảnh hiện có. Nút Lưu thêm preset vào thư viện và kích hoạt; nút Áp dụng trực tiếp đổi overlay trong phiên mà không tự tạo preset trong thư viện.
- Build Release thành công. 10 test mới cho mã chia sẻ đạt; 750 test đạt khi loại bài Raw Input cần phần cứng và một bài `SliderField` cũ thiếu resource `FieldLabel` khi chạy riêng trên luồng STA. Chưa đo FPS thực tế trên desktop.
### 2026-10-22 — Giới hạn GC latency theo phiên game và tinh gọn tương tác WPF

- Bỏ việc bật `SustainedLowLatency` ngay từ lúc khởi động. `GamingGcLatencyService` chỉ chuyển chế độ khi cửa sổ game khớp profile đang ở foreground và overlay thực sự hiện; khi chuyển về app, ẩn overlay, bật Stream suppression, dừng watcher hoặc thoát ứng dụng, dịch vụ phục hồi chế độ GC trước đó. Không ép `GC.Collect` và không can thiệp `NoGCRegion` do nơi khác quản lý.
- Giữ cơ chế aliasing theo hình dạng đã có ở overlay: nét thẳng dạng khối dùng `Aliased`, vòng tròn/ảnh xoay giữ render mượt; ảnh pixel nhập vào overlay tiếp tục dùng `NearestNeighbor` khi phóng nguyên bội. Bảng vẽ pixel mới dùng `Aliased` và `NearestNeighbor` riêng, không đổi kiểu chữ mặc định.
- Bỏ hai margin âm trong template ListBoxItem/TabControl bằng padding hoặc render transform có cùng vị trí hiển thị; bỏ Fade khi mở ComboBox để phản hồi trực tiếp. Trạng thái của bảng vẽ pixel được giữ chiều cao cố định, tránh đổi chiều cao card mỗi khi có thông báo. Không đổi toàn bộ kích thước nút/ô hoặc trạng thái Visibility vì các cỡ chữ đa ngôn ngữ và vùng bắt chuột cần được giữ nguyên.
- Build Release thành công; 4 test mới cho chuyển trạng thái GC và 754 test trong bộ lọc phù hợp đạt. Lượt này chưa đo thời gian Measure/Arrange, GC pause hay FPS trên phần cứng thực tế; không cam kết loại bỏ hoàn toàn micro-stutter.
### 2026-10-22 — Cuộn quán tính có thời hạn cho các panel

- Thêm `SmoothScrollBehavior` cho các `PanelScrollViewer`: cộng dồn bước lăn vào điểm đích, nội suy EaseOutCubic trong 200 ms bằng timer chỉ hoạt động lúc đang cuộn; dừng ngay khi ẩn, unload hoặc người dùng bấm chuột/phím. Không giữ `ScrollViewer` bằng dictionary tĩnh, không chạy vòng lặp khi idle.
- `WheelScrollAssist` vẫn chọn vùng cuộn gần con trỏ và chuyển cú lăn ra vùng cha ở mép; nay xét điểm đích đang chuyển động để không nuốt các cú lăn liên tiếp. Các panel cuộn theo pixel; danh sách Thư viện tiếp tục `VirtualizingStackPanel` + `CanContentScroll=True` + Recycling để không dựng toàn bộ card trong RAM.
- Đặt cờ rounding/deferred scrolling an toàn ở style `ScrollViewer`; khi chuyển động, cờ `IsScrolling` tạm bỏ nền hover trên nút thường, nút nhấn và mục ListBox mà không khóa click. Giữ nguyên font chữ, màu nền card và cơ chế dựng chữ mặc định.
- Build Release thành công; 760 test trong bộ lọc phù hợp đạt, gồm 6 test mới cho bước lăn và easing. Chưa đo độ mượt hoặc FPS trên màn hình thật; animation cuộn có thể tăng số lần layout trong 200 ms nên không khẳng định triệt tiêu mọi lag trên mọi máy.

### 2026-10-22 — Bộ chọn nhịp chuyển động giao diện 60–144 FPS

- Thêm card UI FPS trong Tab Chung với slider 60–144 và các nút chọn nhanh 60/75/90/120/144 FPS; giá trị được lưu riêng trong `settings.json`, mặc định 144 và được giới hạn trong khoảng hợp lệ. Giữ nguyên FPS overlay và kiểu chữ hiện có.
- Áp dụng ngay nhịp đã chọn cho timer cuộn quán tính và các animation toast do ứng dụng tạo; timer cuộn vẫn dừng khi không thao tác. Nạp lại giá trị đã lưu khi khởi động và đồng bộ khi cài đặt thay đổi.
- Không dùng `Timeline.DesiredFrameRateProperty.OverrideMetadata` lúc chạy vì đây không phải giới hạn compositor toàn cục; tốc độ thực tế phụ thuộc WPF, màn hình và tải hệ thống. Build Release thành công; 761 test phù hợp đạt.

### 2026-10-22 — Đồng bộ giao diện thẻ UI FPS trong Tab Chung

- Chuyển card UI FPS sang style `Card` dùng chung với các panel Hiệu năng và Bảo vệ màn hình, gồm nền/border/bo góc tự thích ứng khi bật ảnh nền ứng dụng. Tiêu đề nhóm dùng `SectionHeader` chữ in hoa; dòng mô tả giữ cỡ chữ nhỏ và tự xuống dòng.
- Xếp điều khiển thành hàng nhãn bấm để focus slider, slider 60–144 và ô giá trị nền tối bên phải. Thu gọn năm nút chọn nhanh thành chip số 60/75/90/120/144 với nhãn riêng; bổ sung chuỗi tiếng Việt và tiếng Anh. Giữ nguyên binding, lệnh lưu FPS, font và cơ chế dựng chữ.
- Build Release thành công, 0 cảnh báo và 0 lỗi. Đây là thay đổi XAML/chuỗi giao diện nên không bổ sung bài test mô phỏng triển khai.

### 2026-10-22 — Rà soát binding, resource và Win32 foreground path

- Ghi rõ `Mode=OneWay` cho 89 binding chỉ hiển thị trong các view/theme (văn bản, danh sách, trạng thái, màu, thumbnail), giữ `TwoWay` cho ô nhập, slider và toggle. Chuyển 5 `DynamicResource` cuối trong popup chọn màu sang `StaticResource` vì theme hiện không thay brush/style lúc chạy. Đây chủ yếu là chuẩn hóa ý nghĩa binding; các thuộc tính TextBlock vốn mặc định OneWay nên không tuyên bố giảm chi phí theo tỷ lệ cố định.
- Trong `ForegroundWindowWatcher`, dùng một delegate Dispatcher tái sử dụng cho sự kiện foreground, và đọc tiêu đề ở nhánh fallback qua bộ đệm UTF-16 `stackalloc` cùng P/Invoke con trỏ. Nhánh kiểm tra tiêu đề không còn tạo `StringBuilder`/`string` cho mỗi lần quét; khi foreground thật sự đổi, chuỗi/đối tượng sự kiện vẫn cần tạo để cập nhật UI và audit log. `RECT`/ID tiến trình đã là value type trên stack, không cấp phát thêm struct dùng chung gây tranh chấp luồng.
- Không áp `BitmapCache` lên card có slider/chữ động: thay đổi một phần tử con sẽ làm WPF raster hóa lại cả card và giữ thêm bitmap trong VRAM; nền ảnh hiện đã được xử lý sẵn thành bitmap frozen. Không nâng `ThreadPriority` cho UI hay hạ ưu tiên thread pool dùng chung, tránh giành CPU của game hoặc tác vụ khác. Không đổi phông chữ, TextFormattingMode, màu hay viền card.
- Build Release thành công; 761 bài test hồi quy phù hợp đạt. Chưa đo FPS, CPU, RAM hoặc VRAM trên máy chạy game nên không khẳng định mức giảm tài nguyên cụ thể.

### 2026-10-24 — Rà soát tổng thể và tái sử dụng lệnh vẽ tĩnh

- Kiểm tra đường vẽ Overlay và Pixel Art: Overlay đã dùng một `CrosshairVisualHost` với `DrawingContext`/`Drawing` frozen; Pixel Art đã dùng một `WriteableBitmap` thay vì nhiều phần tử hình WPF. Giữ nguyên cơ chế aliasing thích ứng theo hình dạng và toàn bộ khung Border, màu, viền, bố cục, font chữ gốc.
- Lưới và đường tâm của bảng vẽ Pixel Art nay được dựng thành một `DrawingGroup` frozen, chỉ tạo lại khi kích thước, số ô hoặc trạng thái đường tâm đổi; thao tác vẽ từng pixel không còn tạo lại toàn bộ lệnh kẻ lưới. Overlay tái sử dụng `TranslateTransform` frozen khi kích thước host không đổi, tránh cấp phát trên mỗi lần `OnRender`.
- Xác nhận các cơ chế đã có: `SetWinEventHook` cho foreground với timer dự phòng khi hook lỗi, callback phím vũ khí chuyển việc sang Dispatcher, GC latency giới hạn theo phiên game, lazy tab và khởi tạo dịch vụ sau khung hình đầu, cuộn mượt 200 ms, Delay=10 cho slider, nền ảnh ẩn khi cửa sổ không hiển thị, cấu hình ReadyToRun/TieredCompilation/TieredPGO. Không thêm cache bitmap vào card tương tác vì thay đổi slider sẽ làm raster hóa lại cả card và tốn VRAM; không ép `GC.Collect` khi thu nhỏ hay nâng ưu tiên UI để tránh pause và tranh CPU với game. Không dùng ArrayPool cho ảnh pixel tối đa 32×32 và thao tác cập nhật 1 pixel vì không có buffer tạm lớn trên đường nóng.
- Build Release thành công (1 cảnh báo xUnit2029 có sẵn); đầu ra kiểm thử báo 592/592 bài đạt khi loại nhóm RawInput cần thiết bị thật. Tiến trình test không tự thoát sau khi in kết quả nên đã dừng thủ công; chưa xác nhận mã thoát cuối cùng của runner. Chưa đo FPS, draw call, VRAM hoặc độ trễ input trên phần cứng thực tế nên không tuyên bố mức cải thiện định lượng.
