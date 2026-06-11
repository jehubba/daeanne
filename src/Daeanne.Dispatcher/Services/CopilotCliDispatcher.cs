using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Daeanne.Shared.Models;
using Microsoft.Extensions.Options;

namespace Daeanne.Dispatcher.Services;

/// <summary>
/// Dispatches tasks by cold-starting the GitHub Copilot CLI in non-interactive mode.
/// Invocation: copilot --agent &lt;name&gt; -p "&lt;prompt&gt;" --silent --no-ask-user --allow-all-tools -C &lt;workdir&gt;
/// Resume invocation: copilot --resume &lt;session-id&gt; -p "&lt;orienting-prompt&gt;" --silent ...
/// </summary>
public class CopilotCliDispatcher(
    IOptions<DispatchConfig> config,
    PreferenceMemoryService preferenceMemory,
    ILogger<CopilotCliDispatcher> logger)
    : IAgentDispatcher
{
    private readonly DispatchConfig _config = config.Value;
    private readonly PreferenceMemoryService _preferenceMemory = preferenceMemory;

    public async Task<DispatchResult?> TryResumeAsync(AgentTask task, string workDir, CancellationToken ct = default)
    {
        // Sessions are named by task ID (--name at dispatch time), so --resume <taskId> works directly.
        var resumeKey = task.Id.ToString();
        var sessionMdExists = File.Exists(Path.Combine(workDir, "session.md"));
        if (!sessionMdExists)
        {
            logger.LogDebug("TryResumeAsync: no session found for {TaskId}", task.Id);
            return null;
        }

        var planDoc        = Path.Combine(workDir, "daeanne-plan.md");
        var callbacksDir   = Path.Combine(workDir, "callbacks");
        var callbackFiles  = Directory.Exists(callbacksDir)
            ? Directory.GetFiles(callbacksDir, "*.json")
            : [];

        string orientingPrompt;

        if (callbackFiles.Length > 0)
        {
            // ── Callback-triggered resume ─────────────────────────────────────
            // One or more sub-tasks completed and posted results. Inject them
            // directly so the agent cannot miss them and re-dispatch.
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"You are resuming task {task.Id}.");
            sb.AppendLine();
            sb.AppendLine($"⚠ CALLBACK RESUME: {callbackFiles.Length} sub-task result(s) have arrived.");
            sb.AppendLine("DO NOT dispatch new sub-tasks. DO NOT re-read your plan and re-execute it.");
            sb.AppendLine("Your job now is to SYNTHESIZE the results below and complete the original request.");
            sb.AppendLine();
            sb.AppendLine("── Sub-task results ──────────────────────────────────────────────");
            foreach (var f in callbackFiles)
            {
                var name    = Path.GetFileNameWithoutExtension(f);
                var content = await File.ReadAllTextAsync(f, ct);
                sb.AppendLine($"[{name}]");
                sb.AppendLine(content);
                sb.AppendLine();
            }
            sb.AppendLine("── End of sub-task results ───────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine($"Your plan doc is at: {planDoc}");
            sb.AppendLine("Read your plan doc to understand the original request and what you were waiting for.");
            sb.AppendLine("Then synthesize the sub-task results, deliver to the user, and close this task.");

            orientingPrompt = sb.ToString();
            logger.LogInformation("Resuming task {TaskId} with {Count} callback result(s) injected",
                task.Id, callbackFiles.Length);
        }
        else
        {
            // ── Interruption resume (no callbacks) ────────────────────────────
            orientingPrompt = $"""
                You are resuming task {task.Id}. Your previous session was interrupted.

                Your plan doc is at: {planDoc}

                Read your plan doc first. Then check the current status of any sub-tasks
                listed in your Actions section using:
                  Invoke-RestMethod "http://127.0.0.1:47777/tasks/<sub-task-id>"

                Then continue from where you left off — skip anything already checked off,
                complete anything remaining, and close the task as normal.
                """;
            logger.LogInformation("Resuming task {TaskId} (interruption — no callbacks)", task.Id);
        }

        var psi = new ProcessStartInfo
        {
            FileName               = _config.CopilotExe,
            RedirectStandardOutput = !_config.ShowAgentWindow,
            RedirectStandardError  = !_config.ShowAgentWindow,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            WorkingDirectory       = workDir
        };

        psi.ArgumentList.Add("--resume"); psi.ArgumentList.Add(resumeKey);
        psi.ArgumentList.Add("-p");       psi.ArgumentList.Add(orientingPrompt);
        psi.ArgumentList.Add("--silent");
        psi.ArgumentList.Add("--no-ask-user");
        psi.ArgumentList.Add("--allow-all-tools");
        psi.ArgumentList.Add("--allow-all-paths");
        psi.ArgumentList.Add("--allow-all-urls");
        psi.ArgumentList.Add("--share");
        psi.ArgumentList.Add(Path.Combine(workDir, "session.md"));

        if (_config.ShowAgentWindow)
            psi = BuildWindowedResumeProcess(psi, resumeKey, orientingPrompt, workDir);

        return await RunProcessAsync(task.Id, psi, workDir, ct);
    }

    public async Task<DispatchResult> DispatchAsync(AgentTask task, CancellationToken ct = default)
    {
        var agentName = _config.GetAgentName(task.Type);
        if (agentName is null)
            return new DispatchResult(false, null, $"No agent configured for task type '{task.Type}'.");

        // Per-task working directory keeps outputs isolated
        var workDir = TaskDirManager.ActivePath(_config.ResolvedWorkDir, task.Id, task.IsScheduled);
        Directory.CreateDirectory(workDir);

        var prompt = BuildPrompt(task, workDir);
        logger.LogInformation("Dispatching task {TaskId} ({Type}) via agent '{Agent}' in {WorkDir}",
            task.Id, task.Type, agentName, workDir);

        var psi = new ProcessStartInfo
        {
            FileName               = _config.CopilotExe,
            RedirectStandardOutput = !_config.ShowAgentWindow,
            RedirectStandardError  = !_config.ShowAgentWindow,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            WorkingDirectory       = workDir
        };

        psi.ArgumentList.Add("--agent"); psi.ArgumentList.Add(agentName);
        psi.ArgumentList.Add("--name");  psi.ArgumentList.Add(task.SessionName ?? task.Id.ToString());
        psi.ArgumentList.Add("-p");      psi.ArgumentList.Add(prompt);
        psi.ArgumentList.Add("--silent");
        psi.ArgumentList.Add("--no-ask-user");
        psi.ArgumentList.Add("--allow-all-tools");
        psi.ArgumentList.Add("--allow-all-paths");
        psi.ArgumentList.Add("--allow-all-urls");
        psi.ArgumentList.Add("--share");
        psi.ArgumentList.Add(Path.Combine(workDir, "session.md"));

        if (_config.ShowAgentWindow)
            psi = BuildWindowedProcess(psi, agentName, prompt, workDir);

        var timeoutOverride = _config.GetTimeoutMinutes(task.Type);
        return await RunProcessAsync(task.Id, psi, workDir, ct, timeoutOverride);
    }

    // ─── Shared process runner ────────────────────────────────────────────────

    // Matches ANSI/VT100 escape sequences (CSI codes, OSC, single-char escapes).
    private static readonly Regex AnsiEscapeRegex = new(
        @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~]|\][^\x07]*\x07)",
        RegexOptions.Compiled);

    private static string StripAnsi(string s) => AnsiEscapeRegex.Replace(s, "");

    private async Task<DispatchResult> RunProcessAsync(
        Guid taskId, ProcessStartInfo psi, string workDir, CancellationToken ct,
        int? timeoutMinutesOverride = null)
    {
        var timeoutMinutes = timeoutMinutesOverride ?? _config.TaskTimeoutMinutes;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));

        Process? process = null;
        try
        {
            var cmdLine = psi.FileName + " " + string.Join(" ",
                psi.ArgumentList.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            logger.LogInformation("Task {TaskId}: launching (timeout={Min}m): {CmdLine}",
                taskId, timeoutMinutes, cmdLine);

            process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start copilot process.");

            string rawStdout = string.Empty, stderr = string.Empty;

            if (_config.ShowAgentWindow)
            {
                await process.WaitForExitAsync(timeoutCts.Token);
                var outputFile = Path.Combine(workDir, "agent-output.txt");
                rawStdout = File.Exists(outputFile) ? await File.ReadAllTextAsync(outputFile, ct) : string.Empty;
            }
            else
            {
                rawStdout = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                stderr    = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
                await process.WaitForExitAsync(timeoutCts.Token);
            }

            if (process.ExitCode != 0)
            {
                var isAuthError =
                    stderr.Contains("Authorization error", StringComparison.OrdinalIgnoreCase) ||
                    stderr.Contains("you may need to run /login", StringComparison.OrdinalIgnoreCase);

                if (isAuthError)
                    logger.LogError(
                        "Task {TaskId}: Copilot CLI auth expired (exit {Code}). " +
                        "Run /login in this Copilot CLI session, then promote the task to Pending.",
                        taskId, process.ExitCode);
                else
                    logger.LogWarning("Task {TaskId} agent exited with code {Code}. stderr: {Err}",
                        taskId, process.ExitCode, stderr);

                return new DispatchResult(false, null,
                    $"Agent exited with code {process.ExitCode}. stderr: {stderr.Trim()}",
                    IsAuthError: isAuthError);
            }

            // Strip ANSI/VT100 escape codes — the CLI may emit progress spinners, colour,
            // or cursor-control sequences that make the output unreadable to downstream agents.
            var stdout    = StripAnsi(rawStdout).Trim();
            var ansiFound = stdout.Length != rawStdout.TrimEnd().Length;

            if (ansiFound)
                logger.LogDebug("Task {TaskId}: stripped ANSI codes from output " +
                    "(raw {RawLen} chars → cleaned {CleanLen} chars).\nRaw output:\n{Raw}",
                    taskId, rawStdout.Length, stdout.Length, rawStdout);

            // Warn when stripping reduced a non-trivial output to near-nothing — that indicates
            // the sub-agent produced only terminal UI noise and no readable content.
            if (ansiFound && stdout.Length < 20 && rawStdout.TrimEnd().Length > 100)
            {
                var warning =
                    $"\n\n> ⚠ **Dispatcher warning**: sub-agent output was {rawStdout.TrimEnd().Length} bytes " +
                    $"raw but only {stdout.Length} chars after ANSI stripping. " +
                    $"The output may be unusable — consider retrying or doing this work inline.\n";

                logger.LogWarning("Task {TaskId}: near-empty output after ANSI strip " +
                    "(raw={RawLen}, stripped={StrippedLen}). Writing warning to plan doc.",
                    taskId, rawStdout.TrimEnd().Length, stdout.Length);

                TryAppendToPlanDoc(workDir, warning);
            }

            logger.LogInformation("Task {TaskId} completed. Output length: {Len}", taskId, stdout.Length);

            var resultJson = JsonSerializer.Serialize(new
            {
                response   = stdout,
                workDir,
                sessionLog = Path.Combine(workDir, "session.md")
            });

            return new DispatchResult(true, resultJson, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            KillProcessTree(process, taskId, "cancelled");
            return new DispatchResult(false, null, "Dispatch was cancelled.");
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process, taskId, "timed out");
            logger.LogWarning("Task {TaskId} timed out after {Min} minutes.", taskId, timeoutMinutes);
            return new DispatchResult(false, null, $"Task timed out after {timeoutMinutes} minutes.");
        }
        catch (Exception ex)
        {
            KillProcessTree(process, taskId, "errored");
            logger.LogError(ex, "Unexpected error dispatching task {TaskId}", taskId);
            return new DispatchResult(false, null, ex.Message);
        }
    }

    private void KillProcessTree(Process? process, Guid taskId, string reason)
    {
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                logger.LogInformation("Task {TaskId}: killed process tree ({Reason}).", taskId, reason);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Task {TaskId}: failed to kill process tree ({Reason}).", taskId, reason);
        }
    }

    private static void TryAppendToPlanDoc(string workDir, string text)
    {
        try
        {
            var planDoc = Path.Combine(workDir, "daeanne-plan.md");
            if (File.Exists(planDoc))
                File.AppendAllText(planDoc, text);
        }
        catch { /* best-effort — never fail the task over a plan doc write */ }
    }

    // ─── Session ID extraction ────────────────────────────────────────────────

    private static string? ExtractSessionId(string workDir)
    {
        var sessionPath = Path.Combine(workDir, "session.md");
        if (!File.Exists(sessionPath)) return null;

        // session.md contains: **Session ID:** `<guid>`
        var content = File.ReadAllText(sessionPath);
        var match   = System.Text.RegularExpressions.Regex.Match(
            content, @"\*\*Session ID:\*\*\s+`([^`]+)`");

        return match.Success ? match.Groups[1].Value : null;
    }

    private string BuildPrompt(AgentTask task, string workDir)
    {
        var context = string.IsNullOrWhiteSpace(task.ContextJson)
            ? string.Empty
            : $"\n\nAdditional context:\n{task.ContextJson}";
        var preferences = _preferenceMemory.BuildPrincipalPreferencesBlock();

        // Inject callback contract when this task was dispatched by a parent orchestrator
        var callbackSection = string.Empty;
        if (task.ParentTaskId.HasValue)
        {
            var baseUrl  = _config.DispatcherUrl ?? "http://127.0.0.1:47777";
            var parentId = task.ParentTaskId.Value;
            var taskId   = task.Id;

            // Build PowerShell snippets separately to avoid brace-escaping conflicts in raw literals
            var ackSnippet = "Invoke-RestMethod -Method Post \"" + baseUrl + "/tasks/" + parentId + "/callback/ack\" `\n"
                           + "  -Body (@{ subtaskId = \"" + taskId + "\" } | ConvertTo-Json) -ContentType \"application/json\"";
            var resultSnippet = "Invoke-RestMethod -Method Post \"" + baseUrl + "/tasks/" + parentId + "/callback\" `\n"
                              + "  -Body (@{ subtaskId = \"" + taskId + "\"; summary = \"...\"; resultPath = \"...\"; succeeded = $true } | ConvertTo-Json) `\n"
                              + "  -ContentType \"application/json\"";

            callbackSection = $"""


                ## Callback Contract (REQUIRED)
                You were dispatched as a sub-task by parent task {parentId}.

                parent_task_id:   {parentId}
                callback_ack_url: {baseUrl}/tasks/{parentId}/callback/ack
                callback_url:     {baseUrl}/tasks/{parentId}/callback

                Step 1 — ACK immediately on startup, before any work:
                  {ackSnippet}

                Step 2 — POST result when complete:
                  {resultSnippet}

                Both steps are mandatory. Missing the ACK means the orchestrator thinks you never started.
                """;
        }

        // dispatched_at lets agents include duration in their output
        return $"""
            ## Character
            Character traits live in the static agent profile and are unchanged by this prompt.

            {preferences}

            task_id: {task.Id}
            task_type: {task.Type}
            output_path: {workDir}
            dispatched_at: {DateTime.UtcNow:O}{callbackSection}

            --- BEGIN TASK ---
            {task.Prompt}{context}
            --- END TASK ---
            """;
    }

    /// <summary>
    /// Writes a prompt file and returns a ProcessStartInfo that opens a new PowerShell
    /// window running the copilot agent. The window is visible and closes when the agent exits.
    /// Used when Dispatch:ShowAgentWindow is true.
    /// </summary>
    private static ProcessStartInfo BuildWindowedProcess(
        ProcessStartInfo _, string agentName, string prompt, string workDir)
    {
        var promptFile  = Path.Combine(workDir, "prompt.txt");
        var outputFile  = Path.Combine(workDir, "agent-output.txt");
        var sessionMd   = Path.Combine(workDir, "session.md");
        File.WriteAllText(promptFile, prompt);

        // Tee-Object writes output to agent-output.txt AND shows it in the window.
        // Read-Host keeps the window open after the agent exits so you can review.
        var taskId = Path.GetFileName(workDir);
        var script = $$"""
            $host.UI.RawUI.WindowTitle = 'Daeanne | {{taskId}}'
            Set-Location '{{workDir.Replace("'", "''")}}'
            $prompt = Get-Content '{{promptFile.Replace("'", "''")}}' -Raw
            $copilotArgs = @('--agent', '{{agentName}}', '-p', $prompt, '--silent', '--no-ask-user', '--allow-all-tools', '--allow-all-paths', '--allow-all-urls', '--share', '{{sessionMd.Replace("'", "''")}}'  )
            & copilot @copilotArgs 2>&1 | Tee-Object -FilePath '{{outputFile.Replace("'", "''")}}'
            $ec = $LASTEXITCODE
            Write-Host ""
            if ($ec -ne 0) {
                Write-Host "--- Agent exited with code $ec. Press Enter to close. ---"
                Read-Host
            }
            """;

        var scriptFile = Path.Combine(workDir, "run.ps1");
        File.WriteAllText(scriptFile, script);

        return new ProcessStartInfo
        {
            FileName  = "powershell.exe",
            Arguments = $"-File \"{scriptFile}\"",
            UseShellExecute = true,
            CreateNoWindow  = false,
            WorkingDirectory = workDir
        };
    }

    private static ProcessStartInfo BuildWindowedResumeProcess(
        ProcessStartInfo _, string sessionId, string prompt, string workDir)
    {
        var promptFile = Path.Combine(workDir, "resume-prompt.txt");
        var outputFile = Path.Combine(workDir, "agent-output.txt");
        var sessionMd  = Path.Combine(workDir, "session.md");
        File.WriteAllText(promptFile, prompt);

        var taskId = Path.GetFileName(workDir);
        var script = $$"""
            $host.UI.RawUI.WindowTitle = 'Daeanne | resume | {{taskId}}'
            Set-Location '{{workDir.Replace("'", "''")}}'
            $prompt = Get-Content '{{promptFile.Replace("'", "''")}}' -Raw
            $copilotArgs = @('--resume', '{{sessionId}}', '-p', $prompt, '--silent', '--no-ask-user', '--allow-all-tools', '--allow-all-paths', '--allow-all-urls', '--share', '{{sessionMd.Replace("'", "''")}}'  )
            & copilot @copilotArgs 2>&1 | Tee-Object -FilePath '{{outputFile.Replace("'", "''")}}'
            $ec = $LASTEXITCODE
            Write-Host ""
            if ($ec -ne 0) {
                Write-Host "--- Resumed session exited with code $ec. Press Enter to close. ---"
                Read-Host
            }
            """;

        var scriptFile = Path.Combine(workDir, "resume.ps1");
        File.WriteAllText(scriptFile, script);

        return new ProcessStartInfo
        {
            FileName  = "powershell.exe",
            Arguments = $"-File \"{scriptFile}\"",
            UseShellExecute = true,
            CreateNoWindow  = false,
            WorkingDirectory = workDir
        };
    }
}
