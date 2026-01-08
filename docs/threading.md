# Threading Pattern

HTTP requests run on background threads, but Unity APIs must be accessed from the main thread.

## The Problem

```csharp
// BAD: Blocks main thread, freezes game
Plugin.RunOnMainThreadAndWait(() => {
    while (!IsReady()) {
        Thread.Sleep(50);  // Unity can't update!
    }
});
```

## The Solution

Dispatch checks to main thread, sleep on HTTP thread:

```csharp
// GOOD: Main thread stays responsive
while (true) {
    bool ready = false;
    Plugin.RunOnMainThreadAndWait(() => ready = IsReady());
    if (ready) break;
    Thread.Sleep(50);  // HTTP thread sleeps, Unity keeps running
}
```

## Key Methods

- `Plugin.RunOnMainThreadAndWait()` - dispatch Unity calls to main thread
- `Plugin.IsMainThread` - check current thread
- All `Thread.Sleep` calls should happen on HTTP thread, not main thread
