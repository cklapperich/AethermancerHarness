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
                            responseBody = HandleCombatAction(ReadBody(request));
                        else
                            (responseBody, statusCode) = (JsonHelper.Serialize(new { error = "Method not allowed" }), 405);
                        break;

                    case "/combat/preview":
                        if (method == "POST")
                            responseBody = HandleCombatPreview(ReadBody(request));
                        else
                            (responseBody, statusCode) = (JsonHelper.Serialize(new { error = "Method not allowed" }), 405);
                        break;

                    case "/combat/enemy-actions":
                        responseBody = ActionHandler.GetEnemyActions();
                        break;

                    case "/exploration/teleport":
                        if (method == "POST")
                            responseBody = HandleTeleport(ReadBody(request));
                        else
                            (responseBody, statusCode) = (JsonHelper.Serialize(new { error = "Method not allowed" }), 405);
                        break;

                    case "/exploration/interact":
                        if (method == "POST")
                            responseBody = ActionHandler.ExecuteInteract();
                        else
                            (responseBody, statusCode) = (JsonHelper.Serialize(new { error = "Method not allowed" }), 405);
                        break;

                    case "/combat/start":
                        if (method == "POST")
                            responseBody = HandleCombatStart(ReadBody(request));
                        else
                            (responseBody, statusCode) = (JsonHelper.Serialize(new { error = "Method not allowed" }), 405);
                        break;

                    case "/skill-select":
                        if (method == "POST")
                            responseBody = HandleSkillSelect(ReadBody(request));
                        else
                            (responseBody, statusCode) = (JsonHelper.Serialize(new { error = "Method not allowed" }), 405);
                        break;

                    default:
                        responseBody = JsonHelper.Serialize(new
                        {
                            error = "Not found",
                            endpoints = new[]
                            {
                                "/health", "/state", "/actions", "/combat/action", "/combat/preview",
                                "/combat/enemy-actions", "/combat/start", "/exploration/teleport",
                                "/exploration/interact", "/skill-select"
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
                SendResponse(response, JsonHelper.Serialize(new { error = e.Message }), 500);
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
            return JsonHelper.Serialize(new
            {
                status = "ok",
                gameReady = GameController.Instance != null,
                inCombat = GameStateManager.Instance?.IsCombat ?? false,
                readyForInput = ActionHandler.IsReadyForInput(),
                inputStatus = ActionHandler.GetInputReadyStatus()
            });
        }

        private string HandleState(string format)
        {
            if (format == "text")
                return JsonHelper.Serialize(new { text = StateSerializer.ToText() });
            return StateSerializer.ToJson();
        }

        private string HandleActions()
        {
            return StateSerializer.GetValidActionsJson();
        }

        private string HandleCombatAction(string body)
        {
            var json = JsonHelper.Parse(body);
            var consumableIndex = JsonHelper.Value(json, "consumableIndex", -1);
            var skillIndex = JsonHelper.Value(json, "skillIndex", -1);

            if (consumableIndex >= 0 && skillIndex >= 0)
                return JsonHelper.Serialize(new { error = "Cannot specify both consumableIndex and skillIndex" });

            if (consumableIndex >= 0)
            {
                var targetIndex = JsonHelper.Value(json, "targetIndex", -1);
                return ActionHandler.ExecuteConsumableAction(consumableIndex, targetIndex);
            }

            var actorIndex = JsonHelper.Value(json, "actorIndex", -1);
            var targetIndex2 = JsonHelper.Value(json, "targetIndex", -1);
            return ActionHandler.ExecuteCombatAction(actorIndex, skillIndex, targetIndex2);
        }

        private string HandleCombatPreview(string body)
        {
            var json = JsonHelper.Parse(body);
            var consumableIndex = JsonHelper.Value(json, "consumableIndex", -1);
            var skillIndex = JsonHelper.Value(json, "skillIndex", -1);

            if (consumableIndex >= 0 && skillIndex >= 0)
                return JsonHelper.Serialize(new { error = "Cannot specify both consumableIndex and skillIndex" });

            if (consumableIndex >= 0)
            {
                var targetIndex = JsonHelper.Value(json, "targetIndex", -1);
                return ActionHandler.ExecuteConsumablePreview(consumableIndex, targetIndex);
            }

            var actorIndex = JsonHelper.Value(json, "actorIndex", -1);
            var targetIndex2 = JsonHelper.Value(json, "targetIndex", -1);
            return ActionHandler.ExecutePreview(actorIndex, skillIndex, targetIndex2);
        }

        private string HandleTeleport(string body)
        {
            var json = JsonHelper.Parse(body);
            var x = JsonHelper.Value(json, "x", 0f);
            var y = JsonHelper.Value(json, "y", 0f);
            var z = JsonHelper.Value(json, "z", 0f);
            return ActionHandler.ExecuteTeleport(x, y, z);
        }

        private string HandleCombatStart(string body)
        {
            var json = JsonHelper.Parse(body);
            var monsterGroupIndex = JsonHelper.Value(json, "monsterGroupIndex", -1);
            var monsterIndex = JsonHelper.Value(json, "monsterIndex", 0);
            var useVoidBlitz = JsonHelper.Value(json, "voidBlitz", false);

            if (useVoidBlitz)
                return ActionHandler.ExecuteVoidBlitz(monsterGroupIndex, monsterIndex);
            return ActionHandler.ExecuteStartCombat(monsterGroupIndex);
        }

        private string HandleSkillSelect(string body)
        {
            var json = JsonHelper.Parse(body);
            var reroll = JsonHelper.Value(json, "reroll", false);

            if (reroll)
                return ActionHandler.ExecuteSkillSelection(-1, reroll: true);

            var skillIndex = JsonHelper.Value(json, "skillIndex", -1);
            if (skillIndex < -1 || skillIndex > 2)
                return JsonHelper.Serialize(new { error = $"Invalid skillIndex: {skillIndex}. Use 0-2 for skills, -1 for max health." });

            return ActionHandler.ExecuteSkillSelection(skillIndex, reroll: false);
        }
    }
}
