# RC2 — preset allocation và độ ổn định tài nguyên

Ngày đo: 2026-11-05 và 2026-11-06. Môi trường: Windows 11, .NET 8, CrossGOverlay Release x64; chạy ngoài game. Các số dưới đây là dữ liệu đo thật trên máy này, không phải cam kết cho mọi GPU hoặc phiên chạy dài.

## Phương pháp tái lập

```powershell
dotnet run -c Release --project benchmarks/PresetAllocationBenchmarks/PresetAllocationBenchmarks.csproj
dotnet run -c Release --project benchmarks/AllocationTraceSummary/AllocationTraceSummary.csproj -- benchmarks/results/stress-20261105-222918/preset-alloc.nettrace
powershell -NoProfile -ExecutionPolicy Bypass -File benchmarks/Invoke-Stress.ps1 -Scenario preset-multistage
dotnet test tests/CrosshairOverlay.Tests/CrosshairOverlay.Tests.csproj -c Release -p:Platform=x64 -p:DefineConstants=STRESS --filter "FullyQualifiedName~PresetResourceStabilityTests"
```

Stress chạy trong tiến trình ứng dụng thật, với Settings được `Hide()` sau khởi động. Mốc là 1.000 lượt warmup, rồi 3 × 5.000 lượt đổi preset, xen 1 ms nhường UI sau mỗi 100 lượt. Chỉ fixture stress gọi `GC.Collect()` trước từng mẫu; mã sản xuất không ép GC. `GetGuiResources` đo GDI trên HWND thật; bài xUnit cô lập renderer không có HWND nên số GDI bằng 0 và chỉ dùng để kiểm tra phần thư viện/render trong testhost. Dùng cùng máy, cùng dữ liệu cô lập và lặp lại bốn tiến trình mới.

## Allocation stack và thay đổi có mục tiêu

| Phép đo | Kết quả | Ý nghĩa |
| --- | ---: | --- |
| BenchmarkDotNet `SetActive` không subscriber | 3,007 ns; 0 B/op | Gán preset và phát `EventArgs.Empty` không tạo 220 KiB. |
| `CrosshairRenderer.Build` với geometry/paint cache đã warm | 7,977 µs; 3.944 B/op | Cache được dùng, nhưng bản vẽ `DrawingGroup` mới vẫn có chi phí. Không phải nguồn 220 KiB. |
| EventPipe `gc-verbose` 15 giây | 277 allocation ticks mẫu | 16 mẫu stack phổ biến nhất đi qua `PropertyPathWorker.ReplaceItem` → `SettingsViewModel.SyncFromLibrary`; thêm mẫu `PropertyChangedEventManager.AddListener/RemoveListener`. |

`GCAllocationTick` là **mẫu**, không thể cộng cột sampled bytes thành phân rã chính xác theo loại. Trace chỉ ra việc đổi `Editor.Profile` khiến WPF tháo/gắn lại hàng loạt binding khi cửa sổ Settings đang hiện. Mã cũ tiếp tục làm việc này cả khi Settings ẩn. `SettingsViewModel` nay tạm ngưng đồng bộ editor/selection khi Settings ẩn hoặc tab Crosshair không được chọn, và đồng bộ lại trước khi hiện cửa sổ hoặc khi quay về tab. Luồng `ActiveChanged` của overlay, game và thống kê vẫn chạy. Không thay `EventArgs.Empty` hoặc cấu trúc geometry cache vì benchmark không cho thấy chúng là nguyên nhân chính.

Lần đo cũ xấp xỉ 225 KiB/lượt đã chạy với **Settings hiện**: tham số `--tray` trong script cũ không được ứng dụng xử lý. Vì vậy không được gọi chênh lệch 225 KiB → 2,76 KiB là phép so sánh A/B hoàn toàn đồng điều kiện. Với Settings ẩn thực sự, bốn lần stress mới cho khoảng 2,76–2,77 KiB/lượt sau warmup. Khi người dùng đang xem editor Crosshair, WPF vẫn phải cập nhật binding.

