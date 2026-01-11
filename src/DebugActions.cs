using System;
using System.Collections.Generic;
using System.Reflection;

namespace AethermancerHarness
{
    /// <summary>
    /// Debug-related actions for inspecting game state.
    /// </summary>
    public static partial class ActionHandler
    {
        /// <summary>
        /// Debug endpoint to dump comprehensive dialogue state.
        /// Useful for debugging stuck dialogue or weird UI states.
        /// </summary>
        public static string DebugDialogueState()
        {
            object result = null;
            string error = null;

            Plugin.RunOnMainThreadAndWait(() =>
            {
                try
                {
                    var display = UIController.Instance?.DialogueDisplay;
                    var dialogueInteractable = GetCurrentDialogue();
                    var dialogueData = GetCurrentDialogueData();

                    // Get private fields from DialogueDisplay via reflection
                    var displayType = typeof(DialogueDisplay);
                    var inputField = displayType.GetField("input", BindingFlags.NonPublic | BindingFlags.Instance);
                    var isLastLineField = displayType.GetField("isLastLine", BindingFlags.NonPublic | BindingFlags.Instance);
                    var timeOpenField = displayType.GetField("timeOpen", BindingFlags.NonPublic | BindingFlags.Instance);
                    var leftIsSpeakingField = displayType.GetField("leftIsSpeaking", BindingFlags.NonPublic | BindingFlags.Instance);

                    bool isLastLine = false;
                    float timeOpen = 0f;
                    bool leftIsSpeaking = false;
                    bool inputEnabled = false;

                    if (display != null)
                    {
                        isLastLine = (bool)(isLastLineField?.GetValue(display) ?? false);
                        timeOpen = (float)(timeOpenField?.GetValue(display) ?? 0f);
                        leftIsSpeaking = (bool)(leftIsSpeakingField?.GetValue(display) ?? false);

                        var inputActions = inputField?.GetValue(display);
                        if (inputActions != null)
                        {
                            var mainProp = inputActions.GetType().GetProperty("Main");
                            var main = mainProp?.GetValue(inputActions);
                            if (main != null)
                            {
                                var enabledProp = main.GetType().GetProperty("enabled");
                                inputEnabled = (bool)(enabledProp?.GetValue(main) ?? false);
                            }
                        }
                    }

                    // Get DialogueEventManager.CurrentInteraction state
                    var currentInteraction = DialogueEventManager.CurrentInteraction;
                    List<object> currentOptions = null;
                    List<object> previousSelections = null;

                    if (currentInteraction != null)
                    {
                        currentOptions = currentInteraction.CurrentOptions;
                        previousSelections = currentInteraction.PreviousSelection;
                    }

                    // Get current node info from DialogueInteractable
                    object currentNodeInfo = null;
                    if (dialogueInteractable != null)
                    {
                        var currentNodeField = typeof(DialogueInteractable).GetField("currentNode", BindingFlags.NonPublic | BindingFlags.Instance);
                        var currentNodeOptionsField = typeof(DialogueInteractable).GetField("currentNodeOptions", BindingFlags.NonPublic | BindingFlags.Instance);

                        var currentNode = currentNodeField?.GetValue(dialogueInteractable) as DialogueNodeData;
                        var currentNodeOptions = currentNodeOptionsField?.GetValue(dialogueInteractable) as List<DialogueNodeData>;

                        if (currentNode != null)
                        {
                            currentNodeInfo = new
                            {
                                DialogueText = currentNode.DialogueText,
                                LocalizedText = currentNode.LocalizedText,
                                IsChoiceEvent = currentNode.IsChoiceEvent,
                                IsMultiplicatorNode = currentNode.IsMultiplicatorNode,
                                AutopickIfChoice = currentNode.AutopickIfChoice,
                                AllOptionsSmall = currentNode.AllOptionsSmall,
                                LastOptionIsSmall = currentNode.LastOptionIsSmall,
                                EnableMonsterDetails = currentNode.EnableMonsterDetails,
                                OptionsCount = currentNodeOptions?.Count ?? 0,
                                Options = currentNodeOptions != null ? GetNodeOptionTexts(currentNodeOptions) : null
                            };
                        }
                    }

                    // Get character display states
                    object leftDisplayState = null;
                    object rightDisplayState = null;

                    if (display != null)
                    {
                        leftDisplayState = GetCharacterDisplayState(display.LeftCharacterDisplay, "Left");
                        rightDisplayState = GetCharacterDisplayState(display.RightCharacterDisplay, "Right");
                    }

                    result = new
                    {
                        success = true,
                        timestamp = DateTime.Now.ToString("HH:mm:ss.fff"),

                        // Basic state
                        IsDialogueOpen = display?.IsOpen ?? false,
                        InputAllowed = display?.InputAllowed ?? false,
                        InputEnabled = inputEnabled,
                        IsLastLine = isLastLine,
                        TimeOpen = timeOpen,
                        LeftIsSpeaking = leftIsSpeaking,

                        // DialogueDisplayData
                        DialogueData = dialogueData != null ? new
                        {
                            DialogueText = dialogueData.DialogueText,
                            IsChoiceEvent = dialogueData.IsChoiceEvent,
                            OptionsCount = dialogueData.DialogueOptions?.Length ?? 0,
                            Options = dialogueData.DialogueOptions,
                            AllOptionsSmall = dialogueData.AllOptionsSmall,
                            LastOptionIsSmall = dialogueData.LastOptionIsSmall,
                            EnableMonsterDetails = dialogueData.EnableMonsterDetails,
                            LeftCharacterName = dialogueData.LeftCharacter?.CharacterName,
                            RightCharacterName = dialogueData.RightCharacter?.CharacterName,
                            LeftIsSpeaking = dialogueData.LeftIsSpeaking
                        } : null,

                        // DialogueInteractable state
                        DialogueInteractable = dialogueInteractable != null ? new
                        {
                            CharacterName = dialogueInteractable.DialogueCharacter?.CharacterName,
                            InteractableType = dialogueInteractable.InteractableType.ToString(),
                            OneTimeDialogue = dialogueInteractable.OneTimeDialogue,
                            UseDialoguePool = dialogueInteractable.UseDialoguePool,
                            CanGoBack = dialogueInteractable.IsGoBackPossible(out _)
                        } : null,

                        // Current node from interactable
                        CurrentNode = currentNodeInfo,

                        // DialogueEventManager state
                        EventManager = new
                        {
                            HasCurrentInteraction = currentInteraction != null,
                            CurrentOptionsCount = currentOptions?.Count ?? 0,
                            CurrentOptionTypes = currentOptions != null ? GetOptionTypes(currentOptions) : null,
                            PreviousSelectionsCount = previousSelections?.Count ?? 0
                        },

                        // Character display states (shows UI element states)
                        LeftCharacterDisplay = leftDisplayState,
                        RightCharacterDisplay = rightDisplayState,

                        // Menu states that could interfere
                        OtherMenuStates = new
                        {
                            IsInSkillSelection = StateSerializer.IsInSkillSelection(),
                            IsInEquipmentSelection = StateSerializer.IsInEquipmentSelection(),
                            IsInMonsterSelection = StateSerializer.IsInMonsterSelection(),
                            IsInMerchantMenu = IsMerchantMenuOpen(),
                            IsInAetherSpringMenu = StateSerializer.IsInAetherSpringMenu(),
                            PopupOpen = PopupController.Instance?.IsOpen ?? false,
                            IsPaused = GameStateManager.Instance?.IsPaused ?? false
                        }
                    };
                }
                catch (Exception ex)
                {
                    error = $"{ex.Message}\n{ex.StackTrace}";
                }
            });

            if (error != null)
                return JsonConfig.Error(error);

            return JsonConfig.Serialize(result);
        }

