using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using BepInEx.Logging;

namespace AethermancerHarness
{
    public class HarnessServer
    {
        private readonly HttpListener _listener;
        private readonly Thread _listenerThread;
        private readonly ManualLogSource _logger;
        private volatile bool _running;
        private readonly int _port;

        public HarnessServer(ManualLogSource logger, int port = 8080)
        {
            _logger = logger;
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://+:{port}/");
            _listenerThread = new Thread(ListenLoop) { IsBackground = true };
        }

        public void Start()
        {
            try
            {
                _listener.Start();
                _running = true;
                _listenerThread.Start();
                _logger.LogInfo($"HTTP server started on port {_port}");
            }
            catch (Exception e)
            {
                _logger.LogError($"Failed to start HTTP server: {e.Message}");
                // Try localhost only as fallback
                try
                {
                    _listener.Prefixes.Clear();
                    _listener.Prefixes.Add($"http://localhost:{_port}/");
                    _listener.Start();
                    _running = true;
                    _listenerThread.Start();
                    _logger.LogInfo($"HTTP server started on localhost:{_port} (fallback)");
                }
                catch (Exception e2)
                {
                    _logger.LogError($"Failed to start HTTP server on localhost: {e2.Message}");
                }
            }
        }

        public void Stop()
        {
            _running = false;
            _listener.Stop();
            _listenerThread.Join(1000);
            _logger.LogInfo("HTTP server stopped");
        }

        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    var context = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
                }
                catch (HttpListenerException) when (!_running)
                {
                    // Normal shutdown
                }
                catch (Exception e)
                {
                    _logger.LogError($"Error in listener: {e.Message}");
                }
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            var request = context.Request;
            var response = context.Response;

