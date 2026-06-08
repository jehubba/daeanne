using System.Diagnostics;
using System.Text.Json;
using Daeanne.Shared.Models;
using Microsoft.Extensions.Options;

namespace Daeanne.Dispatcher.Services;

/// <summary>
/// Dispatches tasks by cold-starting the GitHub Copilot CLI in non-interactive mode.
/// Invocation: copilot --agent &lt;name&gt; -p "&lt;prompt&gt;" --silent --no-ask-user --allow-all-tools -C &lt;workdir&gt;
/// </summary>
public class CopilotCliDispatcher(IOptions<DispatchConfig> config, ILogger<CopilotCliDispatcher> logger)
    : IAgentDispatcher
{
    private readonly DispatchConfig _config = config.Value;

    public async Task<DispatchResult> DispatchAsync(AgentTask task, CancellationToken ct = default)
    {
        var agentName = _config.GetAgentName(task.Type);
        if (agentName is null)
            return new DispatchResult(false, null, $"No agent configured for task type '{task.Type}'.");

        // Per-task working directory keeps outputs isolated
        var workDir = Path.Combine(_config.ResolvedWorkDir, task.Id.ToString());
        Directory.CreateDirectory(workDir);

        var prompt = BuildPrompt(task, workDir);
        logger.LogInformation("Dispatching task {TaskId} ({Type}) via agent '{Agent}' in {WorkDir}",
            task.Id, task.Type, agentName, workDir);

        var psi = new ProcessStartInfo
        {
            FileName = "copilot",
            RedirectStandardOutput = !_config.ShowAgentWindow,
            RedirectStandardError  = !_config.ShowAgentWindow,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workDir
        };

        // Use ArgumentList to avoid shell-escaping issues with prompt content
        psi.ArgumentList.Add("--agent"); psi.ArgumentList.Add(agentName);
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

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(_config.TaskTimeoutMinutes));

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start copilot process.");

            string stdout = string.Empty, stderr = string.Empty;

            if (_config.ShowAgentWindow)
            {
                // Windowed mode: output goes to the visible terminal, not captured here.
                // session.md (written by --share) is the durable record.
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            else
            {
                stdout = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
                stderr = await process.StandardError.ReadToEndAsync(timeoutCts.Token);
                await process.WaitForExitAsync(timeoutCts.Token);
            }

            if (process.ExitCode != 0)
            {
                logger.LogWarning("Task {TaskId} agent exited with code {Code}. stderr: {Err}",
                    task.Id, process.ExitCode, stderr);
                return new DispatchResult(false, null,
                    $"Agent exited with code {process.ExitCode}. stderr: {stderr.Trim()}");
            }

            logger.LogInformation("Task {TaskId} completed. Output length: {Len}", task.Id, stdout.Length);

            var resultJson = JsonSerializer.Serialize(new
            {
                response = stdout.Trim(),
                agent = agentName,
                workDir,
                sessionLog = Path.Combine(workDir, "session.md")
            });

            return new DispatchResult(true, resultJson, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new DispatchResult(false, null, "Dispatch was cancelled.");
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Task {TaskId} timed out after {Min} minutes.", task.Id, _config.TaskTimeoutMinutes);
            return new DispatchResult(false, null, $"Task timed out after {_config.TaskTimeoutMinutes} minutes.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error dispatching task {TaskId}", task.Id);
            return new DispatchResult(false, null, ex.Message);
        }
    }

    private static string BuildPrompt(AgentTask task, string workDir)
    {
        var context = string.IsNullOrWhiteSpace(task.ContextJson)
            ? string.Empty
            : $"\n\nAdditional context:\n{task.ContextJson}";

        // Use the exact keywords the research agent's orchestrated-mode detection expects.
        // output_path is the directory; the agent writes <output_path>/<task_id>-research.md
        return $"""
            task_id: {task.Id}
            task_type: {task.Type}
            output_path: {workDir}

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
        // Write prompt to file so we don't have to deal with shell quoting
        var promptFile = Path.Combine(workDir, "prompt.txt");
        File.WriteAllText(promptFile, prompt);

        // PS1 script reads the prompt file and invokes copilot
        var sessionMd = Path.Combine(workDir, "session.md");
        var script = $"""
            Set-Location '{workDir.Replace("'", "''")}'
            $prompt = Get-Content '{promptFile.Replace("'", "''")}' -Raw
            copilot --agent '{agentName}' -p $prompt --silent --no-ask-user --allow-all-tools --allow-all-paths --allow-all-urls --share '{sessionMd.Replace("'", "''")}'
            """;

        var scriptFile = Path.Combine(workDir, "run.ps1");
        File.WriteAllText(scriptFile, script);

        return new ProcessStartInfo
        {
            FileName  = "powershell.exe",
            Arguments = $"-NoExit -File \"{scriptFile}\"",
            UseShellExecute = true,   // opens a new console window
            CreateNoWindow  = false,
            WorkingDirectory = workDir
        };
    }
}
