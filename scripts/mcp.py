#!/usr/bin/env python3
"""Generic MCP-for-Unity caller.
Usage:
  python3 mcp.py tool <name> '<json-args>'
  python3 mcp.py exec '<csharp source>'   # runs execute_code
  python3 mcp.py console [count]
"""

import urllib.request, json, sys

BASE = "http://127.0.0.1:6990/mcp"


def _call(method, params=None):
    h = {
        "Content-Type": "application/json",
        "Accept": "application/json, text/event-stream",
    }
    init = json.dumps(
        {
            "jsonrpc": "2.0",
            "method": "initialize",
            "params": {
                "protocolVersion": "2024-11-05",
                "capabilities": {},
                "clientInfo": {"name": "armsmith", "version": "1"},
            },
            "id": 0,
        }
    ).encode()
    r = urllib.request.urlopen(
        urllib.request.Request(BASE, data=init, headers=h, method="POST"), timeout=15
    )
    r.read()
    sess = r.headers.get("Mcp-Session-Id", "")
    h["Mcp-Session-Id"] = sess
    body = {"jsonrpc": "2.0", "method": method, "id": 1}
    if params:
        body["params"] = params
    r = urllib.request.urlopen(
        urllib.request.Request(
            BASE, data=json.dumps(body).encode(), headers=h, method="POST"
        ),
        timeout=120,
    )
    raw = r.read().decode()
    for line in raw.split("\n"):
        if line.startswith("data:"):
            return json.loads(line[5:])
    return json.loads(raw)


def tool(name, args):
    return _call("tools/call", {"name": name, "arguments": args})


def text(res):
    try:
        c = res.get("result", {}).get("content", [{}])
        return (
            "\n".join(x.get("text", "") for x in c) if c else json.dumps(res, indent=2)
        )
    except Exception:
        return json.dumps(res, indent=2)


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "console"
    if cmd == "tool":
        name = sys.argv[2]
        args = json.loads(sys.argv[3]) if len(sys.argv) > 3 else {}
        print(text(tool(name, args)))
    elif cmd == "exec":
        src = sys.argv[2]
        print(text(tool("execute_code", {"action": "execute", "code": src})))
    elif cmd == "console":
        n = int(sys.argv[2]) if len(sys.argv) > 2 else 30
        print(text(tool("read_console", {"action": "get", "count": n})))
    else:
        print("unknown cmd")
