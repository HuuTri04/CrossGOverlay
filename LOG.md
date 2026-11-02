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

### 2026-10-24 — Giảm vẽ hover khi cuộn Tab Chung

- Tab Chung nằm trong `SettingsWindow.xaml`, không có file `GeneralTab.xaml` riêng. Giữ nguyên toàn bộ card Border, viền, ô giá trị, kiểu chữ và cấu trúc hai cột. Bốn binding slider của Tab Chung (tỉ lệ chung, UI FPS, opacity và blur ảnh nền) dùng `Delay=15`; ô UI FPS đã là `OneWay` với `StringFormat`, các ô nhập trong `SliderField` vẫn giữ khả năng gõ số.
- Template slider dùng chung vốn đã gọn, dùng thumb đơn và brush frozen; cố định chiều cao ở 24 px và bỏ hiệu ứng đổi màu hover của thumb trong lúc cuộn. ComboBox, ToggleSwitch và nút mắt OBS cũng bỏ hover redraw khi `SmoothScrollBehavior.IsScrolling` bật; tương tác bấm/focus vẫn hoạt động.
- Card chứa slider/toggle là nội dung động nên không thêm `BitmapCache` hàng loạt: đổi một giá trị sẽ raster hóa lại cả card và dùng thêm VRAM. Trang Tab Chung dùng `StackPanel` tĩnh, vì vậy không bật cờ virtualization giả trên `ScrollViewer`; cơ chế pixel scrolling và cuộn mượt hiện có được giữ nguyên. Không ép `Background=Transparent` cho mọi panel con vì nền `null` không vẽ thêm lớp màu, còn `Transparent` có thể đổi vùng hit test.
- Build Release thành công, 0 cảnh báo/lỗi. 767 bài test hồi quy đạt khi loại nhóm RawInput cần thiết bị thật và một bài test `SliderField_DisplaysAndParsesPercentAndPixelSuffixes`; bài này thất bại độc lập vì tạo control trực tiếp mà không nạp resource `FieldLabel` của ứng dụng. Chưa có phép đo frame time trên phần cứng thực tế để khẳng định đã hết lag.

### 2026-10-24 — Nhấp vùng trống để rời ô nhập và công tắc

- Cửa sổ Settings chứa toàn bộ tab nay có `MainRootGrid` focusable nhưng không tham gia thứ tự Tab và không vẽ viền focus. Ở pha `MouseDown` nổi lên, chỉ cú nhấp trái chưa được control xử lý vào vùng không tương tác mới chuyển keyboard focus sang grid này. Các ô nhập, nút gạt, Slider, ComboBox, mục danh sách, nhãn có `Target` và nút bấm vẫn giữ hành vi gốc; `LostFocus` của ô nhập được phát đúng để chốt giá trị đang gõ.
- Kiểm tra template: TextBox và khung mật khẩu OBS đã dùng trigger `IsKeyboardFocusWithin`, Switch dùng `IsMouseOver`/`IsKeyboardFocused`; WPF tự trả viền và hover về màu gốc khi focus/con trỏ rời đi. Không cưỡng bức `ReleaseMouseCapture` ở `MouseLeave` vì điều đó có thể cắt ngang thao tác kéo hoặc nhấn đang diễn ra. Không thêm handler riêng vào từng tab hay handler toàn cục trong `App.xaml`, tránh xử lý trùng và tác động lên dialog/toast.
- Build Release thành công (1 cảnh báo xUnit2029 có sẵn); 767 test phù hợp đạt. Nhóm RawInput cần phần cứng thật và bài test `SliderField_DisplaysAndParsesPercentAndPixelSuffixes` thiếu resource `FieldLabel` khi khởi tạo control độc lập vẫn được loại khỏi lượt chạy này. Chưa kiểm tra trực quan trạng thái hover/focus trên desktop thật.

### 2026-10-24 — Giảm số lượt layout khi cuộn Tab Chung có ảnh nền

- Xác nhận lớp ảnh nền của `SettingsWindow` đã nằm ngoài `ScrollViewer`, không bắt chuột, dùng bitmap nạp `OnLoad` và `Freeze()` (kể cả ảnh đã pre-blur), scale `LowQuality`. Card Tab Chung không có `BitmapCache`; `PanelScrollViewer` đã dùng pixel scrolling, layout rounding và không cuộn hoãn. Vì vậy không thêm bản sao ảnh, không gắn cache lên card hay ép aliasing cho ảnh chụp.
- Tab Chung liên kết trạng thái ảnh nền với `SmoothScrollBehavior.UseBackdropFrameBudget`: khi ảnh nền hiện, cuộn EaseOut vẫn chạy nhưng tối đa 60 nhịp/giây để giảm số lượt `ScrollToVerticalOffset` và layout của các panel; khi ảnh nền tắt, nhịp quay về UI FPS người dùng chọn (60–144). Không thay đổi cơ chế cuộn của các tab khác. Nền card `#D9111B22` nay dùng chung một `SolidColorBrush` frozen; giữ nguyên màu, viền, bo góc và kiểu chữ.
- Không thể triệt tiêu 100% alpha blending mà vẫn giữ card bán mờ trên ảnh nền; các panel con không đặt `Background` hiện không tạo lớp màu để bỏ. Build Release thành công (1 cảnh báo xUnit2029 có sẵn); 11 test cuộn và 771 test hồi quy phù hợp đạt. Bộ lọc vẫn loại nhóm RawInput cần thiết bị thật và bài test `SliderField` thiếu resource khi chạy độc lập. Chưa đo frame time trên desktop có ảnh nền để xác nhận mức giảm lag thực tế.

### 2026-10-24 — Kiểm thử hồi quy hệ thống và xử lý dữ liệu đầu vào hỏng