Đo lại riêng đường **Settings hiện** sau thay đổi, bằng fixture `preset` 10.000 lượt/`Task.Delay(5)`: [CSV thực tế](benchmarks/results/stress-20261106-112051/preset/diagnostics/stress-preset.csv) ghi tổng byte cấp phát 22.766.896 → 2.276.768.768, tức **225.400 B/lượt (~220,12 KiB/lượt)**. GDI 44 → 50 sau GC; Private Bytes 191.561.728 → 252.993.536 B. Phép đo này xác nhận chi phí binding khi editor đang hiện **vẫn còn**; tối ưu hiện tại chỉ loại bỏ công việc vô ích khi Settings ẩn hoặc tab khác được chọn. Thời gian 284.223 ms bao gồm 10.000 lần `Task.Delay(5)` và scheduler, không phải latency của `SetActive`.

## Stress đa vòng, dữ liệu sau GC

| Lượt | GDI warm → cuối | Private Bytes warm → cuối | Chênh Private | Private vòng 1 → vòng 3 | Managed warm → cuối | RC2 |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| [1](benchmarks/results/stress-20261105-223814/preset-multistage/diagnostics/stress-preset-multistage.csv) | 44 → 44 | 240.291.840 → 256.405.504 B | +15,37 MiB | +0,54 MiB | 8.622.024 → 8.617.064 B | GDI PASS; Private FAIL |
| [2](benchmarks/results/stress-20261105-224145/preset-multistage/diagnostics/stress-preset-multistage.csv) | 44 → 44 | 239.788.032 → 255.868.928 B | +15,34 MiB | +0,66 MiB | 8.588.288 → 8.577.864 B | GDI PASS; Private FAIL |
| [3](benchmarks/results/stress-20261105-224200/preset-multistage/diagnostics/stress-preset-multistage.csv) | 44 → 44 | 240.549.888 → 256.249.856 B | +14,97 MiB | −0,06 MiB | 8.609.640 → 8.598.952 B | GDI PASS; Private FAIL |
| [4](benchmarks/results/stress-20261105-224336/preset-multistage/diagnostics/stress-preset-multistage.csv) | 44 → 44 | 175.448.064 → 245.202.944 B | +66,52 MiB | +63,81 MiB | 8.601.576 → 8.599.016 B | GDI PASS; Private FAIL |

Ngưỡng yêu cầu: GDI cuối ≤ warmup +2; Private Bytes cuối ≤ warmup +1 MiB. Trong bốn lượt đầu, GDI đạt 4/4 và Private Bytes **không đạt 4/4**. Ba lượt đầu tăng chủ yếu giữa warmup và vòng đầu, sau đó gần phẳng. Lượt thứ tư bắt đầu với Private Bytes thấp hơn ~61 MiB và có bước nhảy ~61 MiB giữa vòng 1 và 2; như vậy 1.000 lượt warmup chưa luôn ổn định hóa các cấp phát native bất đồng bộ. Lượt ETW elevated ở phần dưới xác định chủ sở hữu graphics stack của hai vùng 61 MiB trên cùng máy. Chưa đủ dữ liệu để loại trừ mọi rò rỉ trong phiên dài hơn hoặc trên GPU khác.

Lượt thử ban đầu với tài khoản không có quyền System Profiling bị `0x80070005 Access is denied`. Sau đó script được chạy từ PowerShell Administrator và đã xuất ETL hợp lệ.

