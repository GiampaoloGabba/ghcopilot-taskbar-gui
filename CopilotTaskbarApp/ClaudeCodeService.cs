using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CopilotTaskbarApp;

public class ClaudeCodeService : IAiService
{
    private readonly AiProviderSettings _settings;

    public ClaudeCodeService(AiProviderSettings settings)
    {
        _settings = settings;
    }

    public string ProviderName => "Claude Code";

    private string ModelFlag => _settings.ClaudeModel switch
    {
        ClaudeModel.Opus => "opus",
        ClaudeModel.Haiku => "haiku",
        _ => "sonnet"
    };

    public async Task<string> GetResponseAsync(string prompt, string? context = null, string? imageBase64 = null, List<ChatMessage>? recentMessages = null, CancellationToken cancellationToken = default)
    {
        var methodStartTime = DateTime.UtcNow;
        System.Diagnostics.Debug.WriteLine($"[ClaudeCodeService] ===== Request START at {methodStartTime:HH:mm:ss.fff} =====");

        try
        {
            var fullPrompt = BuildPrompt(prompt, context, recentMessages);

            System.Diagnostics.Debug.WriteLine($"[ClaudeCodeService] ===== PROMPT ({fullPrompt.Length} chars) =====");
            System.Diagnostics.Debug.WriteLine($"[ClaudeCodeService] {fullPrompt}");
            System.Diagnostics.Debug.WriteLine($"[ClaudeCodeService] ===== END PROMPT =====");

            var systemPrompt = BuildSystemPrompt(context);

            var psi = new ProcessStartInfo
            {
                FileName = "claude",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add("--output-format");
            psi.ArgumentList.Add("text");
            psi.ArgumentList.Add("--max-turns");
            psi.ArgumentList.Add("1");
            psi.ArgumentList.Add("--no-session-persistence");
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(ModelFlag);

            if (_settings.ClaudeSkipPermissions)
            {
                psi.ArgumentList.Add("--dangerously-skip-permissions");
            }

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                psi.ArgumentList.Add("--append-system-prompt");
                psi.ArgumentList.Add(systemPrompt);
            }

            psi.ArgumentList.Add(fullPrompt);

            System.Diagnostics.Debug.WriteLine($"[ClaudeCodeService] Launching: claude (model={ModelFlag}, skipPerms={_settings.ClaudeSkipPermissions})");

            var sendStart = DateTime.UtcNow;
            using var process = Process.Start(psi);
            if (process == null)
            {
                return "Failed to start Claude Code CLI. Make sure 'claude' is installed and in your PATH.";
            }

            // Read output asynchronously
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

            // Wait for process with timeout
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(300));

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                var elapsed = DateTime.UtcNow - methodStartTime;
                return $"Request timed out after {elapsed.TotalSeconds:F0} seconds. Claude Code CLI did not respond in time.";
            }

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);

            var sendElapsed = DateTime.UtcNow - sendStart;
            var totalElapsed = DateTime.UtcNow - methodStartTime;
            System.Diagnostics.Debug.WriteLine($"[ClaudeCodeService] Process completed in {sendElapsed.TotalSeconds:F2}s (exit code: {process.ExitCode})");
            System.Diagnostics.Debug.WriteLine($"[ClaudeCodeService] Total request time: {totalElapsed.TotalSeconds:F2}s");

            if (process.ExitCode != 0)
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeCodeService] STDERR: {error}");

                var errorLower = (error ?? "").ToLowerInvariant();
                if (errorLower.Contains("auth") || errorLower.Contains("api key") || errorLower.Contains("unauthorized"))
                {
                    return "Authentication required for Claude Code.\n\n" +
                           "Please authenticate:\n" +
                           "Run: claude login\n" +
                           "Or set your API key: export ANTHROPIC_API_KEY=your_key\n\n" +
                           "Then restart this application.";
                }

                return $"Claude Code error (exit {process.ExitCode}): {error?.Trim() ?? output?.Trim() ?? "Unknown error"}";
            }

            var result = output?.Trim() ?? "";

            if (!string.IsNullOrEmpty(result))
            {
                System.Diagnostics.Debug.WriteLine($"[ClaudeCodeService] ===== RESPONSE ({result.Length} chars) =====");
                System.Diagnostics.Debug.WriteLine($"[ClaudeCodeService] {result}");
                System.Diagnostics.Debug.WriteLine($"[ClaudeCodeService] ===== END RESPONSE =====");
                return result;
            }

            return "No response received from Claude Code.";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "Claude Code CLI ('claude') not found. Make sure it is installed and available in your PATH.";
        }
        catch (Exception ex)
        {
            var totalElapsed = DateTime.UtcNow - methodStartTime;
            System.Diagnostics.Debug.WriteLine($"[ClaudeCodeService] ERROR after {totalElapsed.TotalSeconds:F2}s: {ex.GetType().Name}");
            System.Diagnostics.Debug.WriteLine($"[ClaudeCodeService] {ex.Message}");
            return $"Error: {ex.Message}";
        }
    }

    public async Task<bool> CheckAuthenticationAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "claude",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return false;

            var outputTask = process.StandardOutput.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            await outputTask.ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public ValueTask DisposeAsync()
    {
        // No persistent resources to clean up - each request is a separate process
        return ValueTask.CompletedTask;
    }

    private static string BuildSystemPrompt(string? context)
    {
        if (string.IsNullOrEmpty(context) ||
            (!context.Contains("[Active Focus]") && !context.Contains("[Open Folders]")))
        {
            return "";
        }

        return "You are a desktop assistant integrated into Windows 11. Answer questions based on what the user is actively doing.\n" +
               "RESPONSE GUIDELINES:\n" +
               "- Avoid markdown formatting (no bold, bullets, or headers) - use plain conversational text\n" +
               "- When folders/projects are open: mention the specific path and tailor suggestions to that directory\n" +
               "- Be curious: analyze Active Focus, Open Folders, Open Applications, Background Services, and Environment Variables to understand what the user is working on\n" +
               "- MAINTAIN CONTEXT CONTINUITY with the conversation history\n" +
               "- When Active Focus shows a WSL distribution, prioritize that environment for commands and suggestions\n" +
               "- BE ACTIONABLE: execute imperative commands immediately, do not just provide instructions\n" +
               "- REPORT PARTIAL PROGRESS for multi-step operations\n" +
               "- Give practical, immediate answers\n" +
               "- Keep responses concise unless depth is needed\n" +
               "- ACCURACY IS CRITICAL: Only state facts you are certain about.";
    }

    private static string BuildPrompt(string prompt, string? context, List<ChatMessage>? recentMessages)
    {
        if (string.IsNullOrEmpty(context))
            return prompt;

        if (!context.Contains("[Active Focus]") && !context.Contains("[Open Folders]"))
            return $"Working directory: {context}\n\n{prompt}";

        var sb = new StringBuilder();

        // Add conversation history
        if (recentMessages != null && recentMessages.Count > 0)
        {
            var recentCount = Math.Min(10, recentMessages.Count);
            var relevant = recentMessages.Skip(Math.Max(0, recentMessages.Count - recentCount)).ToList();

            sb.AppendLine("RECENT CONVERSATION:");
            foreach (var msg in relevant)
            {
                var role = msg.Role == "user" ? "User" : "Assistant";
                sb.AppendLine($"{role}: {msg.Content}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("CURRENT CONTEXT:");
        sb.AppendLine(context);
        sb.AppendLine();
        sb.AppendLine($"Current Question: {prompt}");

        return sb.ToString();
    }

}