- Mở rộng bộ xUnit hiện có với các ca: settings JSON hỏng và khôi phục `.bak`, FPS UI ngoài dải, phần tử `null` trong danh sách phím tắt/profile, 20 lần ghi cấu hình đồng thời cùng debounce/flush, mã chia sẻ Pixel Art hỏng, kích thước lưới không hợp lệ, thay ảnh nền Full HD/4K 30 lần, 50 chu kỳ đăng ký/gỡ WinEventHook, HWND không hợp lệ, sự kiện game đến từ luồng nền và ngắt kết nối OBS trong lúc bắt tay WebSocket. Sửa fixture test để nạp tài nguyên ứng dụng trước khi tạo `SliderField` độc lập.
- Sửa lỗi khởi động khi JSON chứa `Hotkeys: [null]` hoặc `GameProfiles: [null]`: lọc phần tử hỏng trước khi bổ sung phím tắt mặc định. FPS UI ngoài khoảng 60–144 trở về 144; lưới Pixel Art từ chối kích thước không hỗ trợ trước khi thay bitmap. Đưa sự kiện foreground từ luồng khác về Dispatcher và bỏ qua sự kiện đã xếp hàng sau khi dừng dịch vụ. Cho phép dừng lại `ForegroundWindowWatcher` sau `Dispose` an toàn; `HotkeyService.Attach` không giữ HWND sai nếu tạo hook thất bại.
- Nền Preview dùng chung bộ giải mã ảnh giới hạn cạnh tối đa 1600 px, nạp `OnLoad`, đóng file và `Freeze()` trước khi đưa lên UI. Dịch vụ OBS có endpoint nội bộ cho test loopback; endpoint sản phẩm vẫn là `ws://127.0.0.1:4455`. Giữ nguyên bố cục, màu và kiểu chữ giao diện.
- Build Release x64 thành công, 0 warning/0 error; 788 bài test tự động đạt và 1 bài Raw Input cần chuột vật lý được đánh dấu skip (gói `SendInput` không xuất hiện như Raw Input trong môi trường chạy này). Chưa có phép đo VRAM hoặc kiểm thử click/focus bằng thao tác desktop thực; không suy diễn từ test ảnh rằng bộ nhớ GPU tuyệt đối không rò rỉ.

### 2026-10-24 — Gom geometry tâm ngắm và bỏ qua lượt định vị trùng

- Gom các nhánh dọc, ngang và chéo thành `StreamGeometry` frozen theo từng lớp preset. Lõi và viền dùng chung đường hình học, chỉ thay pen; vẫn giữ thứ tự vẽ, độ mờ và độ dày riêng của nhánh. Bộ test hình học kiểm tra số nhánh, độ dài, T-shape, thứ tự và việc tái sử dụng cùng geometry frozen.
- Overlay vốn không đăng ký `CompositionTarget.Rendering` liên tục; `CrosshairVisualHost` chỉ yêu cầu vẽ lại khi drawing thay đổi. `OverlayController` nay bỏ qua `SetWindowPos` khi tọa độ và kích thước không đổi, đồng thời đặt lại cache vị trí khi hiện lại cửa sổ hoặc cấu hình màn hình/DPI đổi.
- Toàn bộ slider dùng chung `SliderField` có một lớp debounce 15 ms ở binding nội bộ; bỏ debounce lồng trên binding ngoài để không cộng dồn độ trễ. Hai slider trực tiếp (zoom preview, UI FPS) dùng `Delay=15`. Expander và RadioButton trong trang cuộn bỏ hiệu ứng hover trong thời gian cuộn; focus và thao tác bấm giữ nguyên.
- Giữ nguyên thẻ card, kích thước điều khiển hiện có, màu và typography mặc định. Không ép chiều cao cố định cho mọi card hay cột nhãn vì nội dung dịch/ngắt dòng cần được đo theo kích thước cửa sổ. Build Release x64 sạch cảnh báo; 789 test đạt, 1 test Raw Input cần thiết bị thật được skip. Chưa benchmark frame time/VRAM trên máy chơi game nên không khẳng định mức tăng FPS định lượng.

### 2026-10-25 — Giảm đánh thức UI và giới hạn bộ nhớ trong phiên game

- Cuộn mượt chỉ gọi `ScrollToVerticalOffset` khi lệch ít nhất 0,5 đơn vị WPF; dừng timer sớm khi đã tới đích hoặc mép vùng cuộn. Tab Chung đã tự giới hạn nhịp cuộn về 60 FPS khi ảnh nền hiển thị; không thay đổi thiết lập FPS người dùng chọn cho toàn ứng dụng.
- `ForegroundWindowWatcher` loại sự kiện foreground đã cũ bằng cách so HWND với foreground hiện thời. Sự kiện thay đổi tên/vị trí của cửa sổ chính được gom theo cửa sổ 75 ms trước khi đưa lên Dispatcher; sự kiện đổi foreground và đóng cửa sổ vẫn xử lý ngay để overlay không trễ khi vào game.
- `GamingGcLatencyService` chỉ bật low-latency trong phiên game phù hợp có overlay hiển thị như trước, đồng thời kiểm tra Managed Heap khi vào game và mỗi 30 giây. Vượt 250 MB thì trả lại chế độ GC trước phiên và không bật lại cho tới phiên game tiếp theo; không ép thu gom rác. Kiểm thử thêm các ngưỡng và việc khôi phục chế độ.
- Khi Settings ẩn, hẹn nhả ảnh nền đã giải mã/xử lý sau 30 giây để mở lại nhanh trong khoảng ngắn. Khi hiện lại, chỉ tái sử dụng nếu đường dẫn, dung lượng, thời điểm sửa file và bán kính blur còn khớp. Tác vụ blur cũ vẫn bị hủy khi đổi tham số hoặc ẩn cửa sổ; thêm điểm kiểm tra hủy ngay sau khi sao chép pixel.
- Rà soát dịch vụ nền: Smart Contrast/Pixel Shift đã dừng theo trạng thái overlay; RGB không có vòng lấy mẫu và AudioFeedback chỉ phát âm theo sự kiện. OBS chỉ duy trì kết nối khi Stream Mode cần biết trạng thái capture để tự hiện lại overlay, nên không ngắt chỉ vì Settings ẩn. Giữ nguyên typography/card và renderer; không thêm BitmapCache. Build Release sạch cảnh báo; 794 test đạt, 1 test Raw Input cần chuột thật được skip. Chưa có profiler trace hay số đo P95/P99 trên máy chơi game để kết luận mức giảm CPU/FPS định lượng.

### 2026-10-25 — Tách cache hình học, màu vẽ và kiểm tra vòng đời thumbnail

