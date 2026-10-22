using System.IO;
using System.Media;

namespace CrosshairOverlay.Services.Hardware;

/// <summary>Owns an optional short WAV cue and releases it with the service provider.</summary>
public sealed class AudioFeedbackService : IDisposable
{
    private SoundPlayer? _player;
    private MemoryStream? _stream;

    public void LoadWav(byte[] wav)
    {
        ArgumentNullException.ThrowIfNull(wav);
        Dispose();
        _stream = new MemoryStream(wav, writable: false);
        _player = new SoundPlayer(_stream);
        _player.Load();
    }

    public void Play() => _player?.Play();

    public void Dispose()
    {
        _player?.Stop();
        _player?.Dispose();
        _player = null;
        _stream?.Dispose();
        _stream = null;
    }
}
