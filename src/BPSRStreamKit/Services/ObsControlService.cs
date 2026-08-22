using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace BPSRStreamKit.Services;

public sealed class ObsControlService
{
    public async Task<bool> IsMainStreamActiveAsync()
    {
        try
        {
            var response = await RequestAsync("GetStreamStatus");
            return response?["outputActive"]?.GetValue<bool>() == true;
        }
        catch { return false; }
    }

    public async Task StartMainStreamAsync()
    {
        if (await IsMainStreamActiveAsync()) return;
        await RequestAsync("StartStream");
    }

    public async Task StopMainStreamAsync()
    {
        try
        {
            if (await IsMainStreamActiveAsync()) await RequestAsync("StopStream");
        }
        catch { }
    }

    public async Task SyncSceneItemTransformAsync(string sourceScene, string destinationScene, string sourceName)
    {
        try
        {
            await using var session = await Session.ConnectAsync(TimeSpan.FromSeconds(8));
            var sourceItems = await session.RequestAsync("GetSceneItemList", new JsonObject { ["sceneName"] = sourceScene });
            var destinationItems = await session.RequestAsync("GetSceneItemList", new JsonObject { ["sceneName"] = destinationScene });
            var sourceItem = FindSceneItem(sourceItems, sourceName);
            var destinationItem = FindSceneItem(destinationItems, sourceName);
            var sourceId = sourceItem?["sceneItemId"]?.GetValue<int>() ?? 0;
            var destinationId = destinationItem?["sceneItemId"]?.GetValue<int>() ?? 0;
            if (sourceId <= 0 || destinationId <= 0) return;

            var transform = await session.RequestAsync("GetSceneItemTransform", new JsonObject
            {
                ["sceneName"] = sourceScene,
                ["sceneItemId"] = sourceId
            });
            var data = transform?["sceneItemTransform"]?.DeepClone();
            if (data is null) return;

            await session.RequestAsync("SetSceneItemTransform", new JsonObject
            {
                ["sceneName"] = destinationScene,
                ["sceneItemId"] = destinationId,
                ["sceneItemTransform"] = data
            });
        }
        catch { }
    }

    private static JsonObject? FindSceneItem(JsonObject? response, string sourceName) =>
        response?["sceneItems"]?.AsArray()?.OfType<JsonObject>()
            .FirstOrDefault(x => string.Equals(x["sourceName"]?.GetValue<string>(), sourceName, StringComparison.Ordinal));

    private static async Task<JsonObject?> RequestAsync(string requestType, JsonObject? requestData = null)
    {
        await using var session = await Session.ConnectAsync(TimeSpan.FromSeconds(8));
        return await session.RequestAsync(requestType, requestData);
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket;
        private Session(ClientWebSocket socket) => _socket = socket;

        public static async Task<Session> ConnectAsync(TimeSpan timeout)
        {
            var socket = new ClientWebSocket();
            using var cts = new CancellationTokenSource(timeout);
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{ObsAutomationService.Port}"), cts.Token);
            var session = new Session(socket);

            var hello = await session.ReceiveObjectAsync(cts.Token);
            if (hello?["op"]?.GetValue<int>() != 0)
                throw new InvalidDataException("OBS WebSocket did not send a Hello message.");

            var identify = new JsonObject { ["rpcVersion"] = 1 };
            var auth = hello["d"]?["authentication"]?.AsObject();
            if (auth is not null)
            {
                var password = ObsAutomationService.GetOrCreatePassword();
                var challenge = auth["challenge"]?.GetValue<string>() ?? string.Empty;
                var salt = auth["salt"]?.GetValue<string>() ?? string.Empty;
                var secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
                identify["authentication"] = Convert.ToBase64String(
                    SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
            }

            await session.SendObjectAsync(new JsonObject { ["op"] = 1, ["d"] = identify }, cts.Token);
            var identified = await session.ReceiveObjectAsync(cts.Token);
            if (identified?["op"]?.GetValue<int>() != 2)
                throw new InvalidDataException("OBS WebSocket authentication failed.");
            return session;
        }

        public async Task<JsonObject?> RequestAsync(string requestType, JsonObject? requestData = null)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            try
            {
                var requestId = Guid.NewGuid().ToString("N");
                var body = new JsonObject { ["requestType"] = requestType, ["requestId"] = requestId };
                if (requestData is not null) body["requestData"] = requestData;
                await SendObjectAsync(new JsonObject { ["op"] = 6, ["d"] = body }, cts.Token);

                while (true)
                {
                    var message = await ReceiveObjectAsync(cts.Token);
                    if (message?["op"]?.GetValue<int>() != 7) continue;
                    var data = message["d"]?.AsObject();
                    if (!string.Equals(data?["requestId"]?.GetValue<string>(), requestId, StringComparison.Ordinal)) continue;
                    var status = data?["requestStatus"]?.AsObject();
                    if (status?["result"]?.GetValue<bool>() != true)
                    {
                        var comment = status?["comment"]?.GetValue<string>() ?? $"OBS request '{requestType}' failed.";
                        throw new InvalidOperationException(comment);
                    }
                    return data?["responseData"]?.AsObject();
                }
            }
            catch (OperationCanceledException ex)
            {
                throw new TimeoutException($"OBS request '{requestType}' timed out.", ex);
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
