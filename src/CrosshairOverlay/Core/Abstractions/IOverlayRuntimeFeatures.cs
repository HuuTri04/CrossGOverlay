namespace CrosshairOverlay.Core.Abstractions;

/// <summary>Optional clocks owned by the concrete overlay window lifecycle.</summary>
public interface IOverlayRuntimeFeatures
{
    void ConfigureOledPixelShift(bool enabled);
    void ConfigureSmartContrast(bool enabled);
}
