# PX2 allocation benchmark — Release x64

Measured on 2026-11-06 with BenchmarkDotNet 0.15.8, .NET 8.0.31, Windows 11 x64. Command:

```powershell
dotnet run --project benchmarks/PresetAllocationBenchmarks/PresetAllocationBenchmarks.csproj -c Release -- --filter '*Px2AllocationBenchmarks*' --job Short
```

Both methods compress the same deterministic random canvas with GZip Fastest. `PreviousGrowingStreamAndBase64` reconstructs the former growing `MemoryStream`, intermediate Base64 string and concatenation path. `CurrentPooledPx2` calls the current `PixelArtCode.Encode`. The input array is created during global setup, outside the measured operation.

| Canvas | Former allocation/op | Current allocation/op | Reduction | Former mean | Current mean |
| --- | ---: | ---: | ---: | ---: | ---: |
| 256×256 | 2,383.57 KiB | 683.55 KiB | 71.3% | 5.087 ms | 5.046 ms |
| 1024×1024 | 38,229.19 KiB | 10,926.43 KiB | 71.4% | 89.226 ms | 79.914 ms |

The final PX2 string still requires a large allocation, so zero allocation is impossible with this API. The short job has three measured iterations and wide timing confidence intervals; these data support the managed allocation reduction, not a reliable speedup claim. `ArrayPool<byte>` retains buffers for reuse, which can keep process memory above the live managed payload. The raw BenchmarkDotNet CSV and Markdown are under `BenchmarkDotNet.Artifacts/results/` in this workspace.