- Giữ bề mặt HWND overlay theo vùng bao thật của tâm ngắm (đã gồm viền, bóng, DPI và offset); vị trí/kích thước chỉ cập nhật qua `SetWindowPos` khi đổi thực tế. Bổ sung cache LRU tối đa 64 bộ `StreamGeometry` theo tọa độ nhánh, tách khỏi cache brush (64) và pen (128) đã `Freeze()`. Đổi màu/độ mờ dùng lại hình học; đổi độ dài, khoảng cách hoặc DPI dựng hình mới. Bóng của overlay cũng tái sử dụng `DropShadowEffect` frozen khi thông số không đổi.
- Bảng nhận diện Game cập nhật bằng một snapshot bất biến thay cho sáu lần phát `PropertyChanged`; callback khác luồng gom sự kiện mới nhất trước khi đưa về Dispatcher ưu tiên Background. Giữ nguyên XAML card, màu nền `#D9111B22`, typography `Ideal` và bố cục.
- Thumbnail thư viện là vector vẽ theo viewport ảo hóa, không phải ảnh raster cần giải mã 64 px. Cache ảnh raster của renderer đã là LRU 64 MB; không thêm bộ giải mã riêng không được dùng. Sửa thumbnail GIF chỉ giữ khung tĩnh: dừng timer phát khung ngay sau khi dựng Drawing, tránh timer sống theo thẻ đã tái sử dụng. Overlay và preview GIF vẫn chạy theo giới hạn FPS hiện có; tâm tĩnh chỉ vẽ khi thay đổi, Pixel Shift/Smart Contrast dừng khi overlay ẩn.
- Kiểm thử thêm: 200 lần thay đổi geometry/màu không vượt ngân sách cache; 100 chu kỳ tạo/giải phóng ViewModel tab Game và 100 thumbnail không giữ preset qua GC; thumbnail GIF không để lại timer. Build Release x64: 0 warning/0 error; 800 test pass, 1 Raw Input test cần chuột vật lý được skip. Chưa có đo VRAM, GDI/Private Bytes dài hạn hoặc profiler frame time trên phần cứng game; không khẳng định CPU/FPS tuyệt đối từ unit test.

### 2026-10-25 — Giảm cấp phát blur, nhịp rảnh và I/O trên Dispatcher

- `ImageBlurHelper` mượn cả ba mảng Pbgra32 từ `ArrayPool<byte>.Shared`, trả trong `finally` kể cả khi hủy tác vụ/lỗi; `BitmapSource` được tạo và `Freeze()` trước khi trả mảng. Test xác nhận bitmap đầu không đổi sau nhiều lượt blur tái sử dụng bộ đệm.
- Overlay kiểm tra topmost mỗi 1,5 giây khi vừa hiện hoặc vừa bị che, giãn về 5 giây sau bốn lượt ổn định; sự kiện foreground vẫn khẳng định topmost ngay. Stats tự hủy lịch lưu còn treo khi overlay ẩn và không còn dữ liệu bẩn. Không gom timer OLED (chu kỳ phút), Stats (5 phút) và watcher (fallback chỉ bật khi WinEventHook lỗi) thành nhịp chung 1 giây vì như vậy tăng wakeup.
- Khi Settings ẩn, giữ cây tab và ViewModel để mở lại nhanh, dừng GIF preview và thả bitmap raster tạm của preview. Ảnh nền đã xử lý vẫn được giữ tối đa 30 giây theo cache có sẵn, rồi nhả; lớp nền Window đã `Collapsed` khi ẩn. Không ép LOH compaction/full GC vì có thể tạo pause trong game; chưa có benchmark chứng minh cần thao tác đó.
- Bản Debug đếm số lần chủ động yêu cầu `InvalidateVisual`, số `OnRender` và lần gọi `SetWindowPos` mà không ghi log mỗi khung. Test Debug xác nhận gán lại cùng Drawing và aliasing không đổi không yêu cầu vẽ lại.
- Nút xóa thống kê và lần lưu `stats_history.json` chuyển sang `await` với khóa ghi bất đồng bộ; thao tác mở/flush file atomic của settings và stats chạy trên thread pool. Thoát thông thường từ tray/hotkey chờ flush preset, settings, stats và audit log bất đồng bộ trước khi đóng; đường `OnExit` trực tiếp giữ flush đồng bộ dự phòng để tránh mất dữ liệu. `LOG.md` chỉ là nhật ký phát triển, không được ứng dụng ghi khi chạy.
- Giữ nguyên `TextFormattingMode=Ideal`, màu/card và bố cục. Build Release x64 sạch 0 warning/0 error; 801 test pass, 1 Raw Input test cần chuột thật được skip; test bộ đếm Debug đạt. Chưa đo CPU wakeup, P95 frame time hoặc thời gian mở lại Settings trên phần cứng game nên không khẳng định ngưỡng tuyệt đối.

### 2026-10-25 — Benchmark Release x64 và báo cáo hiệu năng có số liệu gốc

- Thêm `benchmarks/Measure-CrossGOverlay.ps1` để khởi chạy bản Release x64 không gắn debugger, đo mốc khung hình đầu Settings từ `OnContentRendered`, CPU, Working Set, Private Bytes, tổng handle, GDI và USER handles; lưu CSV/JSON cùng SHA-256 binary. Tiến trình benchmark dùng `CROSSGOVERLAY_BENCHMARK_ROOT` để tách dữ liệu khỏi cấu hình người dùng.
- Loại các lượt thử trong sandbox không có tray icon khỏi báo cáo. Mười lượt warm startup hợp lệ có first frame trung bình 1050,3 ms, trung vị 1043,5 ms (961–1241 ms); cold start chưa đo. Settings ẩn: 3 lượt × 600 giây, 1.800 mẫu CPU đều đọc 0% ở độ phân giải 1 giây, Private Bytes trung bình 219,643 MB, Working Set 296,791 MB. GDI giữ 50→50 trong cả ba lượt; không thấy tăng đều trong 10 phút. Ngưỡng Private Bytes ≤120 MB không đạt.
- Bổ sung script lấy `System.Runtime` bằng `dotnet-counters` và bộ đếm `GPU Process Memory` của Windows khi **không chạy game**. Bốn lượt 45 giây có Managed Heap trung bình 9,755–9,788 MB, allocation khoảng 8–9 KB/s, không ghi nhận GC pause trong 88 mẫu; ba lượt GPU shared memory toàn tiến trình 156,477–156,555 MB, dedicated counter 0 MB. Các kết quả ngắn này không chứng minh ổn định 60 phút hay chi phí GPU/DWM của overlay.
- Tạo `PERFORMANCE_BENCHMARK_REPORT.md` với 8 kịch bản, ma trận 12 nhóm chỉ số và biểu đồ từ CSV/JSON thực; CSV app-only xuất từng lượt, trung vị và độ lệch chuẩn mẫu, để trống chênh lệch khi không có baseline trước tối ưu. Chuẩn bị mẫu CSV Delta Force Firing Range 3 trạng thái × 5 lượt × 120 giây cùng script tính trung vị, độ biến thiên và chênh lệch baseline; script từ chối dòng chưa đo và đã được kiểm tra bằng fixture tạm không lưu vào kết quả. Đã tìm thấy bản PresentMon trong NVIDIA FrameViewSDK nhưng **không chạy với Delta Force** vì chính sách Garena/Delta Force chưa xác nhận cho phép công cụ này. FPS, 1% Low và P95/P99 game giữ N/A, không tạo số liệu giả. Build Release x64 sạch 0 warning/0 error; 801 test đạt, 1 bài Raw Input cần chuột vật lý được skip.

