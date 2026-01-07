#!/bin/bash

STEAM_APPID=2288470

echo "Launching Aethermancer (App ID: $STEAM_APPID) via Steam..."
echo "HTTP server will be available at http://localhost:8080"
echo ""
echo "Endpoints:"
echo "  GET  /health        - Check if game is ready"
echo "  GET  /state         - Get current game state (JSON)"
echo "  GET  /state?format=text - Get state as compact text"
echo "  GET  /actions       - Get valid actions for current actor"
echo "  POST /combat/action - Execute combat action"
echo ""

steam steam://rungameid/$STEAM_APPID

echo "Game launch requested. Waiting for server..."
echo "(Press Ctrl+C to stop waiting)"
echo ""

# Poll until server responds
for i in {1..60}; do
    if curl -s http://localhost:8080/health > /dev/null 2>&1; then
        echo "Server is up!"
        curl -s http://localhost:8080/health | python3 -m json.tool 2>/dev/null || curl -s http://localhost:8080/health
        exit 0
    fi
    sleep 1
    echo -n "."
done

echo ""
echo "Timeout waiting for server. Check if game started correctly."
exit 1