Script đã chuẩn bị để chạy từ PowerShell Administrator, tự dừng WPR trong `finally` và giữ ETL/CSV ngay cả khi ngưỡng stress FAIL:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File benchmarks/Collect-RC2VirtualAlloc.ps1
```

Có thể phân tích lại ETL bằng `dotnet run -c Release --project benchmarks/NativeAllocationTraceSummary/NativeAllocationTraceSummary.csproj -- <đường-dẫn-etl>` hoặc WPA.

## ETW elevated ngày 2026-11-06 — chủ sở hữu hai vùng 61 MiB

Trace [VirtualAllocation ETL](benchmarks/results/rc2-native-20261106-115012/CrossGOverlay_RC2_VirtualAllocation.etl) của PID 36964 ghi đúng **hai địa chỉ vùng riêng biệt** có `MEM_RESERVE` rồi `MEM_COMMIT` mỗi vùng 64.004.096 B (61,039 MiB). Reserve và commit trên cùng địa chỉ là **một vùng**, không cộng đôi. Bản ghi cấp phát lớn nhất tiếp theo chỉ 5,52 MiB.

| Vùng | `MEM_COMMIT` (giờ máy) | Base address | `MEM_DECOMMIT`/`MEM_RELEASE` | Chủ sở hữu theo module stack |
| --- | --- | --- | --- | --- |
| 1 | 11:50:13.7253567 | `0x14b863b0000` | 11:50:21.8598 khi thoát | WPF `wpfgfx_cor3` → `d3d9` → `d3d9on12` → `d3d12core` → Intel `igd12um64xel` → `dxgkrnl.sys`/`dxgmms2.sys` |
| 2 | 11:50:17.3736401 | `0x14b92d80000` | 11:50:21.8664 khi thoát | Cùng chuỗi WPF/Direct3D/Intel graphics driver |

Không thấy vùng 61 MiB thứ ba trong 16.000 lượt đổi preset của trace này. Module stack chứng minh đây là cấp phát trên đường dựng hình WPF/Direct3D và driver đồ họa, **không phải mảng Managed Heap hay GDI Region do mỗi lần `SetActive` tạo ra**. ETL chưa giải symbol tên hàm và không có dấu mốc HWND tại callsite, nên chưa gán chắc từng vùng cho Overlay, Settings hay Toast. Việc vùng 2 xuất hiện muộn phù hợp với bước nhảy Private Bytes không ổn định ở bốn lượt trước; đây là suy luận đối chiếu, không phải kết quả stack riêng của những lượt đó. Không ép GC, thu Working Set hoặc đổi renderer để xóa các vùng graphics này.

[CSV cùng phiên](benchmarks/results/rc2-native-20261106-115012/stress/preset-multistage/diagnostics/stress-preset-multistage.csv): sau warmup 1.000 lượt, GDI 47 → 47 và Private Bytes 258.379.776 → 256.331.776 B sau 3×5.000 lượt, **cả hai ngưỡng PASS trong phiên elevated này**. Managed Heap 9.532.856 → 9.530.552 B. Vùng graphics thứ hai đã được commit trước mẫu warmup của phiên này; điều đó giải thích vì sao ngưỡng Private Bytes đạt ở lượt này nhưng không đạt ở bốn lượt trước.

Bài xUnit opt-in dùng cùng ngưỡng cũng FAIL: Private Bytes 30.351.360 → 31.461.376 B (+1.110.016 B); GDI 0 → 0 vì không dựng HWND. Không thay ngưỡng để làm xanh bài test. Lệnh PowerShell stress trả lỗi nếu gate FAIL nhưng vẫn giữ CSV và `StressTest_Report.txt` để đối chứng.

## Kiểm tra và giới hạn

- Bộ xUnit Release x64 thông thường: **839 pass, 1 skip, 0 fail**; bài stress ngưỡng nghiêm ngặt là opt-in (`STRESS`) và đang FAIL như trên.
- Đã quét đường tạo GDI: `GetHicon` đi với `DestroyIcon`, `GetDC` đi với `ReleaseDC`; không tìm thấy `GetHbitmap` hoặc `CreateIconIndirect` cần thêm `DeleteObject`. GDI giữ 44 trong bốn lượt thường và 47 trong lượt elevated sau warmup.
- Chưa đo GPU/VRAM hoặc input latency phần cứng. [UAT_CHECKLIST.md](UAT_CHECKLIST.md) bổ sung Wacom/Huion, chuột, 1024×1024, 60 nét Undo/Redo, High Contrast/Mica/Toast/Tray. Các mục đó vẫn cần thử thủ công.

Tham khảo phương pháp: [BenchmarkDotNet MemoryDiagnoser](https://benchmarkdotnet.org/Configs/Diagnosers.htm), [Microsoft dotnet-trace](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-trace).
