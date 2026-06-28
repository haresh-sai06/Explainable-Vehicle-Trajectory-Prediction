using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Aura
{
    /// <summary>
    /// Minimal stub that reacts to Aura Core driver-state events so the loop is provable
    /// before the Python brain exists. Shows an on-screen banner + logs to the console.
    /// Debug keys inject fake events locally (no Aura Core needed) to test the reaction path.
    /// Later, the safety.alert branch will call into the car controller to actually pull over.
    /// </summary>
    [RequireComponent(typeof(AuraClient))]
    public class AuraDemoReactor : MonoBehaviour
    {
        [Header("Debug (works without Aura Core)")]
        [Tooltip("Inject a fake drowsiness alert.")]
        public KeyCode debugAlertKey = KeyCode.K;
        [Tooltip("Inject a fake driver-identified event.")]
        public KeyCode debugIdentifyKey = KeyCode.J;
        [Tooltip("Inject a fake resume (clear the pull-over).")]
        public KeyCode debugResumeKey = KeyCode.L;

        [Header("Vehicle")]
        [Tooltip("The self-driving car to pull over. Auto-found if left empty.")]
        public onnxcontroller vehicle;

        private AuraClient _client;
        private string _banner = string.Empty;
        private float _bannerUntil;

        private void Awake()
        {
            _client = GetComponent<AuraClient>();
            if (vehicle == null) vehicle = FindFirstObjectByType<onnxcontroller>();
        }
        private void OnEnable() => _client.OnMessage += HandleMessage;
        private void OnDisable() => _client.OnMessage -= HandleMessage;

        private void Update()
        {
            if (Input.GetKeyDown(debugAlertKey))
            {
                HandleMessage("safety.alert", JObject.FromObject(new
                {
                    level = "critical",
                    reason = "Eyes closed 3.2s (your baseline threshold 2.4s)",
                    action = "pull_over",
                    modality = "audio"
                }));
            }

            if (Input.GetKeyDown(debugIdentifyKey))
            {
                HandleMessage("driver.identified", JObject.FromObject(new
                {
                    name = "Haresh",
                    playlist = "Focus Drive"
                }));
            }

            if (Input.GetKeyDown(debugResumeKey))
            {
                HandleMessage("safety.clear", new JObject());
            }
        }

        private void HandleMessage(string type, JObject payload)
        {
            switch (type)
            {
                case "driver.identified":
                    var name = payload.Value<string>("name") ?? "Driver";
                    Show($"Welcome, {name}", 4f);
                    Debug.Log($"[Aura] driver.identified -> {name}");
                    break;

                case "safety.alert":
                    var reason = payload.Value<string>("reason") ?? string.Empty;
                    var action = payload.Value<string>("action") ?? string.Empty;
                    Show($"AURA: WAKE UP  ({action})\n{reason}", 6f);
                    Debug.Log($"[Aura] safety.alert -> {action} | {reason}");
                    if (action == "pull_over" && vehicle != null)
                        vehicle.SetEmergencyStop(true);
                    break;

                case "safety.clear":
                    Show("Driver alert — resuming drive", 3f);
                    Debug.Log("[Aura] safety.clear -> resume");
                    if (vehicle != null)
                        vehicle.SetEmergencyStop(false);
                    break;
            }
        }

        private void Show(string message, float seconds)
        {
            _banner = message;
            _bannerUntil = Time.time + seconds;
        }

        private void OnGUI()
        {
            bool connected = _client != null && _client.IsConnected;
            GUI.color = connected ? Color.green : new Color(0.6f, 0.6f, 0.6f);
            GUI.Label(new Rect(12, 10, 260, 22), connected ? "● Aura Core connected" : "○ Aura Core offline");
            GUI.color = Color.white;
            GUI.Label(new Rect(12, 30, 460, 22), $"[Aura demo]  {debugAlertKey} = alert   {debugIdentifyKey} = identify   {debugResumeKey} = resume");

            if (Time.time < _bannerUntil && !string.IsNullOrEmpty(_banner))
            {
                var style = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 18,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true
                };
                style.normal.textColor = Color.white;
                var prev = GUI.color;
                GUI.color = new Color(0.82f, 0.12f, 0.12f, 0.92f);
                GUI.Box(new Rect(Screen.width / 2f - 260f, 64f, 520f, 78f), _banner, style);
                GUI.color = prev;
            }
        }
    }
}
