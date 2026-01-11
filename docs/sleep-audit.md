# Thread.Sleep Audit

All `Thread.Sleep` calls should happen on the HTTP thread, not the main Unity thread. Sleeping on the main thread will freeze the game.

## CONFIRMED: Main Thread Sleep (BUG)

| File | Line | Issue |
|------|------|-------|
| src/TeleportActions.cs | 80 | `TeleportInternal` contains Sleep when `WatchableMode=true`, and is called from within `RunOnMainThreadAndWait` lambdas |

### Call sites that invoke TeleportInternal on main thread:

- `src/ExplorationActions.cs:210` - inside `RunOnMainThreadAndWait` (shrine teleport)
- `src/ExplorationActions.cs:256` - inside `RunOnMainThreadAndWait` (merchant teleport)
- `src/ExplorationActions.cs:303` - inside `RunOnMainThreadAndWait` (aether spring teleport)
- `src/ExplorationActions.cs:404` - inside `RunOnMainThreadAndWait` (portal teleport)
- `src/ExplorationActions.cs:453` - inside `RunOnMainThreadAndWait` (breakable teleport)

### The problematic code:

```csharp
// TeleportActions.cs:67-87
public static void TeleportInternal(Vector3 targetPos)
{
    var playerMovement = PlayerMovementController.Instance;
    if (playerMovement == null)
        return;

    if (Plugin.WatchableMode)
    {
        playerMovement.TeleportToPOI(targetPos);

        // BUG: This runs on main thread when called from RunOnMainThreadAndWait!
        while (PlayerMovementController.Instance?.IsTeleporting == true)
        {
            System.Threading.Thread.Sleep(50);  // <-- MAIN THREAD SLEEP
        }
    }
    else
    {
        playerMovement.transform.position = targetPos;
    }
}
```

## Safe - HTTP Thread Sleeps

These Sleep calls run on the HTTP thread because they're outside of `RunOnMainThreadAndWait` lambdas:

| File | Line | Context |
|------|------|---------|
| src/TeleportActions.cs | 46 | `ExecuteAnimatedTeleport` - after RunOnMainThreadAndWait returns |
| src/CombatActions.cs | 89-90 | Explicit comment: "Sleep on calling thread (HTTP thread)" |
| src/MenuActions.cs | 66-67 | Explicit comment: "Sleep on HTTP thread" |
| src/MenuActions.cs | 213-214 | Explicit comment: "Sleep on HTTP thread" |
| src/MenuActions.cs | 305-306 | Explicit comment: "Sleep on HTTP thread" |

## Assumed Safe - HTTP Thread (no explicit comment)

These Sleep calls appear to be on HTTP thread based on code structure (outside of main thread lambdas):

### src/MenuActions.cs
- Lines 145, 169, 203, 343, 361, 377, 498, 596, 637, 727, 790, 805, 808
- Lines 1089, 1118, 1146 (watchable mode delays)
- Lines 1268, 1276, 1390, 1421, 1492, 1495, 1553, 1570, 1580, 1641, 1660, 1718, 1721

### src/ExplorationActions.cs
- Lines 220, 266, 271, 313, 318, 495

## Suggested Fix

Refactor `TeleportInternal` to not sleep on main thread. Either:

1. Return immediately and let caller handle waiting on HTTP thread
2. Use coroutines for waiting instead of blocking sleep
3. Split into `TeleportInternalInstant` (main thread) and wait logic (HTTP thread)