### 2026-10-26 — Profiling VMMap, startup, cuộn nền ảnh và độ bền tài nguyên

- Bổ sung chẩn đoán startup chỉ bật bằng `CROSSGOVERLAY_BENCHMARK_TIMELINE=1`, lấy mốc sau DI, Settings initialized/first render, overlay first drawing và click đầu tiên. Mười lượt cho trung vị drawing đầu **1177,356 ms** khi mở Settings và **667,192 ms** khi vào tray; đây là lệnh vẽ WPF, chưa phải thời điểm DWM present. Không đổi luồng startup thường khi biến chẩn đoán tắt.
- VMMap năm trạng thái cho thấy Private **191,312 MiB** lúc overlay/tray, **239,371 MiB** sau khi mở cả bảy tab, **228,707 MiB** sau khi ẩn 30 giây, **233,492 MiB** sau 100 chu kỳ Settings và **233,969 MiB** sau thêm 60 phút idle. GDI từ **47→47**, USER **44→41** trong giờ idle; không thấy tăng tuyến tính. Hai vùng Private Data 61,039 MiB chưa có allocation stack, vì vậy chưa quy cho WPF/CLR/GPU và không ép GC/trim Working Set.
- Đo cuộn Tab Chung có ảnh nền **3 × 120 giây** ở mỗi mức 60/144 UI FPS: CPU chuẩn hóa 16 luồng có trung vị của ba lượt lần lượt **1,343% / 1,389%**. WPR bị Windows từ chối `0xc5585011`; P95/P99 UI frame time và Measure/Arrange/Render vẫn **N/A**. Ba lượt 30 giây cho overlay tĩnh, ảnh nền, Smart Contrast và GIF có VMMap/CPU/GPU process memory; đổi 500 preset ở hai lượt và 1000 lượt có GDI dao động thay vì tăng đều. Số đo GPU là toàn tiến trình, không phải VRAM riêng HWND.
- Cập nhật `PERFORMANCE_BENCHMARK_REPORT.md` và script dưới `benchmarks/` cùng CSV gốc; script hiệu ứng lưu checkpoint mỗi 60 giây. Lượt GIF đầu bị ngắt được loại khỏi thống kê; sau khi khóa single-instance được giải phóng, lượt mới chạy trọn **3600,81 giây** và có 360 mẫu. GDI giữ **33** toàn phiên; Process Private trung vị 5 phút giữa/cuối **198,055 / 197,992 MiB**, không thấy tăng tuyến tính sau warmup. VMMap Private **190,953→194,758 MiB** gồm cả warmup; GPU Shared cuối phiên 139,7891 MiB, không có chuỗi GPU cả giờ. Hai probe có điều kiện riêng xác nhận bộ phát GIF có **100 timer tick trong 10,8675 / 10,8812 giây**; đây không phải số frame đã present, và binary probe chỉ khác ở bộ đếm chẩn đoán. Không chạy Delta Force/PresentMon, không tạo số FPS giả và không đổi renderer/font/card khi chưa có profiler xác nhận bottleneck. Build Release x64 0 warning/0 error, 801 test pass, 1 skip; script PowerShell qua kiểm tra cú pháp.

### 2026-10-27 — Tách đường vẽ overlay khi mở Settings và truy nguồn cấp phát native

- Đổi thứ tự `AppBootstrapper`: nạp preset, bắt đầu thống kê, khởi tạo overlay, watcher và quy tắc hiển thị trước khi dựng Splash/Settings; nhường Dispatcher để tâm ngắm vẽ lượt đầu. Tray, hotkey và OBS tiếp tục khởi tạo sau đó. Phân tách cờ vòng đời để gỡ watcher/timer kể cả khi pha sau khởi động lỗi; cửa sổ Settings trở thành `Application.MainWindow`, hộp thoại không lấy overlay ẩn làm owner. Không dùng `UpdateLayout()` để giả nhận DWM đã present.
- Mười lượt Release x64 cuối có trung vị `OverlayFirstDrawing` **694,899 ms** (630,529–734,015 ms), so với baseline 5 lượt **1158,436 ms**; 10/10 lượt vẽ overlay trước Settings. Chỉ **1/10 lượt** đạt ≤650 ms, nên mục tiêu này chưa đạt ổn định. Kiểm tra thêm đường khởi động thẳng vào tray đạt 1/1 lượt. Build 0 warning/0 error; 801 test pass, 1 skip.
- GDIView trước/sau 1000 lượt đổi preset: `Region 12→46`, `Brush/Bitmap/Font/DC` giữ nguyên, tổng GDI `47→81` và sau 60 giây vẫn 81. Checkpoint dao động 77–121; không xác nhận giả thuyết cache LRU trần 117, không thêm `MaxCachedHandles=120` khi chưa có stack.
- VMMap/GPU cùng PID: với toast khay bình thường, ẩn Settings kích hoạt vùng Private Data thứ hai **62.504 KiB** và GPU Shared `105,809→168,734 MiB`; tắt riêng toast trong benchmark thì vùng này không xuất hiện và GPU Shared giữ **105,777 MiB** khi ẩn. Đường toast là điều kiện kích hoạt được A/B hỗ trợ; hàm native sở hữu vùng thứ nhất/thứ hai vẫn chưa có allocation stack. Giữ nguyên cache và không ép `GC.Collect()`/trim Working Set.
- WPR `VirtualAllocation` bị từ chối `0x80070005`; `GPU + DesktopComposition` bị từ chối `0xc5585011`, nên chưa có ETL hợp lệ cho allocation stack, UI P95/P99 hay DWM per-HWND. Bổ sung script GDIView/VMMap/GPU và số đo gốc vào `PERFORMANCE_BENCHMARK_REPORT.md`; không đổi renderer, `TextFormattingMode=Ideal` hoặc màu Card `#D9111B22`.

### 2026-10-27 — Đối chứng Toast 61 MiB, GDI Region và startup xen kẽ

