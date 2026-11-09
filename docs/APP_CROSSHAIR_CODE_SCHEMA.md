# CGO-CH2 binary share code (v1)

Mã hiện hành có dạng `CGO-CH2;<Base64 không padding>;c<CRC32 hex 8 ký tự>`. Base64 chuẩn (`+`, `/`), không dùng GZip. CRC32 tính trên phần Base64 giữa dấu `;` đầu và `;c`, theo cùng quy tắc với mã `CGO-CH;` cũ. Decoder vẫn nhận mã cũ qua JSON/GZip. Byte đầu tiên của payload là phiên bản `0x01`.

Các số nguyên nhiều byte và `double` dùng thứ tự little-endian của .NET `BinaryWriter`. Mọi `double` giữ đủ 64 bit; không lượng tử hóa sang `short`, `float` hay phần trăm. Màu ghi bốn byte theo thứ tự A, R, G, B. Chuỗi dùng UTF-8 với độ dài tiền tố 7-bit của `BinaryWriter.Write(string)`.

| Thứ tự | Trường | Số byte / điều kiện |
| --- | --- | --- |
| 1 | Version | 1 byte, luôn `0x01` |
| 2 | Shape | 1 byte, giá trị `CrosshairShape` 0–6 |
| 3 | Profile flags | 1 byte: bit 0 Favorite, bit 1 có Color khác Lime, bit 2 có SchemaVersion khác 2; bit 3–7 phải bằng 0 |
| 4 | SchemaVersion | `Int32` nếu bit 2 bật |
| 5 | Profile Color | ARGB 4 byte nếu bit 1 bật |
| 6 | Name | UTF-8 với độ dài 7-bit |
| 7 | Thông số profile | 1 byte mask + các `double` khác mặc định theo thứ tự Opacity(1), Scale(1), Rotation(0), OffsetX(0), OffsetY(0) |
| 8 | InnerLines | 1 byte flags + 1 byte mask + các `double` khác mặc định |
| 9 | OuterLines | Cùng cấu trúc InnerLines, nhưng so với mặc định của `CreateOuter()` |
| 10 | CenterDot | 1 byte flags, ARGB nếu màu khác Lime, 1 byte mask + các `double` khác mặc định |
| 11 | Outline | 1 byte flags, ARGB nếu màu khác Black, 1 byte mask + các `double` khác mặc định |
| 12 | Ring | 1 byte flags + 1 byte mask + các `double` khác mặc định |
| 13 | Shadow | 1 byte flags, ARGB nếu màu khác Black, 1 byte mask + các `double` khác mặc định |
| 14 | Image settings | 1 byte flags, đường dẫn UTF-8 nếu có, 1 byte mask + các `double` khác mặc định |

Trong mỗi nhóm số, bit `n` của mask cho biết giá trị thứ `n` được ghi bằng một `double` 8 byte; bit tắt nghĩa là dùng giá trị mặc định ghi dưới đây. Bit ngoài phạm vi nhóm bị từ chối. Các bool được gộp trong byte flags:

| Nhóm | Bit flags (từ bit 0) | Thứ tự `double` và mặc định |
| --- | --- | --- |
| InnerLines / OuterLines | Enabled, SeparateVerticalLength, SeparateVerticalThickness, ShowTop, ShowBottom, ShowLeft, ShowRight, RoundedCaps | Opacity, Length, VerticalLength, Thickness, VerticalThickness, Offset; mặc định theo `CreateInner()` / `CreateOuter()` |
| CenterDot | Enabled, UseProfileColor, có Color khác Lime | Size(3), Opacity(1) |
| Outline | Enabled, có Color khác Black | Thickness(1), Opacity(1) |
| Ring | Enabled, Filled | Radius(12), Thickness(2) |
| Shadow | Enabled, có Color khác Black | BlurRadius(4), Opacity(0.7) |
| Image settings | có FilePath (phân biệt `null` và chuỗi rỗng) | Scale(1), Opacity(1), OffsetX(0), OffsetY(0) |

Mã `CGO-CH2` chỉ được tạo cho tâm ngắm vẽ (`Type=Standard`). Tùy chọn ảnh không hoạt động vẫn được lưu để không mất dữ liệu khi đổi loại sau này. `EmbeddedImage` không đi theo mã này, như ở định dạng cũ. Khi nhập, ứng dụng cấp `Id` và thời gian mới để không ghi đè preset trong thư viện; các metadata đó không có trong payload. Payload vượt 256 KiB, version/flags lạ, số không hữu hạn hoặc byte thừa đều bị từ chối.

Để thêm trường mới, cần tăng byte version và tạo nhánh decoder riêng; không thay đổi ý nghĩa hay thứ tự byte của v1.
