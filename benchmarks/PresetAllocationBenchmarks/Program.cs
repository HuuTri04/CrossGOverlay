using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using CrosshairOverlay.Services.Presets;
using CrosshairOverlay.Services.Rendering;
using Microsoft.Extensions.Logging.Abstractions;

BenchmarkSwitcher.FromAssembly(typeof(PresetAllocationBenchmarks).Assembly).Run(args);

[MemoryDiagnoser]
public class PresetAllocationBenchmarks
{
    private PresetLibrary _library = null!;
    private CrosshairRenderer _renderer = null!;
    private CrosshairProfile _first = null!;
    private CrosshairProfile _second = null!;
    private bool _toggle;

    [GlobalSetup]
    public void Setup()
    {
        _first = CrosshairProfile.CreateDefault();
        _second = CrosshairProfile.CreateDefault();
        _library = new PresetLibrary(new Repository(_first, _second), NullLogger<PresetLibrary>.Instance);
        _library.InitializeAsync(_first.Id).GetAwaiter().GetResult();
        _renderer = new CrosshairRenderer(NullLogger<CrosshairRenderer>.Instance);
        _renderer.Build(_first, CrosshairRenderOptions.Default);
        _renderer.Build(_second, CrosshairRenderOptions.Default);
    }

    [Benchmark]
    public void SetActiveWithoutSubscribers()
    {
        _toggle = !_toggle;
        _library.SetActive(_toggle ? _first : _second);
    }

    [Benchmark]
    public object BuildWithWarmGeometryAndPaintCache()
    {
        _toggle = !_toggle;
        return _renderer.Build(_toggle ? _first : _second, CrosshairRenderOptions.Default);
    }

    [GlobalCleanup]
    public void Cleanup() => _library.Dispose();

    private sealed class Repository(params CrosshairProfile[] presets) : IPresetRepository
    {
        public bool LastLoadSkippedFiles => false;
        public event EventHandler? PresetsChanged { add { } remove { } }
        public Task<IReadOnlyList<CrosshairProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CrosshairProfile>>(presets);
        public Task<CrosshairProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(presets.FirstOrDefault(p => p.Id == id));
        public Task SaveAsync(CrosshairProfile profile, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