- Thêm probe chỉ chạy khi có `CROSSGOVERLAY_BENCHMARK_ROOT` và scenario rõ ràng; script chụp VMMap/GDIView/GPU trước/sau **10 và 50 lượt Toast** trong cùng PID. Cùng binary Release x64: 10 Toast chuẩn làm GPU Shared toàn tiến trình **105,453→164,652 MiB**, 50 Toast **105,781→166,703 MiB**; VMMap mỗi lượt từ **một lên hai** vùng Private Data 62.504 KiB, không có vùng thứ ba sau 50 lượt. Checkpoint GPU không tăng tuyến tính. Đối chứng 10 Toast không `DropShadowEffect` vẫn làm GPU Shared **105,773→163,562 MiB** và vẫn có vùng thứ hai: shadow không phải nguyên nhân duy nhất. Chưa có allocation stack nên giữ nguyên Toast, không pool/ép GC/trim RAM.
- Đo 1.000 lượt đổi preset qua UI Automation và `IPresetLibrary.SetActive` nội bộ trên cùng binary: GDI Region tương ứng **12→66→66** và **12→36→36** (trước/sau/sau 30 giây); Brush/Bitmap/Font/DC không đổi. Đường nội bộ cũng tăng Region nên UI Automation không phải nguồn duy nhất; tổng GDI dao động, không đặt trần hủy handle khi chưa có stack.
- Khởi động Tray/Settings xen kẽ **15 cặp** cùng fixture: trung vị mốc WPF overlay first drawing **594,484 / 549,916 ms**, P95/P99 nearest-rank **1.037,485 / 950,165 ms** cho hai mode; overlay vẽ trước Settings first render **15/15**. Không có phép đo cold start hoặc DWM present nên không diễn giải callback WPF thành khung hình đã hiện trên màn hình.
- Token Windows hiện tại Medium Integrity, Administrators deny-only; script elevated ETW kiểm tra quyền và dừng trước WPR. Chưa có ETL/allocator stack/UI P95-P99/DWM cost; thêm `Collect-ElevatedEtw.ps1` cho terminal Administrator và cập nhật toàn bộ phép đo/giới hạn trong `PERFORMANCE_BENCHMARK_REPORT.md`. Build Release x64 0 warning/0 error, 801 test pass, 1 skip; giữ nguyên renderer, typography `Ideal` và Card `#D9111B22`.

### 2026-10-29 — ETW Administrator: allocation Toast, GDI Region và UI Present

- Chạy `Collect-ElevatedEtw.ps1` trong PowerShell Administrator thật để lấy ETL Release x64 cho Toast, cuộn Tab Chung với cấu hình 60/144 UI FPS, và đổi preset nội bộ. Trace `VirtualAllocation` có 0 event lost; vùng Toast thứ hai `0x03D0A000` (62.504 KiB) có stack **`wpfgfx_cor3.dll → d3d9.dll → d3d9on12.dll → d3d12core.dll → igd12um64xel.dll → dxgkrnl/dxgmms2`**. Xác định đây là cấp phát qua đường đồ họa WPF/driver Intel; loại resource chính xác chưa biết. VMMap Private Data **94,348→153,641 MiB**, GPU Shared **105,777→164,953 MiB** sau Toast. Giữ nguyên Toast và không ép GC/trim RAM.
- Profile WPR `Handle` không ghi GDI HRGN, nên bổ sung trace Win32k `GdiHandleOperation`: 1.000 preset có Create/Destroy `HandleType=4` **59.875/59.795**, ròng **+80** khớp GDIView Region **12→92**. Lượt 100 preset có stack cho **5.998/5.998** Create, ròng **+38** khớp Region **12→50**; các handle tạo còn thấy đến snapshot cuối có stack qua **`wpfgfx_cor3.dll → gdi32.dll → win32k`**. Chưa có tên hàm/PDB đầy đủ hoặc chứng cứ leak tuyến tính nên không sửa hủy handle.
- Hai ETL cuộn nền ảnh 30 giây có **0 event lost**. Mỗi lượt gửi 190 wheel message; app có 950/944 sự kiện `Present` cho một HWND hoạt động, khoảng giữa `Present` P50/P95/P99 tương ứng **30,662/63,169/77,378 ms** và **30,488/63,120/78,866 ms**. Đây là khoảng giữa yêu cầu nộp khung, **không phải** UI frame processing time hoặc DWM scanout. Trace có DWM Present toàn desktop nhưng thiếu sự kiện WPF `Measure/Arrange/Render`; P95/P99 frame processing, dropped frames riêng app và thời điểm pixel lên màn hình tiếp tục N/A. Không dùng khác biệt 60/144 để tối ưu vì tick cuộn có ảnh nền vẫn giới hạn 60 Hz.
- Cập nhật `PERFORMANCE_BENCHMARK_REPORT.md` với đường dẫn ETL, phương pháp và giới hạn diễn giải. Chỉ chỉnh script benchmark và tài liệu trong đợt này; **không sửa C#/XAML ứng dụng**, renderer, font `Ideal` hoặc Card `#D9111B22`.

### 2026-10-29 — Đối chứng scroll ba trạng thái và fan-out đổi Preset

- Chạy ETW Administrator trên cùng binary Release x64 cho Settings Idle, cuộn nền tối và cuộn có ảnh nền (mỗi lượt 30 giây, `UiTargetFps=60`); năm trace mới, gồm hai lượt đổi Preset, đều **0 event lost**. Idle không có app `Present`; cuộn nền tối/ảnh lần lượt 942/913 app `Present`, khoảng P99 **78,247/79,054 ms**. UI message thread on-CPU **1.819,77/1.815,26 ms** trong 30 giây; chênh lệch nhỏ của một lượt đo chưa đủ kết luận chi phí ảnh nền. CSV đối chứng: `benchmarks/results/scroll-state-etw-20261029.csv`.
- CPU stack có mẫu `Measure`/`Arrange` ở cả hai lượt cuộn, nhưng **0/23** và **0/26** khoảng `Present` ≥75 ms chứa mẫu layout. ETW hiện có không ghi duration pass WPF hoặc timestamp trực tiếp của `ScrollToVerticalOffset`; `UI Thread Busy` theo Application Timeline, P95/P99 thời gian xử lý frame và DWM scanout riêng app vẫn **N/A**. Không quy P99 `Present` cho layout khi chưa có bằng chứng nhân quả.
- Đo 100 lượt đổi Preset qua UI Automation và 100 lượt lệnh nội bộ: UIA `Select()` P50/P95/P99 **23,563/35,704/218,931 ms**, bốn lời gọi chậm trùng bốn khoảng app `Present` ≥75 ms, nhưng chỉ **2/4** khoảng có mẫu stack layout; đường nội bộ không có mốc latency từng lệnh. GDI Region **12→44** (UIA) và **12→52** (nội bộ). Fan-out trực tiếp của `ActiveChanged` đi tới Bootstrapper/Overlay, SettingsViewModel/Preview và StatsService; Tab Thư viện mẫu không mở trong fixture. CSV: `benchmarks/results/preset-fanout-etw-20261029.csv`.
- Chỉ thêm/chỉnh script profiler, CSV kết quả và báo cáo; **không sửa mã C#/XAML ứng dụng**, không tái cấu trúc Visual Tree, giữ typography mặc định `Ideal` và Card `#D9111B22`.

