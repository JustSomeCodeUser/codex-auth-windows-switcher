namespace CodexAuthSwitcher.Models;

public sealed class CodexAccount
{
    public bool IsActive { get; init; }

    public string Selector { get; init; } = "";

    public string Email { get; init; } = "";

    public string Plan { get; init; } = "";

    public string FiveHourUsage { get; init; } = "";

    public string WeeklyUsage { get; init; } = "";

    public string LastActivity { get; init; } = "";

    public string ActiveMarker => IsActive ? "*" : "";

    public string DisplayName => string.IsNullOrWhiteSpace(Email)
        ? Selector
        : $"{Selector}  {Email}";
}
