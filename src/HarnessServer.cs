using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;

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
            // Bind to localhost only - no fallback needed
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listenerThread = new Thread(ListenLoop) { IsBackground = true };
        }

        public void Start()
        {
            _listener.Start();
            _running = true;
            _listenerThread.Start();
            _logger.LogInfo($"HTTP server started on localhost:{_port}");
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
                catch (System.Exception e)
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
                        {
                            string result = null;
                            Plugin.RunOnMainThreadAndWait(() => result = HandleHealth());
                            responseBody = result;
                        }
                        break;

                    case "/ready":
                        {
                            string result = null;
                            Plugin.RunOnMainThreadAndWait(() => result = HandleReady());
                            responseBody = result;
                        }
                        break;

                    case "/state":
                        var format = request.QueryString["format"];
                        if (format == "text")
                            (responseBody, statusCode) = (JsonConfig.Error("Text format not supported. Use JSON."), 400);
                        else
                        {
                            string result = null;
                            Plugin.RunOnMainThreadAndWait(() => result = StateSerializer.ToJson());
                            responseBody = result;
                        }
                        break;

                    case "/actions":
                        {
                            string result = null;
                            Plugin.RunOnMainThreadAndWait(() => result = HandleActions());
                            responseBody = result;
                        }
                        break;

                    case "/combat/action":
                        if (method != "POST")
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        else
                        {
                            var body = ReadBody(request);
                            // HandleCombatAction handles its own threading internally
                            responseBody = HandleCombatAction(body);
                        }
                        break;

                    case "/combat/preview":
                        if (method != "POST")
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        else
                        {
                            var body = ReadBody(request);
                            string result = null;
                            Plugin.RunOnMainThreadAndWait(() => result = HandleCombatPreview(body));
                            responseBody = result;
                        }
                        break;

                    case "/combat/enemy-actions":
                        {
                            string result = null;
                            Plugin.RunOnMainThreadAndWait(() => result = ActionHandler.GetEnemyActions());
                            responseBody = result;
                        }
                        break;

                    case "/exploration/teleport":
                        if (method != "POST")
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        else
                        {
                            var body = ReadBody(request);
                            string result = null;
                            Plugin.RunOnMainThreadAndWait(() => result = HandleTeleport(body));
                            responseBody = result;
                        }
                        break;

                    case "/exploration/interact":
                        if (method != "POST")
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        else
                        {
                            var body = ReadBody(request);
                            string result = null;
                            Plugin.RunOnMainThreadAndWait(() => result = HandleExplorationInteract(body));
                            responseBody = result;
                        }
                        break;

                    case "/exploration/loot-all":
                        if (method != "POST")
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        else
                        {
                            string result = null;
                            Plugin.RunOnMainThreadAndWait(() => result = ActionHandler.ExecuteLootAll());
                            responseBody = result;
                        }
                        break;

                    case "/combat/start":
                        if (method != "POST")
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        else
                        {
                            var body = ReadBody(request);
                            string result = null;
                            Plugin.RunOnMainThreadAndWait(() => result = HandleCombatStart(body));
                            responseBody = result;
                        }
                        break;

                    case "/npc/interact":
                        if (method != "POST")
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        else
                        {
                            var body = ReadBody(request);
                            string result = null;
                            Plugin.RunOnMainThreadAndWait(() => result = HandleNpcInteract(body));
                            responseBody = result;
                        }
                        break;

                    case "/choice":
                    case "/npc/dialogue-choice": // backwards compatibility
                        if (method != "POST")
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        else
                        {
                            var body = ReadBody(request);
                            string result = null;
                            Plugin.RunOnMainThreadAndWait(() => result = HandleChoice(body));
                            responseBody = result;
                        }
                        break;

                    case "/debug/interactables":
                        {
                            string result = null;
                            Plugin.RunOnMainThreadAndWait(() => result = ActionHandler.DebugInteractables());
                            responseBody = result;
                        }
                        break;

                    case "/debug/dialogue":
                        {
                            string result = null;
                            Plugin.RunOnMainThreadAndWait(() => result = ActionHandler.DebugDialogueState());
                            responseBody = result;
                        }
                        break;

                    default:
                        responseBody = JsonConfig.Serialize(new
                        {
                            error = "Not found",
                            endpoints = new[]
                            {
                                "/health", "/ready", "/state", "/actions", "/combat/action", "/combat/preview",
                                "/combat/enemy-actions", "/combat/start", "/exploration/teleport",
                                "/exploration/interact", "/exploration/loot-all", "/npc/interact",
                                "/choice", "/debug/interactables", "/debug/dialogue"
                            }
                        });
                        statusCode = 404;
                        break;
                }

                SendResponse(response, responseBody, statusCode);
            }
            catch (System.Exception e)
            {
                _logger.LogError($"Error handling request: {e}");
                SendResponse(response, JsonConfig.Error(e.Message), 500);
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
            return JsonConfig.Serialize(new
            {
                status = "ok",
                gameReady = GameController.Instance != null
            });
        }

        private string HandleActions()
        {
            return StateSerializer.GetValidActionsJson();
        }

        private string HandleCombatAction(string body)
        {
            var json = JsonConfig.Parse(body);
            var consumableName = JsonConfig.Value(json, "consumableName", (string)null);

            // Consumables use name-based resolution
            if (!string.IsNullOrEmpty(consumableName))
            {
                var targetName = JsonConfig.Value(json, "targetName", (string)null);
                return ActionHandler.ExecuteConsumableAction(consumableName, targetName);
            }

            // Combat actions use names
            var actorName = JsonConfig.Value(json, "actorName", (string)null);
            var skillName = JsonConfig.Value(json, "skillName", (string)null);
            var targetName2 = JsonConfig.Value(json, "targetName", (string)null);

            if (string.IsNullOrEmpty(actorName))
                return JsonConfig.Error("actorName is required");
            if (string.IsNullOrEmpty(skillName))
                return JsonConfig.Error("skillName is required");
            if (string.IsNullOrEmpty(targetName2))
                return JsonConfig.Error("targetName is required");

            return ActionHandler.ExecuteCombatAction(actorName, skillName, targetName2);
        }

        private string HandleCombatPreview(string body)
        {
            var json = JsonConfig.Parse(body);
            var consumableName = JsonConfig.Value(json, "consumableName", (string)null);

            // Consumables use name-based resolution
            if (!string.IsNullOrEmpty(consumableName))
            {
                var targetName = JsonConfig.Value(json, "targetName", (string)null);
                return ActionHandler.ExecuteConsumablePreview(consumableName, targetName);
            }

            // Combat previews use names
            var actorName = JsonConfig.Value(json, "actorName", (string)null);
            var skillName = JsonConfig.Value(json, "skillName", (string)null);
            var targetName2 = JsonConfig.Value(json, "targetName", (string)null);

            if (string.IsNullOrEmpty(actorName))
                return JsonConfig.Error("actorName is required");
            if (string.IsNullOrEmpty(skillName))
                return JsonConfig.Error("skillName is required");
            if (string.IsNullOrEmpty(targetName2))
                return JsonConfig.Error("targetName is required");

            return ActionHandler.ExecutePreview(actorName, skillName, targetName2);
        }

        private string HandleTeleport(string body)
        {
            var json = JsonConfig.Parse(body);
            var x = JsonConfig.Value(json, "x", 0f);
            var y = JsonConfig.Value(json, "y", 0f);
            var z = JsonConfig.Value(json, "z", 0f);
            return TeleportActions.ExecuteTeleport(x, y, z);
        }

        private string HandleCombatStart(string body)
        {
            var json = JsonConfig.Parse(body);
            var monsterGroupName = JsonConfig.Value(json, "monsterGroupName", (string)null);
            var monsterName = JsonConfig.Value(json, "monsterName", (string)null);

            if (string.IsNullOrEmpty(monsterGroupName))
                return JsonConfig.Error("monsterGroupName is required");

            return ActionHandler.ExecuteVoidBlitz(monsterGroupName, monsterName);
        }

        private string HandleNpcInteract(string body)
        {
            var json = JsonConfig.Parse(body);
            var npcName = JsonConfig.Value(json, "npcName", (string)null);

            if (string.IsNullOrEmpty(npcName))
                return JsonConfig.Error("npcName is required");

            return ActionHandler.ExecuteNpcInteract(npcName);
        }

        private string HandleExplorationInteract(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return JsonConfig.Error("name is required");

            var json = JsonConfig.Parse(body);
            var name = JsonConfig.Value(json, "name", (string)null);

            if (string.IsNullOrWhiteSpace(name))
                return JsonConfig.Error("name is required");

            return ActionHandler.TeleportAndInteract(name);
        }

        private string HandleChoice(string body)
        {
            var json = JsonConfig.Parse(body);
            var choiceName = JsonConfig.Value(json, "choiceName", (string)null);
            var shift = JsonConfig.Value(json, "shift", (string)null); // "normal" or "shifted"

            if (string.IsNullOrEmpty(choiceName))
                return JsonConfig.Error("choiceName is required");

            return ActionHandler.ExecuteChoice(choiceName, shift);
        }

        private string HandleReady()
        {
            var state = ActionHandler.GetReadinessState();
            return JsonConfig.Serialize(state);
        }
    }
}