### 2026-10-29 — Benchmark T1–T5 và A/B cô lập subscriber đổi Preset

- Thêm các mốc `Stopwatch.GetTimestamp()` chỉ dưới `#if BENCHMARK`: T1 nhận lệnh; T2 bắt đầu/kết thúc `SetActive`; T3 trước `ActiveChanged`; T4a/b/c cho Bootstrapper, SettingsViewModel, StatsService; T4d cho `BringIntoView` qua Dispatcher; T5 lần đầu `CrosshairVisualHost.OnRender` với preset mới. Fixture ghi CSV sau lượt đo; bản Release sản xuất không chứa probe hay cờ cô lập subscriber.
- Chạy 4 nhánh xen kẽ × 3 lượt × 100 preset ngoài game: baseline đầy đủ, không Stats, không cập nhật editor/preview, không BringIntoView. Đủ **1.200/1.200** mốc T5. Trung vị của P50 T1→T5 từng lượt lần lượt **1,4427 / 1,3877 / 1,4142 / 1,4785 ms**; P95 tương ứng **2,2300 / 2,3470 / 2,1876 / 2,2183 ms**. Tắt editor/preview giảm thời gian T3→`SetActive` kết thúc **8,3754→5,8838 ms**, nhưng T1→T5 chỉ giảm **0,0285 ms**; chưa có bằng chứng cần thay đổi đường sự kiện sản xuất.
- Ở cả 1.200 lượt, T5 xảy ra trước khi `SetActive` trả về; không cộng các thời lượng subscriber như chuỗi tuần tự. T5 là WPF `OnRender`, không phải DWM Present. Kết quả, giới hạn phương pháp và các CSV raw/run/summary được ghi trong `PERFORMANCE_BENCHMARK_REPORT.md`. Build Release thường thành công **0 warning/0 error**, kiểm tra type probe vắng mặt; giữ font mặc định `Ideal` và Card `#D9111B22`.

### 2026-10-29 — Tự hiện overlay khi game tạo/khôi phục cửa sổ render

- Bổ sung WinEventHook toàn cục có lọc sớm `EVENT_OBJECT_SHOW` và `EVENT_SYSTEM_MINIMIZEEND`: chỉ khi HWND phát sự kiện là foreground mới đọc lại trạng thái và yêu cầu ép Z-order. Kể cả metadata của HWND không đổi, sự kiện này vẫn buộc `ProfileAutoSwitcher` gọi `ReassertTopmost()` ngay, không chờ Alt-Tab. Gỡ toàn bộ hook trong `Stop/Dispose`.
- Thay một lượt thử metadata 200 ms bằng chuỗi xác minh hữu hạn tại **100/300/800/1800 ms** cho HWND chưa đủ thông tin; khi profile game đã khớp, xác minh lại HWND/PID và ép topmost ở cùng bốn mốc. Hủy chuỗi retry khi rời game, tắt overlay, dừng theo dõi hoặc bật chế độ ẩn stream. Mọi cập nhật overlay/preset tiếp tục chạy qua UI Dispatcher; `SetWindowPos(HWND_TOPMOST)` dùng `SWP_NOACTIVATE`, bảo toàn click-through.
- Thêm bài hồi quy cho `SHOW` trên cùng HWND và giữ bài kiểm tra 50 chu kỳ hook. `dotnet test` Release: **802 pass, 1 skip, 0 fail**. Không sửa XAML, typography `Ideal` hay Card `#D9111B22`. Chưa kiểm thử trực tiếp bằng game cụ thể trong phiên này.

### 2026-10-31 — Thông báo Windows native thay cho WPF toast

- Thay luồng `TrayIconController.ShowNotification` bằng Windows App SDK `AppNotificationManager` (WPF không đóng gói): Windows quản lý Light/Dark, vị trí, Animation, nút đóng, Focus Assist và Notification Center. Dùng logo hiện có; logo trong nội dung được trích một lần từ resource sang file local để Windows đọc được. Click thông báo mở Settings qua UI Dispatcher; gỡ đăng ký khi thoát.
- Loại `SystemTrayToastWindow` khỏi bản build, không tạo thêm HWND/DropShadowEffect khi thông báo. Gom thông báo trùng nội dung trong 10 giây và dùng Tag theo nội dung để cập nhật đúng mục trong Notification Center mà không ghi đè cảnh báo khác. Khi App SDK không khả dụng (đặc biệt chạy Administrator), dùng thông báo khay native qua `H.NotifyIcon`, có icon app và tôn trọng quiet time; khi người dùng tắt notification trong Windows thì không hiện popup thay thế.
- Giữ .NET 8/WPF và phát hành self-contained; thêm Windows App SDK Foundation để tránh kéo WinUI/AI/ML. Build Release thành công; 802 test pass, 1 skip. Probe giao diện trực tiếp bị chặn do Shell tray trả `TryCreate failed` trong phiên chạy công cụ, nên chưa xác nhận trực quan trên Light/Dark hoặc Notification Center ở desktop người dùng. Không sửa overlay, XAML typography hay Card.

### 2026-11-01 — Sửa native toast không hiện khi thu nhỏ xuống tray

