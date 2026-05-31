namespace CodexAuthSwitcher.Services;

public sealed class CommandResult
{
    public int ExitCode { get; init; }

    public string Output { get; init; } = "";

    public bool IsSuccess => ExitCode == 0;
}
