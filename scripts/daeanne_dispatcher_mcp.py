#!/usr/bin/env python3
from pathlib import Path
import os

import httpx
from mcp.server.fastmcp import FastMCP

DISPATCHER_URL = os.getenv("DAEANNE_DISPATCHER_URL", "http://127.0.0.1:47777").rstrip("/")
KEY_FILE = Path(
    os.getenv("DAEANNE_DISPATCHER_KEY_FILE", "~/.daeanne/secrets/dispatcher-api-key.txt")
).expanduser()


def _headers() -> dict[str, str]:
    api_key = KEY_FILE.read_text(encoding="utf-8").strip()
    if not api_key:
        raise RuntimeError(f"Dispatcher API key file is empty: {KEY_FILE}")
    return {"X-Daeanne-Key": api_key}


def _request(method: str, path: str, **kwargs) -> dict | list:
    with httpx.Client(base_url=DISPATCHER_URL, timeout=30.0, headers=_headers()) as client:
        response = client.request(method, path, **kwargs)
        response.raise_for_status()
        return response.json() if response.content else {}


mcp = FastMCP("daeanne-dispatcher")


@mcp.tool()
def dispatch_task(type: str, prompt: str) -> dict:
    task = _request("POST", "/tasks", json={"type": type, "prompt": prompt})
    return {"task_id": task.get("id"), "task": task}


@mcp.tool()
def get_task_status(task_id: str) -> dict:
    task = _request("GET", f"/tasks/{task_id}")
    return {"status": task.get("status"), "resultJson": task.get("resultJson"), "task": task}


@mcp.tool()
def list_tasks(status: str | None = None, take: int = 50) -> list:
    params: dict[str, str | int] = {"take": take}
    if status:
        params["status"] = status
    return _request("GET", "/tasks", params=params)


@mcp.tool()
def send_email(to: str, subject: str, body: str) -> dict:
    email = _request("POST", "/outbox/email", json={"to": to, "subject": subject, "body": body})
    return {"email_id": email.get("id"), "email": email}


@mcp.tool()
def health_check() -> dict:
    return _request("GET", "/health")


if __name__ == "__main__":
    mcp.run()
