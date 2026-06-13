#!/usr/bin/env node
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import fetch from "node-fetch";
import { readFileSync } from "fs";
import { homedir } from "os";
import { join } from "path";

const BASE_URL = process.env.DISPATCHER_URL ?? "http://127.0.0.1:47777";

function getApiKey() {
  try {
    const keyPath = join(homedir(), ".daeanne", "secrets", "dispatcher-api-key.txt");
    return readFileSync(keyPath, "utf8").trim();
  } catch {
    return process.env.DISPATCHER_API_KEY ?? "";
  }
}

function headers() {
  const key = getApiKey();
  return {
    "Content-Type": "application/json",
    ...(key ? { "X-Daeanne-Key": key } : {}),
  };
}

async function api(method, path, body) {
  const res = await fetch(`${BASE_URL}${path}`, {
    method,
    headers: headers(),
    ...(body !== undefined ? { body: JSON.stringify(body) } : {}),
  });
  const text = await res.text();
  try { return JSON.parse(text); } catch { return text; }
}

const server = new McpServer({ name: "daeanne-dispatcher", version: "1.0.0" });

server.tool(
  "dispatch_task",
  "Dispatch a task to Daeanne. Returns the task ID immediately — use get_task_status to poll.",
  { type: z.string().describe("Task type: Generic | Research | TrendAnalyzer | Scheduling | Email"),
    prompt: z.string().describe("Full task prompt / instructions for Daeanne") },
  async ({ type, prompt }) => {
    const task = await api("POST", "/tasks", { type, prompt });
    return { content: [{ type: "text", text: `Task dispatched. ID: ${task.id}  Status: ${task.status}` }] };
  }
);

server.tool(
  "get_task_status",
  "Get the current status and result of a Daeanne task.",
  { task_id: z.string().describe("Task ID returned by dispatch_task") },
  async ({ task_id }) => {
    const task = await api("GET", `/tasks/${task_id}`);
    const result = task.resultJson ? JSON.parse(task.resultJson) : null;
    const summary = [
      `ID: ${task.id}`,
      `Type: ${task.type}`,
      `Status: ${task.status}`,
      result?.response ? `\nResponse:\n${result.response}` : "",
      task.error ? `\nError: ${task.error}` : "",
    ].filter(Boolean).join("\n");
    return { content: [{ type: "text", text: summary }] };
  }
);

server.tool(
  "list_tasks",
  "List Daeanne tasks, optionally filtered by status.",
  { status: z.string().optional().describe("Filter: Pending | Running | Succeeded | Failed | Blocked | Deferred"),
    take: z.number().optional().default(20).describe("Max results (default 20)") },
  async ({ status, take }) => {
    const qs = new URLSearchParams();
    if (status) qs.set("status", status);
    qs.set("take", String(take ?? 20));
    const tasks = await api("GET", `/tasks?${qs}`);
    if (!Array.isArray(tasks) || tasks.length === 0) {
      return { content: [{ type: "text", text: "No tasks found." }] };
    }
    const lines = tasks.map(t => `${t.id.slice(0, 8)}  ${t.status.padEnd(12)}  ${t.type.padEnd(16)}  ${t.prompt?.slice(0, 60) ?? ""}`);
    return { content: [{ type: "text", text: ["ID        Status        Type              Prompt", ...lines].join("\n") }] };
  }
);

server.tool(
  "send_email",
  "Queue an outbound email from Daeanne (daeanne-srs@outlook.com).",
  { to: z.string().describe("Recipient email address"),
    subject: z.string(),
    body: z.string().describe("Email body (markdown supported)") },
  async ({ to, subject, body }) => {
    const result = await api("POST", "/outbox/email", { to, subject, body });
    return { content: [{ type: "text", text: `Email queued. ID: ${result.id}  Status: ${result.status}` }] };
  }
);

server.tool(
  "health_check",
  "Check whether the Daeanne Dispatcher is reachable and healthy.",
  {},
  async () => {
    const health = await api("GET", "/health");
    return { content: [{ type: "text", text: JSON.stringify(health, null, 2) }] };
  }
);

const transport = new StdioServerTransport();
await server.connect(transport);
