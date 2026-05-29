#!/usr/bin/env python3
"""Unity MCP helper — run as: python3 unity_mcp.py <command>"""

import urllib.request, json, sys, time

BASE = "http://127.0.0.1:6990/mcp"  # PERMANENT: always port 6990


def call(method, params=None):
    headers = {
        "Content-Type": "application/json",
        "Accept": "application/json, text/event-stream",
    }
    # Initialize to get session
    init_body = json.dumps(
        {
            "jsonrpc": "2.0",
            "method": "initialize",
            "params": {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": {"name": "claude-code", "version": "1.0"},
            },
            "id": 0,
        }
    ).encode()
    req = urllib.request.Request(BASE, data=init_body, headers=headers, method="POST")
    resp = urllib.request.urlopen(req, timeout=10)
    resp.read()
    session = resp.headers.get("Mcp-Session-Id", "")

    # Actual call
    headers["Mcp-Session-Id"] = session
    body = {"jsonrpc": "2.0", "method": method, "id": 1}
    if params:
        body["params"] = params
    req = urllib.request.Request(
        BASE, data=json.dumps(body).encode(), headers=headers, method="POST"
    )
    resp = urllib.request.urlopen(req, timeout=20)
    raw = resp.read().decode()
    for line in raw.split("\n"):
        if line.startswith("data:"):
            return json.loads(line[5:])
    return json.loads(raw)


def tool(name, **params):
    return call("tools/call", {"name": name, "arguments": params})


def result_text(r):
    try:
        content = r.get("result", {}).get("content", [{}])
        return content[0].get("text", "") if content else str(r)
    except:
        return str(r)


def resource(uri):
    r = call("resources/read", {"uri": uri})
    try:
        text = r["result"]["contents"][0]["text"]
        return json.loads(text)
    except:
        return r


cmd = sys.argv[1] if len(sys.argv) > 1 else "state"

if cmd == "state":
    s = resource("mcpforunity://editor_state")
    print(json.dumps(s, indent=2))

elif cmd == "console":
    r = tool("read_console", count=30)
    print(result_text(r))

elif cmd == "play":
    r = tool("manage_editor", action="set_play_mode", play_mode_state="Playing")
    print(result_text(r))

elif cmd == "stop":
    r = tool("manage_editor", action="set_play_mode", play_mode_state="Stopped")
    print(result_text(r))

elif cmd == "hierarchy":
    r = tool("manage_scene", action="get_hierarchy", page_size=50)
    print(result_text(r))

elif cmd == "errors":
    r = tool("read_console", count=50, log_type="Error")
    print(result_text(r))

elif cmd == "warnings":
    r = tool("read_console", count=20, log_type="Warning")
    print(result_text(r))

elif cmd == "save":
    r = tool("manage_scene", action="save")
    print(result_text(r))

elif cmd == "tools":
    r = call("tools/list")
    for t in r.get("result", {}).get("tools", []):
        print(t["name"])
