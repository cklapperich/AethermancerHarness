# Functions That Run on Unity Main Thread

These functions are explicitly documented as requiring main thread execution or are called from within `RunOnMainThreadAndWait` lambdas.

## Explicitly Documented (via comments)

| File | Line | Function | Notes |
|------|------|----------|-------|
| src/MenuActions.cs | 219 | `CheckWorthinessCanContinue` | "MUST be called on main thread" |
| src/MenuActions.cs | 232 | `CheckLevelUpCanContinue` | "MUST be called on main thread" |
| src/MenuActions.cs | 258 | `AutoContinueFromEndOfRun` | "MUST be called on main thread" |
| src/CombatActions.cs | 392 | `BuildCombatActionResponse` | "MUST be called on main thread" |

## Called From RunOnMainThreadAndWait Lambdas

These functions are invoked from within `RunOnMainThreadAndWait` callbacks:

| File | Function | Caller Context |
|------|----------|----------------|
| src/TeleportActions.cs | `TeleportInternal` | ExplorationActions - shrine/merchant/spring/portal/breakable interactions |

### TeleportInternal - BUG

`TeleportInternal` is called from main thread context but contains a `Thread.Sleep` when `WatchableMode` is enabled. This **blocks the Unity main thread** and freezes the game.

```csharp
// Called from ExplorationActions.cs inside RunOnMainThreadAndWait:
Plugin.RunOnMainThreadAndWait(() =>
{
    // ...
    TeleportActions.TeleportInternal(targetPos);  // <-- Runs on main thread
    // ...
});

// TeleportInternal contains:
if (Plugin.WatchableMode)
{
    playerMovement.TeleportToPOI(targetPos);
    while (PlayerMovementController.Instance?.IsTeleporting == true)
    {
        System.Threading.Thread.Sleep(50);  // <-- BLOCKS MAIN THREAD!
    }
}
```

## Safe Helper Functions

These are called from main thread but don't contain any Sleep calls:

- `CheckWorthinessCanContinue` - readonly check
- `CheckLevelUpCanContinue` - readonly check
- `AutoContinueFromEndOfRun` - UI trigger only
- `BuildCombatActionResponse` - JSON construction
- `OpenSkillSelectionForMonster` - UI navigation
- `TriggerContinue` - UI trigger
- `TriggerMenuConfirm` - UI trigger
- Various coroutine callbacks (`AdvanceDialogueOneStepCoroutine`, `SelectChoiceCoroutine`)
