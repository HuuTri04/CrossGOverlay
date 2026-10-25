using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;
using CrosshairOverlay.Core.Abstractions;
using CrosshairOverlay.Core.Models;
using Microsoft.Extensions.Logging;

namespace CrosshairOverlay.Services.System;

/// <summary>OBS WebSocket v5 output observer. It is completely idle when Stream Mode is off.</summary>
public sealed class ObsWebSocketService : IDisposable
{
    private readonly IAppSettingsService _settings;
    private readonly ILogger<ObsWebSocketService> _logger;
    private readonly Dispatcher _dispatcher;
    private readonly Uri _endpoint;
    private CancellationTokenSource? _runCancellation;
    private string _password = string.Empty; // Session-only: never persisted to settings or logs.
    private bool _started;
    private bool _streaming;
    private bool _recording;

    public ObsWebSocketService(IAppSettingsService settings, ILogger<ObsWebSocketService> logger)
        : this(settings, logger, new Uri("ws://127.0.0.1:4455")) { }

    internal ObsWebSocketService(IAppSettingsService settings, ILogger<ObsWebSocketService> logger, Uri endpoint)
    {
        _settings = settings;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _endpoint = endpoint;
    }

    public bool IsCapturing => _streaming || _recording;
    public event EventHandler? CaptureChanged;

    public void Start()
    {
        if (_started) return;
        _started = true;
        _settings.Current.PropertyChanged += OnSettingsChanged;
        Refresh();
    }

    public void SetPassword(string password)
    {
        if (string.Equals(_password, password, StringComparison.Ordinal) && _runCancellation is not null) return;
        _password = password;
        if (_started && _settings.Current.StreamModeEnabled) Restart();
    }

    private void OnSettingsChanged(object? sender, global::System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppSettings.StreamModeEnabled)) Refresh();
    }

    private void Refresh()
    {
        if (!_started) return;
        if (!_settings.Current.StreamModeEnabled)
        {
            StopConnection();
            SetCapture(false, false);
        }
        else if (_runCancellation is null) Restart();
    }

    private void Restart()
    {
        StopConnection();
        SetCapture(false, false);
        var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        _ = RunAsync(cancellation);
    }

    private void StopConnection()
    {
        var cancellation = _runCancellation;
        _runCancellation = null;
        cancellation?.Cancel();
        // RunAsync owns and disposes the socket and token source after cancellation.
    }

    private async Task RunAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var socket = new ClientWebSocket();
                    await socket.ConnectAsync(_endpoint, token);
                    using var hello = await ReadAsync(socket, token);
                    if (hello.RootElement.GetProperty("op").GetInt32() != 0) throw new InvalidDataException("OBS Hello missing");
                    var data = hello.RootElement.GetProperty("d");
                    var identify = new Dictionary<string, object> { ["rpcVersion"] = 1, ["eventSubscriptions"] = 64 };
                    if (data.TryGetProperty("authentication", out var auth))
                    {
                        if (_password.Length == 0) throw new InvalidOperationException("OBS WebSocket password required");
                        identify["authentication"] = CreateAuthentication(_password,
                            auth.GetProperty("salt").GetString()!, auth.GetProperty("challenge").GetString()!);
                    }
                    await SendAsync(socket, new { op = 1, d = identify }, token);
                    using var identified = await ReadAsync(socket, token);
                    if (identified.RootElement.GetProperty("op").GetInt32() != 2)
                        throw new InvalidDataException("OBS identification failed");

                    await SendAsync(socket, new { op = 6, d = new { requestType = "GetStreamStatus", requestId = "stream" } }, token);
                    await SendAsync(socket, new { op = 6, d = new { requestType = "GetRecordStatus", requestId = "record" } }, token);
                    while (!token.IsCancellationRequested && socket.State == WebSocketState.Open)
                    {
                        using var message = await ReadAsync(socket, token);
                        HandleMessage(message.RootElement, token);
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception ex) when (ex is WebSocketException or InvalidDataException or InvalidOperationException
                                              or JsonException or IOException or KeyNotFoundException or ObjectDisposedException)
                {
                    _logger.LogDebug("OBS WebSocket unavailable: {Reason}", ex.Message);
                }
                _ = _dispatcher.BeginInvoke(() =>
                {
                    if (!token.IsCancellationRequested) SetCapture(false, false);
                });
                await Task.Delay(TimeSpan.FromSeconds(15), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { cancellation.Dispose(); }
    }

    private void HandleMessage(JsonElement root, CancellationToken token)
    {
        if (!root.TryGetProperty("op", out var opcode)) return;
        var op = opcode.GetInt32();
        if (op is not (5 or 7)) return;
        if (!root.TryGetProperty("d", out var data)) return;
        if (!data.TryGetProperty(op == 5 ? "eventType" : "requestType", out var typeElement)) return;
        var type = typeElement.GetString();
        if (!data.TryGetProperty(op == 5 ? "eventData" : "responseData", out var state)) return;
        if (!state.TryGetProperty("outputActive", out var active)) return;
        var value = active.GetBoolean();
        if (type is "StreamStateChanged" or "GetStreamStatus")
            _ = _dispatcher.BeginInvoke(() =>
            {
                if (!token.IsCancellationRequested) SetCapture(value, _recording);
            });
        else if (type is "RecordStateChanged" or "GetRecordStatus")
            _ = _dispatcher.BeginInvoke(() =>
            {
                if (!token.IsCancellationRequested) SetCapture(_streaming, value);
            });
    }

    private void SetCapture(bool streaming, bool recording)
    {
        var old = IsCapturing;
        _streaming = streaming;
        _recording = recording;
        if (old != IsCapturing) CaptureChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string CreateAuthentication(string password, string salt, string challenge)
    {
        var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    private static async Task SendAsync(ClientWebSocket socket, object payload, CancellationToken token)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
    }

    private static async Task<JsonDocument> ReadAsync(ClientWebSocket socket, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(chunk, token);
            if (result.MessageType == WebSocketMessageType.Close) throw new IOException("OBS disconnected");
            buffer.Write(chunk, 0, result.Count);
            if (buffer.Length > 65536) throw new InvalidDataException("OBS message too large");
        } while (!result.EndOfMessage);
        return JsonDocument.Parse(buffer.ToArray());
    }

    public void Dispose()
    {
        if (!_started) return;
        _started = false;
        _settings.Current.PropertyChanged -= OnSettingsChanged;
        StopConnection();
        SetCapture(false, false);
    }
}
