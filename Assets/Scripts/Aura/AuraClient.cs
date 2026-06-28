using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Aura
{
    /// <summary>
    /// WebSocket client that connects the Unity game to the Aura Core (Python) edge brain.
    /// Receives driver-state events ({type, timestamp, payload}) and forwards them on the main
    /// thread via <see cref="OnMessage"/>. Sends vehicle telemetry / commands back with <see cref="Send"/>.
    /// Pure transport — it does not react to messages itself (see AuraDemoReactor).
    /// </summary>
    public class AuraClient : MonoBehaviour
    {
        [Header("Connection")]
        [Tooltip("Aura Core WebSocket endpoint. Default is the local edge brain.")]
        public string url = "ws://127.0.0.1:8765";
        public bool autoReconnect = true;
        public float reconnectDelay = 1f;

        /// <summary>Fired on the main thread for every inbound message: (type, payload).</summary>
        public event Action<string, JObject> OnMessage;

        public bool IsConnected => _connected;

        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private readonly ConcurrentQueue<string> _inbox = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _outbox = new ConcurrentQueue<string>();
        private volatile bool _connected;
        private bool _connecting;
        private float _reconnectTimer;

        private void OnEnable() => Connect();
        private void OnDisable() => Disconnect();

        public async void Connect()
        {
            if (_connecting || _connected) return;
            Disconnect();
            _connecting = true;
            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();
            try
            {
                // Time-box the handshake so a hung connect can't wedge reconnect forever.
                using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                connectCts.CancelAfter(5000);
                await _ws.ConnectAsync(new Uri(url), connectCts.Token);
                _connected = true;
                Debug.Log($"[Aura] Connected to {url}");
                _ = ReceiveLoop(_cts.Token);
                _ = SendLoop(_cts.Token);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Aura] Connect failed ({url}): {e.Message}");
                _connected = false;
            }
            finally
            {
                _connecting = false;
            }
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buffer = new byte[8192];
            var sb = new StringBuilder();
            try
            {
                while (_ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    sb.Clear();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            _connected = false;
                            return;
                        }
                        sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    } while (!result.EndOfMessage);

                    _inbox.Enqueue(sb.ToString());
                }
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                Debug.LogWarning($"[Aura] Receive error: {e.Message}");
            }
            _connected = false;
        }

        private async Task SendLoop(CancellationToken ct)
        {
            try
            {
                while (_ws != null && _ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    if (_outbox.TryDequeue(out var msg))
                    {
                        var bytes = Encoding.UTF8.GetBytes(msg);
                        await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
                    }
                    else
                    {
                        await Task.Delay(15, ct);
                    }
                }
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                Debug.LogWarning($"[Aura] Send error: {e.Message}");
            }
        }

        private void Update()
        {
            // Drain inbound messages on the main thread so handlers can touch Unity objects safely.
            while (_inbox.TryDequeue(out var raw))
            {
                try
                {
                    var obj = JObject.Parse(raw);
                    var type = obj.Value<string>("type") ?? string.Empty;
                    var payload = obj["payload"] as JObject ?? new JObject();
                    OnMessage?.Invoke(type, payload);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Aura] Bad message dropped: {e.Message}");
                }
            }

            if (autoReconnect && !_connected && !_connecting)
            {
                _reconnectTimer -= Time.deltaTime;
                if (_reconnectTimer <= 0f)
                {
                    _reconnectTimer = reconnectDelay;
                    Connect();
                }
            }
        }

        /// <summary>Queue an outbound message. Envelope is {type, timestamp, payload}.</summary>
        public void Send(string type, object payload)
        {
            var envelope = new { type, timestamp = DateTime.UtcNow.ToString("o"), payload };
            _outbox.Enqueue(JsonConvert.SerializeObject(envelope));
        }

        public void Disconnect()
        {
            _connected = false;
            try { _cts?.Cancel(); } catch { /* ignore */ }
            try { _ws?.Dispose(); } catch { /* ignore */ }
            _ws = null;
            _cts = null;
        }
    }
}