- Đọc log chạy thực: `AppNotificationManager.Register()` lỗi COM `0x8007007E` do thiếu `Microsoft.WindowsAppRuntime.Insights.Resource.dll`; không phải do `ShutdownMode` (đã là `OnExplicitShutdown`) hay đường dẫn ảnh. Thêm `Microsoft.WindowsAppSDK.Runtime` cùng phiên bản 1.8 vào gói self-contained; target publish kiểm tra bắt buộc DLL native và `Resources/icon.png` hiện diện.
- Dùng trực tiếp file icon vật lý `Resources/icon.png` cạnh EXE, chỉ gọi `SetAppLogoOverride` khi file tồn tại và không rỗng; thiếu icon thì gửi toast chữ. Ghi log rõ tình trạng support, `Setting`, đăng ký COM và lệnh gửi notification; giữ `try/catch` để lỗi OS không làm sập app. Gắn handler mở Settings trước khi đăng ký COM; giữ `App.xaml.cs` và `ShutdownMode` hiện có.
- Smoke test với dữ liệu benchmark tách biệt trên bản publish Release x64: `Register()` thành công, `Setting=Enabled`, 3 lệnh `Show()` (thông báo tray đầu và 2 lượt thử) trả về không lỗi, app tự thoát mã 0. `dotnet test` Release: 803 pass, 1 skip. Chưa đo trạng thái banner trên màn hình trong chế độ Do Not Disturb; Windows có quyền chỉ lưu vào Notification Center. Không thay đổi overlay, font hay Card.

### 2026-11-01 — Tinh giản bố cục Tab Chung và cuộn khi có ảnh nền

- Tab Chung hiện nằm trong `SettingsWindow.xaml`. Bỏ ba lớp bọc không cần thiết: `StackPanel` quanh bộ chọn màn hình, `Grid` quanh nút kiểm tra cập nhật và `Border` trống làm khoảng cách. Chuyển margin sang control tương ứng để giữ nguyên bố cục, nút, Card và màu sắc.
- Khi có ảnh nền, bật deferred scrolling cho thao tác kéo Thumb của Tab Chung; cuộn bánh xe vẫn đi qua `SmoothScrollBehavior` với ngân sách 60 Hz. Template Slider/ScrollBar đã tối giản, RepeatButton không nhận focus, Brush chung đã Freeze và hover đã được giảm khi cuộn nên không tạo thêm template hay khóa hit test toàn panel (tránh nuốt click đang thao tác).
- Build Release: 0 warning, 0 error. Test Release: 803 pass, 1 skip. Chưa có phép đo WPF Measure/Arrange hay P95/P99 sau thay đổi, vì vậy không kết luận đã loại bỏ hoàn toàn lag; phông chữ `Ideal` và cấu trúc Card không đổi.

### 2026-11-01 — Tăng tương phản nút lọc trong Tab Thư viện

- `FilterChip` dùng nền tối đặc và viền từ cùng `StaticResource` đã Freeze với ô tìm kiếm; trạng thái hover dùng nền tối sáng hơn và viền xanh. Trạng thái chọn dùng nền xanh LeafyGreen đặc với chữ xanh đen để không bị ảnh nền sáng xuyên qua.
- Tăng độ đậm số lượng mẫu trên chip từ 65% lên 80% để vẫn đọc rõ khi được chọn. Giữ hình viên thuốc, font và `TextFormattingMode=Ideal`; tính theo sRGB, tương phản chữ thường/nền khoảng 14,5:1 và chữ đang chọn/nền khoảng 10,9:1 (số đếm khoảng 6,8:1). Build Release: 0 warning, 0 error.

### 2026-11-01 — Giảm độ chói chip lọc đang chọn

- Đổi riêng trạng thái `IsChecked=True` của `FilterChip` từ xanh neon sang nền xanh rừng sẫm `#0D4E2D`, viền `#1E6E38`, chữ sáng `TextPrimary`. Dùng nền đặc để giữ tương phản ổn định trên cả ảnh nền sáng và tối; hai brush được khai báo chung với `po:Freeze=True`.
- Giữ nguyên nền của chip chưa chọn, phom pill, font `Ideal` và độ dày viền 1px. Tương phản sRGB của chữ sáng trên nền mới khoảng 8,3:1 (số đếm 80% khoảng 5,9:1). Build Release: 0 warning, 0 error.

### 2026-11-01 — Nạp ảnh nền độ phân giải cao không chặn UI

- Rà soát thấy loader đã dùng `BitmapCacheOption.OnLoad`, khóa file chỉ trong thời gian nạp, giới hạn cạnh dài và `Freeze()`. Gom phần giải mã chạy nền thành `AppBackgroundImageLoader.DecodeAsync()` có kiểm tra hủy trước/sau các bước I/O và giải mã; ViewModel ảnh nền ứng dụng và preview hủy yêu cầu cũ khi chọn ảnh mới, khôi phục hoặc đóng/ẩn cửa sổ. Vẫn kiểm tra phiên bản để kết quả cũ không ghi đè ảnh mới.
- Nâng trần giải mã ảnh nền cửa sổ từ 1280 lên **1920 px cạnh dài** để giữ nét trên màn DPI cao, giữ preview ở 1600 px. Đây là đánh đổi: bitmap nền 16:9 tối đa khoảng 7,9 MiB thay vì 3,5 MiB ở trần cũ 1280 px, nhưng thấp hơn khoảng 75% so với giải mã nguyên ảnh 4K; file gốc không bị sửa. Đổi riêng ảnh nền chính sang `BitmapScalingMode=HighQuality`; WPF có thể tốn thêm chi phí nội suy khi thay đổi kích thước cửa sổ.
- Test ảnh PNG 4K xác nhận kết quả 1920×1080, bitmap đã Freeze và file có thể mở độc quyền sau khi nạp; test token hủy trước khi mở file. Toàn bộ test Release: **805 pass, 1 skip**, 0 fail. Chưa có số liệu profiler xác nhận mức cải thiện frame time khi cuộn hoặc mức RAM của phiên thực tế; typography `Ideal` và Card giữ nguyên.

### 2026-11-01 — Giới hạn ảnh nền Preview và tách chất lượng nội suy

- Khung Preview tái sử dụng loader giải mã bất đồng bộ đã có nhưng giảm trần cạnh dài từ 1600 xuống **1280 px** (ảnh 4K 3840×2160 thành 1280×720, khoảng 3,5 MiB bitmap BGRA thay vì 5,5 MiB ở mức 1600). Cơ chế hủy yêu cầu cũ và `Freeze()` vẫn giữ nguyên; file ảnh gốc không bị sửa.
- Tách ảnh nền tùy chỉnh thành `Image` riêng phía sau `CrosshairPreview`, áp `BitmapScalingMode=HighQuality` chỉ cho ảnh. Lớp vẽ tâm ngắm vẫn dùng `NearestNeighbor`/`Aliased` để hiển thị chính xác pixel; nền mặc định và đường kẻ hướng không đổi. Khi có ảnh tùy chỉnh, preview để nền trong suốt, không vẽ thêm nền ca-rô/tối che ảnh.
- Test Release: **806 pass, 1 skip**, 0 fail; test ảnh 4K xác nhận kích thước 1280×720 và test render xác nhận Preview không phủ kín lớp ảnh. Đây là giải mã WPF/WIC trên luồng nền; chưa có bằng chứng rằng codec của máy dùng giải mã phần cứng hay số liệu frame time sau thay đổi.

