using System.Diagnostics;
using System.Text;
using CodexAuthSwitcher.Models;

namespace CodexAuthSwitcher.Services;

public sealed class CodexAuthService
{
    private const string CodexAppId = "OpenAI.Codex_2p2nqsd0c76g0!App";

    public Task<CommandResult> GetVersionAsync(CancellationToken cancellationToken = default)
        => RunCodexAuthAsync("--version", cancellationToken);

    public async Task<(CommandResult Result, IReadOnlyList<CodexAccount> Accounts)> ListAccountsAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await RunCodexAuthAsync("list --skip-api", cancellationToken);
        var accounts = result.IsSuccess
            ? ParseAccounts(result.Output)
            : Array.Empty<CodexAccount>();

        return (result, accounts);
    }

    public Task<CommandResult> SwitchAsync(string query, CancellationToken cancellationToken = default)
        => RunCodexAuthAsync($"switch {QuoteForCmd(query)}", cancellationToken);

    public Task<CommandResult> RemoveAsync(string query, CancellationToken cancellationToken = default)
        => RunCodexAuthAsync($"remove {QuoteForCmd(query)}", cancellationToken);

    public Task<CommandResult> StatusAsync(CancellationToken cancellationToken = default)
        => RunCodexAuthAsync("status", cancellationToken);

    public async Task<CommandResult> RestartCodexAppAsync(CancellationToken cancellationToken = default)
    {
        var output = new StringBuilder();
        var targets = FindCodexAppProcesses().ToList();

        output.AppendLine(targets.Count == 0
            ? "No running Codex App processes found."
            : $"Closing {targets.Count} Codex App process(es)...");

        foreach (var process in targets)
        {
            try
            {
                if (!process.HasExited && process.MainWindowHandle != IntPtr.Zero)
                {
                    process.CloseMainWindow();
                }
            }
            catch
            {
                // Some packaged-app helper processes do not expose a main window.
            }
        }

        await Task.Delay(1500, cancellationToken);

        foreach (var process in targets)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                output.AppendLine($"Could not close process {process.Id}: {ex.Message}");
            }
            finally
            {
                process.Dispose();
            }
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $@"shell:AppsFolder\{CodexAppId}",
                UseShellExecute = true,
            });
            output.AppendLine("Codex App launch requested.");

            return new CommandResult
            {
                ExitCode = 0,
                Output = output.ToString().Trim(),
            };
        }
        catch (Exception ex)
        {
            output.AppendLine("Could not relaunch Codex App: " + ex.Message);
            return new CommandResult
            {
                ExitCode = 1,
                Output = output.ToString().Trim(),
            };
        }
    }

    public void StartDeviceLogin()
    {
        var info = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/d /k codex-auth login --device-auth",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal,
        };

        Process.Start(info);
    }

    private static async Task<CommandResult> RunCodexAuthAsync(
        string arguments,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /c codex-auth {arguments}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        try
        {
            using var process = Process.Start(info);
            if (process is null)
            {
                return new CommandResult
                {
                    ExitCode = 127,
                    Output = "Could not start codex-auth.",
                };
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var output = CleanOutput(await stdoutTask, await stderrTask);
            return new CommandResult
            {
                ExitCode = process.ExitCode,
                Output = output,
            };
        }
        catch (Exception ex)
        {
            return new CommandResult
            {
                ExitCode = 127,
                Output = ex.Message,
            };
        }
    }

    private static IReadOnlyList<CodexAccount> ParseAccounts(string output)
    {
        var accounts = new List<CodexAccount>();
        foreach (var rawLine in output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) ||
                line.StartsWith("ACCOUNT", StringComparison.OrdinalIgnoreCase) ||
                line.All(ch => ch == '-'))
            {
                continue;
            }

            var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 3)
            {
                continue;
            }

            var isActive = tokens[0] == "*";
            var offset = isActive ? 1 : 0;
            if (tokens.Length < offset + 3 || !tokens[offset].All(char.IsDigit))
            {
                continue;
            }

            var cursor = offset;
            var selector = tokens[cursor++];
            var email = tokens[cursor++];
            var plan = tokens[cursor++];
            var fiveHour = ReadUsage(tokens, ref cursor);
            var weekly = ReadUsage(tokens, ref cursor);
            var lastActivity = cursor < tokens.Length
                ? string.Join(" ", tokens.Skip(cursor))
                : "-";

            accounts.Add(new CodexAccount
            {
                IsActive = isActive,
                Selector = selector,
                Email = email,
                Plan = plan,
                FiveHourUsage = fiveHour,
                WeeklyUsage = weekly,
                LastActivity = string.IsNullOrWhiteSpace(lastActivity) ? "-" : lastActivity,
            });
        }

        return accounts;
    }

    private static string ReadUsage(string[] tokens, ref int cursor)
    {
        if (cursor >= tokens.Length)
        {
            return "-";
        }

        if (tokens[cursor] == "-")
        {
            cursor++;
            return "-";
        }

        var parts = new List<string> { tokens[cursor++] };
        if (cursor < tokens.Length && tokens[cursor].StartsWith("(", StringComparison.Ordinal))
        {
            while (cursor < tokens.Length)
            {
                parts.Add(tokens[cursor]);
                if (tokens[cursor].EndsWith(")", StringComparison.Ordinal))
                {
                    cursor++;
                    break;
                }

                cursor++;
            }
        }

        return string.Join(" ", parts);
    }

    private static string CleanOutput(string stdout, string stderr)
    {
        var combined = string.Join(Environment.NewLine, new[] { stdout, stderr }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

        var filtered = combined
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Where(line => !line.Contains("conda-script.py", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Contains("Invoke-Expression", StringComparison.OrdinalIgnoreCase))
            .Where(line => !line.Contains("Cannot bind argument to parameter 'Command'", StringComparison.OrdinalIgnoreCase));

        return string.Join(Environment.NewLine, filtered).Trim();
    }

    private static IEnumerable<Process> FindCodexAppProcesses()
    {
        var currentId = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            if (process.Id == currentId)
            {
                process.Dispose();
                continue;
            }

            if (IsCodexAppProcess(process))
            {
                yield return process;
            }
            else
            {
                process.Dispose();
            }
        }
    }

    private static bool IsCodexAppProcess(Process process)
    {
        try
        {
            var processName = process.ProcessName;
            if (!string.Equals(processName, "Codex", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(processName, "codex", StringComparison.Ordinal))
            {
                return false;
            }

            var path = process.MainModule?.FileName ?? "";
            if (path.Contains(@"\WindowsApps\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return string.Equals(processName, "Codex", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(path);
        }
        catch
        {
            return string.Equals(process.ProcessName, "Codex", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static string QuoteForCmd(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "\"\"";
        }

        return "\"" + value.Replace("\"", "\\\"") + "\"";
    }
}