        private static object GetCharacterDisplayState(DialogueCharacterDisplay charDisplay, string side)
        {
            if (charDisplay == null)
                return new { Side = side, Available = false };

            try
            {
                return new
                {
                    Side = side,
                    Available = true,
                    IsTalking = charDisplay.IsTalking,
                    IsHidden = charDisplay.IsHidden,
                    IsDisplayingDialogue = charDisplay.IsDisplayingDialogue,
                    IsDisplayingEvent = charDisplay.IsDisplayingEvent,
                    IsTextComplete = charDisplay.IsTextComplete,
                    HasDialogOptions = charDisplay.DialogOptions?.List?.Count > 0,
                    DialogOptionsCount = charDisplay.DialogOptions?.List?.Count ?? 0,
                    DialogOptionsCurrentIndex = charDisplay.DialogOptions?.CurrentIndex ?? -1,
                    DialogOptionsIsSelecting = charDisplay.DialogOptions?.IsSelecting ?? false,
                    DialogOptionsIsLocked = charDisplay.DialogOptions?.IsLocked ?? false,
                    HasChoiceEventOptions = charDisplay.ChoiceEventOptions?.List?.Count > 0,
                    ChoiceEventOptionsCount = charDisplay.ChoiceEventOptions?.List?.Count ?? 0,
                    ChoiceEventOptionsCurrentIndex = charDisplay.ChoiceEventOptions?.CurrentIndex ?? -1,
                    ChoiceEventOptionsIsSelecting = charDisplay.ChoiceEventOptions?.IsSelecting ?? false,
                    ChoiceEventOptionsIsLocked = charDisplay.ChoiceEventOptions?.IsLocked ?? false,
                    SelectedOption = charDisplay.GetSelectedOption()
                };
            }
            catch (Exception ex)
            {
                return new { Side = side, Error = ex.Message };
            }
        }

