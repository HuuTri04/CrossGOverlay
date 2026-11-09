using System.Text.Json.Serialization;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.System;

namespace CrosshairOverlay.Services.Storage;

// Only types with properties visible to the compiler are eligible here. The MVVM
// source generator emits AppSettings/CrosshairProfile properties too late for the
// JSON generator to see them, so those types must use the reflection fallback.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(List<CatalogPreset>))]
[JsonSerializable(typeof(StatsHistoryData))]
[JsonSerializable(typeof(LegacyUsageStatsData))]
internal partial class AppJsonSerializerContext : JsonSerializerContext;
