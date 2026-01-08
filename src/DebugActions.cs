using System;
using System.Collections.Generic;

namespace AethermancerHarness
{
    /// <summary>
    /// Debug-related actions for inspecting game state.
    /// </summary>
    public static partial class ActionHandler
    {
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
