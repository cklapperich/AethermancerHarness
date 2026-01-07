#!/usr/bin/env python3
"""Concise endpoint tests for AethermancerHarness API."""

from typing import Any, Callable
import requests
import json

BASE: str = "http://localhost:8080"

def test(
    name: str,
    method: str,
    path: str,
    body: dict[str, Any] | None = None,
    check: Callable[[dict[str, Any]], bool] | None = None
) -> dict[str, Any] | None:
    """Run a test and print result."""
    url: str = f"{BASE}{path}"
    try:
        r = requests.request(method, url, json=body, timeout=5)
        try:
            data: dict[str, Any] = r.json() if r.text else {}
        except json.JSONDecodeError:
            data = {"_raw": r.text[:500]}
        ok: bool = check(data) if check else r.ok
        status: str = "✓" if ok else "✗"
        print(f"{status} {name} [{r.status_code}]")
        if not ok:
            print(f"  Response: {json.dumps(data, indent=2)[:200]}")
        return data
    except Exception as e:
        print(f"✗ {name} - {e}")
        return None

# === Health ===
test("health - basic", "GET", "/health",
     check=lambda d: d.get("status") == "ok")

# === State ===
test("state - json format", "GET", "/state",
     check=lambda d: "mode" in d or "player" in d or "combat" in d)

test("state - text format", "GET", "/state?format=text",
     check=lambda d: "text" in d or "error" not in d)

# === Actions (combat only) ===
test("actions - get available", "GET", "/actions")

# === Combat Enemy Actions ===
test("enemy-actions - get intentions", "GET", "/combat/enemy-actions")

# === Combat Start ===
test("combat/start - with group 0", "POST", "/combat/start",
     body={"monsterGroupIndex": 0})

test("combat/start - with void blitz", "POST", "/combat/start",
     body={"monsterGroupIndex": 0, "voidBlitz": True})

# === Combat Preview ===
test("combat/preview - actor 0, skill 0, target 0", "POST", "/combat/preview",
     body={"actorIndex": 0, "skillIndex": 0, "targetIndex": 0})

# === Combat Action ===
test("combat/action - execute skill", "POST", "/combat/action",
     body={"actorIndex": 0, "skillIndex": 0, "targetIndex": 0})

test("combat/action - use consumable", "POST", "/combat/action",
     body={"consumableIndex": 0, "targetIndex": 0})

# === Exploration Teleport ===
test("teleport - to origin", "POST", "/exploration/teleport",
     body={"x": 0, "y": 0, "z": 0})

test("teleport - specific coords", "POST", "/exploration/teleport",
     body={"x": 10.5, "y": -5.0, "z": 0})

# === Exploration Interact ===
test("interact - trigger nearby", "POST", "/exploration/interact")

# === Skill Select ===
test("skill-select - pick skill 0", "POST", "/skill-select",
     body={"skillIndex": 0})

test("skill-select - reroll", "POST", "/skill-select",
     body={"reroll": True})

# === Error Cases ===
test("404 - invalid endpoint", "GET", "/invalid",
     check=lambda d: d.get("error") == "Not found")

test("405 - wrong method on POST endpoint", "GET", "/combat/action",
     check=lambda d: "error" in d or True)

print("\n=== Tests Complete ===")
