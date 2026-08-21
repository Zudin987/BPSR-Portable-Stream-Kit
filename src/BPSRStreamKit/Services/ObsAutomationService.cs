using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BPSRStreamKit.Infrastructure;

namespace BPSRStreamKit.Services;

public sealed class ObsAutomationService
{
    public const int Port = 4455;
    private static string KeyFile => Path.Combine(AppPaths.Root, "user-data", "obs-websocket.key");

    public static string GetOrCreatePassword()
    {
        try
        {
            if (File.Exists(KeyFile))
            {
                var existing = File.ReadAllText(KeyFile).Trim();
                if (existing.Length >= 20) return existing;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(KeyFile)!);
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            File.WriteAllText(KeyFile, password);
            return password;
        }
        catch
        {
            // Still use a strong per-process credential even if the portable folder is read-only.
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        }
    }

    public static void EnsureServerConfig(string password)
    {
        var configRoot = AppPaths.ObsConfigRoot();
        if (configRoot is null) return;
        var path = Path.Combine(configRoot, "plugin_config", "obs-websocket", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        JsonObject root;
        try { root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject() : new JsonObject(); }
        catch { root = new JsonObject(); }

        root["first_load"] = false;
        root["server_enabled"] = true;
        root["server_port"] = Port;
        root["alerts_enabled"] = false;
        root["auth_required"] = true;
        root["server_password"] = password;
        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public async Task<bool> WaitUntilReadyAsync(TimeSpan? timeout = null)
    {
        var until = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(25));
        while (DateTime.UtcNow < until)
        {
            try
            {
                await using var session = await Session.ConnectAsync(GetOrCreatePassword(), TimeSpan.FromSeconds(3));
                return true;
            }
            catch { await Task.Delay(500); }
        }
        return false;
    }

    public async Task StartVirtualCameraAsync()
    {
        await WithSessionAsync(s => s.RequestAsync("StartVirtualCam"));
    }

    public async Task StopVirtualCameraAsync()
    {
        try { await WithSessionAsync(s => s.RequestAsync("StopVirtualCam")); } catch { }
    }

    public async Task StartAllStreamsAsync()
    {
        await CallAitumAsync("start_all_streams");
    }

    public async Task StopAllStreamsAsync()
    {
        try { await CallAitumAsync("stop_all_streams"); } catch { }
    }

    public async Task SwitchVerticalSceneAsync(string sceneName = "Vertical Live")
    {
        await CallAitumAsync("switch_scene", new JsonObject { ["canvas"] = "Vertical", ["scene"] = sceneName });
    }

    public async Task<string?> GetVerticalCanvasUuidAsync()
    {
        var response = await CallAitumAsync("get_canvas");
        var payload = response?["responseData"]?.AsObject() ?? response;
        var canvases = payload?["canvas"]?.AsArray();
        if (canvases is null) return null;
        foreach (var node in canvases.OfType<JsonObject>())
        {
            if (string.Equals(node["name"]?.GetValue<string>(), "Vertical", StringComparison.OrdinalIgnoreCase))
                return node["uuid"]?.GetValue<string>();
        }
        return null;
    }

    private async Task<JsonObject?> CallAitumAsync(string requestType, JsonObject? requestData = null)
    {
        return await WithSessionAsync(async s =>
        {
            var outer = new JsonObject
            {
                ["vendorName"] = "aitum-stream-suite",
                ["requestType"] = requestType,
                ["requestData"] = requestData ?? new JsonObject()
            };
            return await s.RequestAsync("CallVendorRequest", outer);
        });
    }

    private static async Task<T> WithSessionAsync<T>(Func<Session, Task<T>> action)
    {
        await using var session = await Session.ConnectAsync(GetOrCreatePassword(), TimeSpan.FromSeconds(8));
        return await action(session);
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket;
        private Session(ClientWebSocket socket) => _socket = socket;

        public static async Task<Session> ConnectAsync(string password, TimeSpan timeout)
        {
            var socket = new ClientWebSocket();
            using var cts = new CancellationTokenSource(timeout);
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{Port}"), cts.Token);
            var session = new Session(socket);

            var hello = await session.ReceiveObjectAsync(cts.Token);
            if (hello?["op"]?.GetValue<int>() != 0)
                throw new InvalidDataException("OBS WebSocket did not send a Hello message.");

            var identify = new JsonObject { ["rpcVersion"] = 1 };
            var auth = hello["d"]?["authentication"]?.AsObject();
            if (auth is not null)
            {
                var challenge = auth["challenge"]?.GetValue<string>() ?? string.Empty;
                var salt = auth["salt"]?.GetValue<string>() ?? string.Empty;
                var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
                var response = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
                identify["authentication"] = response;
            }

            await session.SendObjectAsync(new JsonObject { ["op"] = 1, ["d"] = identify }, cts.Token);
            var identified = await session.ReceiveObjectAsync(cts.Token);
            if (identified?["op"]?.GetValue<int>() != 2)
                throw new InvalidDataException("OBS WebSocket authentication failed.");
            return session;
        }

        public async Task<JsonObject?> RequestAsync(string requestType, JsonObject? requestData = null)
        {
            var requestId = Guid.NewGuid().ToString("N");
            var d = new JsonObject { ["requestType"] = requestType, ["requestId"] = requestId };
            if (requestData is not null) d["requestData"] = requestData;
            await SendObjectAsync(new JsonObject { ["op"] = 6, ["d"] = d }, CancellationToken.None);

            while (true)
            {
                var message = await ReceiveObjectAsync(CancellationToken.None);
                if (message?["op"]?.GetValue<int>() != 7) continue;
                var body = message["d"]?.AsObject();
                if (!string.Equals(body?["requestId"]?.GetValue<string>(), requestId, StringComparison.Ordinal)) continue;
                var status = body?["requestStatus"]?.AsObject();
                if (status?["result"]?.GetValue<bool>() != true)
                {
                    var comment = status?["comment"]?.GetValue<string>() ?? $"OBS request '{requestType}' failed.";
                    throw new InvalidOperationException(comment);
                }
                return body?["responseData"]?.AsObject();
            }
        }

        private async Task SendObjectAsync(JsonObject value, CancellationToken token)
        {
            var bytes = Encoding.UTF8.GetBytes(value.ToJsonString());
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
        }

        private async Task<JsonObject?> ReceiveObjectAsync(CancellationToken token)
        {
            using var stream = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var result = await _socket.ReceiveAsync(buffer, token);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new WebSocketException("OBS WebSocket closed the connection.");
                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage) break;
            }
            return JsonNode.Parse(Encoding.UTF8.GetString(stream.ToArray()))?.AsObject();
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_socket.State == WebSocketState.Open)
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            }
            catch { }
            _socket.Dispose();
        }
    }
}
