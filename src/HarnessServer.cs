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
                        var format = request.QueryString["format"];
                        if (format == "text")
                            (responseBody, statusCode) = (JsonConfig.Error("Text format not supported. Use JSON."), 400);
                        else
                            responseBody = StateSerializer.ToJson();
                        break;

                    case "/actions":
                        responseBody = HandleActions();
                        break;

                    case "/combat/action":
                        if (method == "POST")
                            responseBody = HandleCombatAction(ReadBody(request));
                        else
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        break;

                    case "/combat/preview":
                        if (method == "POST")
                            responseBody = HandleCombatPreview(ReadBody(request));
                        else
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        break;

                    case "/combat/enemy-actions":
                        responseBody = ActionHandler.GetEnemyActions();
                        break;

                    case "/exploration/teleport":
                        if (method == "POST")
                            responseBody = HandleTeleport(ReadBody(request));
                        else
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        break;

                    case "/exploration/interact":
                        if (method == "POST")
                            responseBody = ActionHandler.ExecuteInteract();
                        else
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        break;

                    case "/exploration/loot-all":
                        if (method == "POST")
                            responseBody = ActionHandler.ExecuteLootAll();
                        else
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        break;

                    case "/combat/start":
                        if (method == "POST")
                            responseBody = HandleCombatStart(ReadBody(request));
                        else
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        break;

                    case "/skill-select":
                        if (method == "POST")
                            responseBody = HandleSkillSelect(ReadBody(request));
                        else
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        break;

                    case "/npc/interact":
                        if (method == "POST")
                            responseBody = HandleNpcInteract(ReadBody(request));
                        else
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        break;

                    case "/choice":
                    case "/npc/dialogue-choice": // backwards compatibility
                        if (method == "POST")
                            responseBody = HandleChoice(ReadBody(request));
                        else
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        break;

                    case "/merchant/interact":
                        if (method == "POST")
                            responseBody = ActionHandler.ExecuteMerchantInteract();
                        else
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        break;

                    case "/aether-spring/interact":
                        if (method == "POST")
                            responseBody = ActionHandler.ExecuteAetherSpringInteract();
                        else
                            (responseBody, statusCode) = (JsonConfig.Error("Method not allowed"), 405);
                        break;

                    default:
                        responseBody = JsonConfig.Serialize(new
                        {
                            error = "Not found",
                            endpoints = new[]
                            {
                                "/health", "/state", "/actions", "/combat/action", "/combat/preview",
                                "/combat/enemy-actions", "/combat/start", "/exploration/teleport",
                                "/exploration/interact", "/exploration/loot-all", "/skill-select",
                                "/npc/interact", "/choice", "/merchant/interact", "/aether-spring/interact"
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
                gameReady = GameController.Instance != null,
                inCombat = GameStateManager.Instance?.IsCombat ?? false,
                readyForInput = ActionHandler.IsReadyForInput(),
                inputStatus = ActionHandler.GetInputReadyStatus()
            });
        }

        private string HandleActions()
        {
            return StateSerializer.GetValidActionsJson();
        }

        private string HandleCombatAction(string body)
        {
            var json = JsonConfig.Parse(body);
            var consumableIndex = JsonConfig.Value(json, "consumableIndex", -1);
            var skillIndex = JsonConfig.Value(json, "skillIndex", -1);
            var skillName = JsonConfig.Value(json, "skillName", (string)null);

            if (consumableIndex >= 0 && (skillIndex >= 0 || !string.IsNullOrEmpty(skillName)))
                return JsonConfig.Error("Cannot specify both consumableIndex and skill (skillIndex/skillName)");

            if (consumableIndex >= 0)
            {
                var targetIndex = JsonConfig.Value(json, "targetIndex", -1);
                return ActionHandler.ExecuteConsumableAction(consumableIndex, targetIndex);
            }

            var actorIndex = JsonConfig.Value(json, "actorIndex", -1);
            var actorName = JsonConfig.Value(json, "actorName", (string)null);
            var targetIndex2 = JsonConfig.Value(json, "targetIndex", -1);
            return ActionHandler.ExecuteCombatAction(actorIndex, actorName, skillIndex, skillName, targetIndex2);
        }

        private string HandleCombatPreview(string body)
        {
            var json = JsonConfig.Parse(body);
            var consumableIndex = JsonConfig.Value(json, "consumableIndex", -1);
            var skillIndex = JsonConfig.Value(json, "skillIndex", -1);
            var skillName = JsonConfig.Value(json, "skillName", (string)null);

            if (consumableIndex >= 0 && (skillIndex >= 0 || !string.IsNullOrEmpty(skillName)))
                return JsonConfig.Error("Cannot specify both consumableIndex and skill (skillIndex/skillName)");

            if (consumableIndex >= 0)
            {
                var targetIndex = JsonConfig.Value(json, "targetIndex", -1);
                return ActionHandler.ExecuteConsumablePreview(consumableIndex, targetIndex);
            }

            var actorIndex = JsonConfig.Value(json, "actorIndex", -1);
            var actorName = JsonConfig.Value(json, "actorName", (string)null);
            var targetIndex2 = JsonConfig.Value(json, "targetIndex", -1);
            return ActionHandler.ExecutePreview(actorIndex, actorName, skillIndex, skillName, targetIndex2);
        }

        private string HandleTeleport(string body)
        {
            var json = JsonConfig.Parse(body);
            var x = JsonConfig.Value(json, "x", 0f);
            var y = JsonConfig.Value(json, "y", 0f);
            var z = JsonConfig.Value(json, "z", 0f);
            return ActionHandler.ExecuteTeleport(x, y, z);
        }

        private string HandleCombatStart(string body)
        {
            var json = JsonConfig.Parse(body);
            var monsterGroupIndex = JsonConfig.Value(json, "monsterGroupIndex", -1);
            var monsterIndex = JsonConfig.Value(json, "monsterIndex", 0);
            var useVoidBlitz = JsonConfig.Value(json, "voidBlitz", false);

            if (useVoidBlitz)
                return ActionHandler.ExecuteVoidBlitz(monsterGroupIndex, monsterIndex);
            return ActionHandler.ExecuteStartCombat(monsterGroupIndex);
        }

        private string HandleSkillSelect(string body)
        {
            var json = JsonConfig.Parse(body);
            var reroll = JsonConfig.Value(json, "reroll", false);

            if (reroll)
                return ActionHandler.ExecuteSkillSelection(-1, reroll: true);

            var skillIndex = JsonConfig.Value(json, "skillIndex", -1);
            if (skillIndex < -1 || skillIndex > 2)
                return JsonConfig.Error($"Invalid skillIndex: {skillIndex}. Use 0-2 for skills, -1 for max health.");

            return ActionHandler.ExecuteSkillSelection(skillIndex, reroll: false);
        }

        private string HandleNpcInteract(string body)
        {
            var json = JsonConfig.Parse(body);
            var npcIndex = JsonConfig.Value(json, "npcIndex", -1);

            if (npcIndex < 0)
                return JsonConfig.Error("npcIndex is required and must be >= 0");

            return ActionHandler.ExecuteNpcInteract(npcIndex);
        }

        private string HandleChoice(string body)
        {
            var json = JsonConfig.Parse(body);
            var choiceIndex = JsonConfig.Value(json, "choiceIndex", -1);
            var shift = JsonConfig.Value(json, "shift", (string)null); // "normal" or "shifted"

            if (choiceIndex < 0)
                return JsonConfig.Error("choiceIndex is required and must be >= 0");

            return ActionHandler.ExecuteChoice(choiceIndex, shift);
        }
    }
}