### 2026-11-01 — Audit vòng đời ảnh nền, notification và ảo hóa thư viện

- Ảnh Preview chỉ nạp khi Settings hiện; khi ẩn/thu nhỏ, hủy tác vụ giải mã đang chạy và xóa tham chiếu bitmap trong ViewModel để binding của `Image`/`CrosshairPreview` nhả ảnh cũ. Khi hiện lại, nạp bất đồng bộ từ đường dẫn đã lưu; `Dispose()` cũng xóa ảnh. Thay ảnh trực tiếp bằng bitmap mới, không chen trạng thái `null` gây nhấp nháy. Ảnh nền toàn cửa sổ đã có cache 30 giây khi ẩn để mở lại nhanh, sau đó tự nhả source và bitmap đã blur; không thêm `GC.Collect()` vì chưa có bằng chứng LOH bị giữ sau khi bỏ tham chiếu.
- Native toast dùng `WindowsNotificationService.NotificationInvoked`; `TrayIconController.Dispose()` đã gỡ và gọi `Unregister()` khi ứng dụng thoát. Bổ sung chặn `Initialize()` lặp và tách bước hủy callback/COM registration để một bước lỗi không bỏ qua bước còn lại. Không gọi `Uninstall()` vì cơ chế hiện tại là Windows App SDK, không phải `ToastNotificationManagerCompat`.
- `FilterChipSelectedBg` (`#FF0D4E2D`) và `FilterChipSelectedBorder` (`#FF1E6E38`) đã là `StaticResource` chung có `po:Freeze=True`; không tạo Brush mới cho từng chip. Lưới 526+ preset đã dùng `VirtualizingStackPanel`, Recycling, cuộn pixel và `CanContentScroll=True` trong ScrollViewer nội bộ của `ItemsControl`, không có ScrollViewer bao ngoài cấp không gian vô hạn. Các chip danh mục là danh sách nhỏ riêng, không cần ảo hóa.
- Test Release: **807 pass, 1 skip**, 0 fail. Lượt đầu có 1 fail thoáng qua ở test đếm yêu cầu render theo thời gian ngắn; chạy lại riêng nhóm liên quan (21 pass) và toàn bộ suite đều pass. Chưa chạy profiler phiên dài nên không tuyên bố RAM/VRAM hoặc COM handle tuyệt đối không rò rỉ.

### 2026-11-01 — Đóng dropdown khi cuộn nội dung Settings

- Bổ sung `PopupScrollGuard` theo dõi ComboBox đang mở và popup chọn màu bằng tham chiếu yếu. Khi lăn bánh xe trên cửa sổ Settings hoặc khi một `ScrollViewer` thực sự đổi offset (kể cả kéo thanh cuộn), đóng trực tiếp `IsDropDownOpen`/`IsOpen` rồi chuyển focus về vùng gốc. Cuộn bên trong dropdown là HWND riêng nên vẫn dùng được; không xóa focus trên mọi cú lăn khi không có popup.
- Template ComboBox đặt `StaysOpen=False`; đăng ký guard bằng Style chung, còn popup chọn màu dùng cùng cơ chế. Chỉ đóng popup thuộc đúng cửa sổ và chỉ xử lý `ScrollChanged` khi offset thay đổi, tránh làm việc thừa do thay đổi viewport/layout. Giữ nguyên typography `Ideal`, màu và bố cục.
- Test WPF xác nhận Style tự gắn guard, Popup có `StaysOpen=False`, và chỉ cửa sổ sở hữu mới đóng được dropdown. Toàn bộ test Release: **808 pass, 1 skip**, 0 fail. Chưa có số liệu UI automation cho thao tác kéo thanh cuộn thực tế.

### 2026-11-02 — Tổng rà soát vòng đời tài nguyên, ảo hóa danh sách và mã thừa

- Kiểm tra đường dừng/Dispose của WinEventHook theo dõi foreground/cursor và hook chuyển vũ khí: các handle đều có đường `UnhookWinEvent`/`UnhookWindowsHookEx`. Chuỗi xác minh HWND game là hữu hạn, có `CancellationToken` và được hủy khi rời game, dừng dịch vụ hoặc thoát ứng dụng. Đường khởi động tạo overlay và đăng ký preset trước khi mở Settings; cập nhật overlay không phụ thuộc SettingsWindow. `ActiveChanged` đã có `RenderThrottle`, không thêm debounce khi chưa có bằng chứng nghẽn.
- Xác nhận ảnh nền ứng dụng/Preview cùng dùng `AppBackgroundImageLoader`: giải mã bất đồng bộ với trần cạnh dài lần lượt 1920/1280 px, `OnLoad`, `Freeze()` và hủy yêu cầu cũ. Không ép GC hoặc thay đổi cache khi chưa có số liệu retention.
- Sửa danh sách preset cá nhân: bỏ `ScrollViewer` bao ngoài `ListBox`, giữ viewport hữu hạn và bật cuộn pixel, virtualization/recycling. Chuyển cuộn khi kéo thả sang `ScrollViewer` nội bộ; dùng `ScrollIntoView` để mục được chọn ngoài viewport vẫn hiện ra. Thư viện 526+ mẫu và danh sách profile game đã có cơ chế ảo hóa phù hợp.
- Gỡ hai tệp `SystemTrayToastWindow` cũ đã bị loại khỏi build và bỏ khai báo loại trừ tương ứng trong `.csproj`. Giữ `ToastViewModel` vì vẫn dùng cho thông báo nội bộ; Windows notification tiếp tục do Windows App SDK quản lý. Guard đóng popup khi cuộn được gắn ở cửa sổ Settings chung nên bao phủ các tab. Giữ font `Ideal`, tài nguyên màu đã Freeze, trạng thái chip chọn xanh sẫm dạng nền đặc để chữ vẫn rõ trên ảnh nền sáng; không đổi đồng loạt palette/card hoặc package còn dùng.
- `dotnet test` Release: **808 pass, 1 skip, 0 fail**; `git diff --check` sạch. Đây là rà soát mã và test chức năng, chưa phải phép đo profiler chứng minh giảm RAM, FPS hoặc triệt tiêu mọi rò rỉ trong phiên dài.