            try
            {
                var path = request.Url.AbsolutePath.ToLower();
                var method = request.HttpMethod;

                _logger.LogInfo($"{method} {path}");

                string responseBody;
                int statusCode = 200;

                switch (path)
                {
                    case "/health":
                        responseBody = HandleHealth();
                        break;

                    case "/state":
                        var format = request.QueryString["format"] ?? "json";
                        responseBody = HandleState(format);
                        break;

                    case "/actions":
                        responseBody = HandleActions();
                        break;

                    case "/combat/action":
                        if (method == "POST")
                        {
                            var body = ReadBody(request);
                            responseBody = HandleCombatAction(body);
                        }
                        else
                        {
                            responseBody = "{\"error\": \"Method not allowed\"}";
                            statusCode = 405;
                        }
                        break;

                    case "/combat/preview":
                        if (method == "POST")
                        {
                            var body = ReadBody(request);
                            responseBody = HandleCombatPreview(body);
                        }
                        else
                        {
                            responseBody = "{\"error\": \"Method not allowed\"}";
                            statusCode = 405;
                        }
                        break;

                    case "/combat/enemy-actions":
                        responseBody = ActionHandler.GetEnemyActions();
                        break;

                    case "/exploration/teleport":
                        if (method == "POST")
                        {
                            var body = ReadBody(request);
                            responseBody = HandleTeleport(body);
                        }
                        else
                        {
                            responseBody = "{\"error\": \"Method not allowed\"}";
                            statusCode = 405;
                        }
                        break;

                    case "/exploration/interact":
                        if (method == "POST")
                        {
                            responseBody = HandleInteract();
                        }
                        else
                        {
                            responseBody = "{\"error\": \"Method not allowed\"}";
                            statusCode = 405;
                        }
                        break;

                    case "/combat/start":
                        if (method == "POST")
                        {
                            var body = ReadBody(request);
                            responseBody = HandleCombatStart(body);
                        }
                        else
                        {
                            responseBody = "{\"error\": \"Method not allowed\"}";
                            statusCode = 405;
                        }
                        break;

                    case "/skill-select":
                        if (method == "POST")
                        {
                            var body = ReadBody(request);
                            responseBody = HandleSkillSelect(body);
                        }
                        else
                        {
                            responseBody = "{\"error\": \"Method not allowed\"}";
                            statusCode = 405;
                        }
                        break;

                    default:
                        responseBody = "{\"error\": \"Not found\", \"endpoints\": [\"/health\", \"/state\", \"/actions\", \"/combat/action\", \"/combat/preview\", \"/combat/enemy-actions\", \"/combat/start\", \"/exploration/teleport\", \"/exploration/interact\", \"/skill-select\"]}";
                        statusCode = 404;
                        break;
                }

                SendResponse(response, responseBody, statusCode);
            }
            catch (Exception e)
            {
                _logger.LogError($"Error handling request: {e}");
                SendResponse(response, $"{{\"error\": \"{EscapeJson(e.Message)}\"}}", 500);
            }
        }

        private string ReadBody(HttpListenerRequest request)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                return reader.ReadToEnd();
            }
        }

        private void SendResponse(HttpListenerResponse response, string body, int statusCode)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            response.Headers.Add("Access-Control-Allow-Origin", "*");

            var buffer = Encoding.UTF8.GetBytes(body);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.Close();
        }

        // --- Endpoint Handlers ---

        private string HandleHealth()
        {
            var gameReady = GameController.Instance != null;
            var inCombat = GameStateManager.Instance?.IsCombat ?? false;
            var readyForInput = ActionHandler.IsReadyForInput();
            var inputStatus = ActionHandler.GetInputReadyStatus();

            return $"{{\"status\": \"ok\", \"gameReady\": {BoolToJson(gameReady)}, \"inCombat\": {BoolToJson(inCombat)}, \"readyForInput\": {BoolToJson(readyForInput)}, \"inputStatus\": \"{inputStatus}\"}}";
        }

        private string HandleState(string format)
        {
            if (format == "text")
            {
                return $"{{\"text\": \"{EscapeJson(StateSerializer.ToText())}\"}}";
            }
            return StateSerializer.ToJson();
        }

        private string HandleActions()
        {
            return StateSerializer.GetValidActionsJson();
        }

        private string HandleCombatAction(string body)
        {
            // Parse the action from the body
            // Skill format: {"actorIndex": 0, "skillIndex": 0, "targetIndex": 0}
            // Consumable format: {"consumableIndex": 0, "targetIndex": 0}
            try
            {
                var consumableIndex = ParseIntFromJson(body, "consumableIndex");
                var skillIndex = ParseIntFromJson(body, "skillIndex");

                // Reject ambiguous requests with both
                if (consumableIndex >= 0 && skillIndex >= 0)
                    return "{\"error\": \"Cannot specify both consumableIndex and skillIndex\"}";

                // Consumable action
                if (consumableIndex >= 0)
                {
                    var targetIndex = ParseIntFromJson(body, "targetIndex");
                    return ActionHandler.ExecuteConsumableAction(consumableIndex, targetIndex);
                }

                // Skill action (existing behavior)
                var actorIndex = ParseIntFromJson(body, "actorIndex");
                var targetIndex2 = ParseIntFromJson(body, "targetIndex");
                return ActionHandler.ExecuteCombatAction(actorIndex, skillIndex, targetIndex2);
            }
            catch (Exception e)
            {
                return $"{{\"error\": \"Failed to execute action: {EscapeJson(e.Message)}\"}}";
            }
        }

        private string HandleCombatPreview(string body)
        {
            // Parse the preview request
            // Skill format: {"actorIndex": 0, "skillIndex": 0, "targetIndex": 0}
            // Consumable format: {"consumableIndex": 0, "targetIndex": 0}
            try
            {
                var consumableIndex = ParseIntFromJson(body, "consumableIndex");
                var skillIndex = ParseIntFromJson(body, "skillIndex");

                // Reject ambiguous requests with both
                if (consumableIndex >= 0 && skillIndex >= 0)
                    return "{\"error\": \"Cannot specify both consumableIndex and skillIndex\"}";

                // Consumable preview
                if (consumableIndex >= 0)
                {
                    var targetIndex = ParseIntFromJson(body, "targetIndex");
                    return ActionHandler.ExecuteConsumablePreview(consumableIndex, targetIndex);
                }

                // Skill preview (existing behavior)
                var actorIndex = ParseIntFromJson(body, "actorIndex");
                var targetIndex2 = ParseIntFromJson(body, "targetIndex");
                return ActionHandler.ExecutePreview(actorIndex, skillIndex, targetIndex2);
            }
            catch (Exception e)
            {
                return $"{{\"error\": \"Failed to preview action: {EscapeJson(e.Message)}\"}}";
            }
        }

        private string HandleTeleport(string body)
        {
            // Expected format: {"x": 123.5, "y": 456.0, "z": 0}
            try
            {
                var x = ParseFloatFromJson(body, "x");
                var y = ParseFloatFromJson(body, "y");
                var z = ParseFloatFromJson(body, "z", 0f); // z defaults to 0

                return ActionHandler.ExecuteTeleport(x, y, z);
            }
            catch (Exception e)
            {
                return $"{{\"error\": \"Failed to teleport: {EscapeJson(e.Message)}\"}}";
            }
        }

        private string HandleInteract()
        {
            return ActionHandler.ExecuteInteract();
        }

        private string HandleCombatStart(string body)
        {
            // Expected formats:
            // {"monsterGroupIndex": 0}                    - Start combat without void blitz
            // {"monsterGroupIndex": 0, "voidBlitz": true} - Start combat with void blitz animation
            // {"monsterGroupIndex": 0, "voidBlitz": true, "monsterIndex": 1} - Void blitz specific monster
            try
            {
                var monsterGroupIndex = ParseIntFromJson(body, "monsterGroupIndex");
                var monsterIndex = ParseIntFromJson(body, "monsterIndex");
                if (monsterIndex < 0) monsterIndex = 0;

                // Check for voidBlitz flag
                bool useVoidBlitz = body.Contains("\"voidBlitz\"") && body.Contains("true");

                if (useVoidBlitz)
                {
                    return ActionHandler.ExecuteVoidBlitz(monsterGroupIndex, monsterIndex);
                }
                else
                {
                    return ActionHandler.ExecuteStartCombat(monsterGroupIndex);
                }
            }
            catch (Exception e)
            {
                return $"{{\"error\": \"Failed to start combat: {EscapeJson(e.Message)}\"}}";
            }
        }

        private string HandleSkillSelect(string body)
        {
            // Expected format:
            // {"skillIndex": 0-2} - select skill at index
            // {"skillIndex": -1}  - select max health bonus (when at max skills)
            // {"reroll": true}    - reroll all skills
            try
            {
                // Check for reroll
                if (body.Contains("\"reroll\"") && body.Contains("true"))
                {
                    return ActionHandler.ExecuteSkillSelection(-1, reroll: true);
                }

                // Parse skill index
                var skillIndex = ParseIntFromJson(body, "skillIndex");
                if (skillIndex < -1 || skillIndex > 2)
                {
                    return $"{{\"error\": \"Invalid skillIndex: {skillIndex}. Use 0-2 for skills, -1 for max health.\"}}";
                }

                return ActionHandler.ExecuteSkillSelection(skillIndex, reroll: false);
            }
            catch (Exception e)
            {
                return $"{{\"error\": \"Failed to execute skill selection: {EscapeJson(e.Message)}\"}}";
            }
        }

        // --- Helpers ---

        private static string BoolToJson(bool b) => b ? "true" : "false";

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        private static int ParseIntFromJson(string json, string key)
        {
            var pattern = $"\"{key}\"\\s*:\\s*(\\d+)";
            var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
            if (match.Success)
            {
                return int.Parse(match.Groups[1].Value);
            }
            return -1;
        }

        private static float ParseFloatFromJson(string json, string key, float defaultValue = 0f)
        {
            var pattern = $"\"{key}\"\\s*:\\s*(-?[\\d.]+)";
            var match = System.Text.RegularExpressions.Regex.Match(json, pattern);
            if (match.Success)
            {
                return float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }
            return defaultValue;
        }
    }
}