        private static List<string> GetNodeOptionTexts(List<DialogueNodeData> nodes)
        {
            var texts = new List<string>();
            foreach (var node in nodes)
            {
                texts.Add(node?.LocalizedText ?? node?.DialogueText ?? "(null)");
            }
            return texts;
        }

        private static List<string> GetOptionTypes(List<object> options)
        {
            var types = new List<string>();
            foreach (var opt in options)
            {
                types.Add(opt?.GetType().Name ?? "null");
            }
            return types;
        }

        /// <summary>
        /// Debug endpoint to discover all interactables and their types.
        /// Useful for finding gold pickup type names.
        /// </summary>
        public static string DebugInteractables()
        {
            var results = new List<object>();
            string error = null;

            Plugin.RunOnMainThreadAndWait(() =>
            {
                try
                {
                    // Find all BaseInteractable objects in the scene
                    var allInteractables = UnityEngine.Object.FindObjectsByType<BaseInteractable>(
                        UnityEngine.FindObjectsSortMode.None);

                    // Also search for ALL MonoBehaviours and filter by interesting names
                    var allMonoBehaviours = UnityEngine.Object.FindObjectsByType<UnityEngine.MonoBehaviour>(
                        UnityEngine.FindObjectsSortMode.None);

                    foreach (var mono in allMonoBehaviours)
                    {
                        if (mono == null) continue;
                        var typeName = mono.GetType().Name;

                        // Look for collectibles, pickups, gold, currency, loot types
                        if (typeName.Contains("Collect") || typeName.Contains("Pickup") ||
                            typeName.Contains("Gold") || typeName.Contains("Currency") ||
                            typeName.Contains("Loot") || typeName.Contains("Drop") ||
                            typeName.Contains("Reward") || typeName.Contains("Item") ||
                            typeName.Contains("Coin") || typeName.Contains("Money") ||
                            typeName.Contains("Prize") || typeName.Contains("Treasure") ||
                            typeName.Contains("Spawned") || typeName.Contains("Ground"))
                        {
                            var pos = mono.transform.position;
                            results.Add(new
                            {
                                Type = typeName,
                                FullType = mono.GetType().FullName,
                                X = pos.x,
                                Y = pos.y,
                                Z = pos.z,
                                GameObject = mono.gameObject.name,
                                Source = "MonoBehaviour_Search"
                            });
                        }
                    }

                    // Find ALL objects near player (within 100 units)
                    var playerPos = PlayerMovementController.Instance?.transform.position ?? UnityEngine.Vector3.zero;
                    foreach (var mono in allMonoBehaviours)
                    {
                        if (mono == null) continue;
                        var pos = mono.transform.position;
                        var dist = UnityEngine.Vector3.Distance(pos, playerPos);
                        if (dist < 100f && dist > 0.1f) // Near player but not player itself
                        {
                            var typeName = mono.GetType().Name;
                            // Skip common/uninteresting types
                            if (typeName.Contains("Controller") || typeName.Contains("Manager") ||
                                typeName.Contains("Camera") || typeName.Contains("Canvas") ||
                                typeName.Contains("Audio") || typeName.Contains("Light") ||
                                typeName.Contains("Renderer") || typeName.Contains("Animator"))
                                continue;

                            results.Add(new
                            {
                                Type = typeName,
                                FullType = mono.GetType().FullName,
                                X = pos.x,
                                Y = pos.y,
                                Z = pos.z,
                                Distance = dist,
                                GameObject = mono.gameObject.name,
                                Source = "NearPlayer"
                            });
                        }
                    }

                    // Also check SmallEventInteractables for event details
                    var map = LevelGenerator.Instance?.Map;
                    if (map != null)
                    {
                        foreach (var evt in map.SmallEventInteractables)
                        {
                            if (evt == null) continue;
                            var pos = evt.transform.position;
                            var evtType = evt.GetType();

                            // Get all public properties via reflection
                            var props = new Dictionary<string, string>();
                            foreach (var prop in evtType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
                            {
                                try
                                {
                                    var val = prop.GetValue(evt);
                                    if (val != null)
                                        props[prop.Name] = val.ToString();
                                }
                                catch { }
                            }

                            results.Add(new
                            {
                                Type = evtType.Name,
                                FullType = evtType.FullName,
                                X = pos.x,
                                Y = pos.y,
                                Z = pos.z,
                                GameObject = evt.gameObject.name,
                                WasUsedUp = evt.WasUsedUp,
                                Properties = props,
                                Source = "SmallEventInteractables"
                            });
                        }
                    }

                    foreach (var obj in allInteractables)
                    {
                        if (obj == null) continue;

                        var pos = obj.transform.position;
                        var typeName = obj.GetType().Name;
                        var fullTypeName = obj.GetType().FullName;

                        // Get relevant methods via reflection
                        var methods = new List<string>();
                        foreach (var method in obj.GetType().GetMethods(
                            System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.DeclaredOnly))
                        {
                            var name = method.Name;
                            if (name.Contains("Break") || name.Contains("Interact") ||
                                name.Contains("Force") || name.Contains("Collect") ||
                                name.Contains("Pickup") || name.Contains("Start"))
                            {
                                methods.Add(name);
                            }
                        }

                        bool wasUsedUp = false;
                        bool canInteract = false;
                        try { wasUsedUp = obj.WasUsedUp; } catch { }
                        try { canInteract = obj.CanBeInteracted(); } catch { }

                        results.Add(new
                        {
                            Type = typeName,
                            FullType = fullTypeName,
                            X = pos.x,
                            Y = pos.y,
                            Z = pos.z,
                            WasUsedUp = wasUsedUp,
                            CanInteract = canInteract,
                            Methods = methods.ToArray()
                        });
                    }

                    // Also check breakable objects from PropGenerator
                    var propGen = LevelGenerator.Instance?.PropGenerator;
                    if (propGen != null)
                    {
                        var breakables = propGen.GeneratedBreakableObjects;
                        if (breakables != null)
                        {
                            // Track positions we've already added
                            var seenPositions = new HashSet<string>();
                            foreach (var obj in allInteractables)
                            {
                                if (obj != null)
                                {
                                    var p = obj.transform.position;
                                    seenPositions.Add($"{p.x:F2},{p.y:F2}");
                                }
                            }

                            foreach (var breakable in breakables)
                            {
                                if (breakable == null) continue;
                                var pos = breakable.transform.position;
                                var posKey = $"{pos.x:F2},{pos.y:F2}";

                                if (!seenPositions.Contains(posKey))
                                {
                                    seenPositions.Add(posKey);

                                    var typeName = breakable.GetType().Name;
                                    var methods = new List<string>();
                                    foreach (var method in breakable.GetType().GetMethods(
                                        System.Reflection.BindingFlags.Public |
                                        System.Reflection.BindingFlags.Instance |
                                        System.Reflection.BindingFlags.DeclaredOnly))
                                    {
                                        var name = method.Name;
                                        if (name.Contains("Break") || name.Contains("Interact") ||
                                            name.Contains("Force") || name.Contains("Collect"))
                                        {
                                            methods.Add(name);
                                        }
                                    }

                                    bool canInt = false;
                                    try { canInt = breakable.CanBeInteracted(); } catch { }

                                    results.Add(new
                                    {
                                        Type = typeName,
                                        FullType = breakable.GetType().FullName,
                                        X = pos.x,
                                        Y = pos.y,
                                        Z = pos.z,
                                        WasUsedUp = false,
                                        CanInteract = canInt,
                                        Methods = methods.ToArray(),
                                        Source = "PropGenerator.GeneratedBreakableObjects"
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            });

            if (error != null)
                return JsonConfig.Error(error);

            Plugin.Log.LogInfo($"DebugInteractables: Found {results.Count} interactables");
            return JsonConfig.Serialize(new { success = true, count = results.Count, interactables = results });
        }
    }
}
