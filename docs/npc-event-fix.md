# NPC Event Selection Fix

## Issues

1. **Event not auto-selected**: When interacting with NPCs, the system would return to the API caller when "Event" was an available option instead of auto-selecting it and progressing game state.

2. **Crash on Event selection**: Selecting "Event" caused a fatal crash with "Graphics device is null".

## Root Cause

The HTTP server runs request handlers on **background threads**, but Unity UI operations (creating TextMeshPro, Mesh objects, modifying transforms) **must run on the main thread**.

When `SelectDialogueOptionInternal` called `display.ShowDialogue(nextDialogue)` from a background thread, it triggered:
```
MenuList:AddTextItem -> TMPro.TextMeshPro:Awake -> UnityEngine.Mesh:.ctor
```

This crashed because the graphics context is only available on the main thread.

Stack trace excerpt:
```
Graphics device is null.
Caught fatal signal - signo:5
#6  TMPro.TextMeshPro:Awake()
#11 MenuList:AddDefaultItem()
#14 DialogueDisplay:ShowDialogue()
#15 ActionHandler:SelectDialogueOptionInternal()
```

## Solution

### 1. Main Thread Dispatcher (Plugin.cs)

Added a thread-safe queue and dispatcher:

```csharp
private static readonly ConcurrentQueue<Action> _mainThreadQueue;

public static void RunOnMainThreadAndWait(Action action, int timeoutMs = 10000)
{
    if (IsMainThread) { action(); return; }

    var completedEvent = new ManualResetEventSlim(false);
    _mainThreadQueue.Enqueue(() => { action(); completedEvent.Set(); });
    completedEvent.Wait(timeoutMs);
}

private void Update()
{
    while (_mainThreadQueue.TryDequeue(out var action))
        action();
}
```

### 2. Auto-Select Event Option (ActionHandler.cs)

Added `FindEventOptionIndex()` to detect "Event" in dialogue options and modified `AutoProgressDialogue()` to auto-select it:

```csharp
int eventIndex = FindEventOptionIndex(data.DialogueOptions);
if (eventIndex >= 0)
{
    SelectDialogueOptionInternal(eventIndex);
    continue;
}
```

### 3. Wrapped UI Operations

All UI-touching code now runs on main thread:

- `ExecuteNpcInteract`: Teleport + ForceStart()
- `AutoProgressDialogue`: display.OnConfirm()
- `SelectDialogueOptionInternal`: All dialogue selection
- `ExecuteDialogueChoice`: UI selection + dialogue progression

## Flow

```
HTTP Request (background thread)
    |
    v
RunOnMainThreadAndWait() -- queues action
    |                           |
    | (blocks)                  v
    |                    Update() processes queue
    |                           |
    |                           v
    |                    UI operation executes
    |                           |
    v  <-- completedEvent.Set() |
Returns response
```

## Files Modified

- `Plugin.cs`: Added main thread dispatcher
- `ActionHandler.cs`: Added Event detection, wrapped UI calls
