using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexAuthSwitcher.Models;
using CodexAuthSwitcher.Services;
using Microsoft.Win32;

namespace CodexAuthSwitcher;

public partial class MainWindow : Window
{
    private const int MaxVisibleAccountRows = 5;
    private const double AccountRowHeight = 48;
    private const double AccountHeaderHeight = 50;
    private const double WindowHeightWithoutAccountRows = 620;

    private readonly CodexAuthService codexAuth = new();
    private readonly ObservableCollection<CodexAccount> accounts = new();

    public MainWindow()
    {
        InitializeComponent();
        AccountsGrid.ItemsSource = accounts;
        LoadAppImages();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var darkMode = IsWindowsAppThemeDark();
        ThemeToggle.IsChecked = darkMode;
        ApplyTheme(darkMode);
        await LoadVersionAsync();
        await RefreshAccountsAsync();
    }

    private void ThemeToggle_Changed(object sender, RoutedEventArgs e)
    {
        ApplyTheme(ThemeToggle.IsChecked == true);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await RefreshAccountsAsync();
    }

    private void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            codexAuth.StartDeviceLogin();
            Log("Opened a device-auth login window. Finish login there, then click Refresh here.");
        }
        catch (Exception ex)
        {
            Log("Could not start device-auth login: " + ex.Message);
        }
    }

    private async void Status_Click(object sender, RoutedEventArgs e)
    {
        await RunWithBusyStateAsync("Checking codex-auth status...", async () =>
        {
            var result = await codexAuth.StatusAsync();
            Log(result.Output);
            if (result.Output.Contains("account: disabled", StringComparison.OrdinalIgnoreCase))
            {
                Log("Note: account: disabled means the account API lookup is disabled. Local switching still works.");
            }
        });
    }

    private async void RestartCodex_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this,
            "Restart Codex App now?\n\nThis will close the Codex desktop app and open it again. Any unsaved Codex App work should be settled first.",
            "Restart Codex App",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await RunWithBusyStateAsync("Restarting Codex App...", async () =>
        {
            var result = await codexAuth.RestartCodexAppAsync();
            Log(result.Output);
        });
    }

    private void OpenCodexFolder_Click(object sender, RoutedEventArgs e)
    {
        var path = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".codex");

        if (!System.IO.Directory.Exists(path))
        {
            MessageBox.Show(this, "The .codex folder was not found.", "Codex Auth Switcher",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    private async void SwitchAccount_Click(object sender, RoutedEventArgs e)
    {
        if (GetAccountFromButton(sender) is not { } account)
        {
            return;
        }

        await SwitchToAccountAsync(account, restartAfterSwitch: false);
    }

    private async void SwitchAndRestartAccount_Click(object sender, RoutedEventArgs e)
    {
        if (GetAccountFromButton(sender) is not { } account)
        {
            return;
        }

        await SwitchToAccountAsync(account, restartAfterSwitch: true);
    }

    private async Task SwitchToAccountAsync(CodexAccount account, bool restartAfterSwitch)
    {
        if (account.IsActive)
        {
            Log($"{account.DisplayName} is already active.");
            if (restartAfterSwitch)
            {
                await RestartCodexAppWithConfirmationAsync();
            }

            return;
        }

        var confirm = MessageBox.Show(this,
            restartAfterSwitch
                ? $"Switch Codex to {account.Email} and restart Codex App?"
                : $"Switch Codex to {account.Email}?\n\nAfter switching, restart Codex App or reopen Codex CLI.",
            restartAfterSwitch ? "Switch and restart" : "Switch account",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await RunWithBusyStateAsync($"Switching to {account.DisplayName}...", async () =>
        {
            var query = string.IsNullOrWhiteSpace(account.Email) ? account.Selector : account.Email;
            var result = await codexAuth.SwitchAsync(query);
            Log(result.Output);
            if (result.IsSuccess)
            {
                Log(restartAfterSwitch
                    ? "Switch complete. Restarting Codex App..."
                    : "Switch complete. Click Restart Codex, or reopen Codex CLI, to fully apply it.");
                await RefreshAccountsAsync();

                if (restartAfterSwitch)
                {
                    var restartResult = await codexAuth.RestartCodexAppAsync();
                    Log(restartResult.Output);
                }
            }
        });
    }

    private async void RemoveAccount_Click(object sender, RoutedEventArgs e)
    {
        if (GetAccountFromButton(sender) is not { } account)
        {
            return;
        }

        var confirm = MessageBox.Show(this,
            $"Remove {account.Email} from codex-auth?\n\nThis removes the saved account from the switcher.",
            "Remove account",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await RunWithBusyStateAsync($"Removing {account.DisplayName}...", async () =>
        {
            var query = string.IsNullOrWhiteSpace(account.Email) ? account.Selector : account.Email;
            var result = await codexAuth.RemoveAsync(query);
            Log(result.Output);
            if (result.IsSuccess)
            {
                await RefreshAccountsAsync();
            }
        });
    }

    private async Task LoadVersionAsync()
    {
        var result = await codexAuth.GetVersionAsync();
        VersionText.Text = result.IsSuccess
            ? result.Output
            : "codex-auth not found";
    }

    private async Task RefreshAccountsAsync()
    {
        await RunWithBusyStateAsync("Refreshing accounts...", async () =>
        {
            var (result, parsedAccounts) = await codexAuth.ListAccountsAsync();
            if (!result.IsSuccess)
            {
                Log(result.Output);
                CurrentAccountText.Text = "Could not load accounts";
                return;
            }

            accounts.Clear();
            foreach (var account in parsedAccounts)
            {
                accounts.Add(account);
            }

            var active = accounts.FirstOrDefault(account => account.IsActive);
            CurrentAccountText.Text = active is null
                ? "No active account"
                : $"{active.Selector}  {active.Email}  ({active.Plan})";

            Log(accounts.Count == 0
                ? "No codex-auth accounts found."
                : $"Loaded {accounts.Count} account(s).");

            AdjustWindowForAccountCount(accounts.Count);
        });
    }

    private async Task RunWithBusyStateAsync(string message, Func<Task> action)
    {
        SetControlsEnabled(false);
        BusyText.Text = message;

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Log(ex.Message);
        }
        finally
        {
            BusyText.Text = "Ready";
            SetControlsEnabled(true);
        }
    }

    private void SetControlsEnabled(bool isEnabled)
    {
        AccountsGrid.IsEnabled = isEnabled;
    }

    private void LoadAppImages()
    {
        var baseDir = AppContext.BaseDirectory;
        var iconPath = System.IO.Path.Combine(baseDir, "Assets", "CodexAuthSwitcher.ico");
        var logoPath = System.IO.Path.Combine(baseDir, "Assets", "CodexAuthSwitcher.png");

        if (System.IO.File.Exists(iconPath))
        {
            Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute));
        }

        if (System.IO.File.Exists(logoPath))
        {
            HeaderLogo.Source = new BitmapImage(new Uri(logoPath, UriKind.Absolute));
        }
    }

    private void AdjustWindowForAccountCount(int accountCount)
    {
        var visibleRows = Math.Clamp(accountCount, 1, MaxVisibleAccountRows);
        AccountsGrid.MaxHeight = AccountHeaderHeight + visibleRows * AccountRowHeight + 4;

        var targetHeight = WindowHeightWithoutAccountRows + visibleRows * AccountRowHeight;
        var maxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 100);
        Height = Math.Min(maxHeight, Math.Max(MinHeight, targetHeight));
    }

    private async Task RestartCodexAppWithConfirmationAsync()
    {
        var confirm = MessageBox.Show(this,
            "Restart Codex App now?\n\nThis will close the Codex desktop app and open it again. Any unsaved Codex App work should be settled first.",
            "Restart Codex App",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        await RunWithBusyStateAsync("Restarting Codex App...", async () =>
        {
            var result = await codexAuth.RestartCodexAppAsync();
            Log(result.Output);
        });
    }

    private void ApplyTheme(bool darkMode)
    {
        ThemeToggle.Content = darkMode ? "Light mode" : "Dark mode";

        SetColor("PageBrush", darkMode ? "#0F141B" : "#F4F7FB");
        SetColor("PanelBrush", darkMode ? "#151B23" : "#FFFFFF");
        SetColor("SubtlePanelBrush", darkMode ? "#1F2937" : "#EDF3FA");
        SetColor("BorderBrushSoft", darkMode ? "#2E3A4A" : "#D9E2EF");
        SetColor("TextBrush", darkMode ? "#E5E7EB" : "#151B23");
        SetColor("MutedTextBrush", darkMode ? "#9CA3AF" : "#647083");
        SetColor("AccentBrush", darkMode ? "#60A5FA" : "#2563EB");
        SetColor("AccentHoverBrush", darkMode ? "#3B82F6" : "#1D4ED8");
        SetColor("RestartBrush", darkMode ? "#14B8A6" : "#0F766E");
        SetColor("RestartHoverBrush", darkMode ? "#2DD4BF" : "#0D9488");
        SetColor("DangerBrush", darkMode ? "#F87171" : "#DC2626");
        SetColor("GridLineBrush", darkMode ? "#263244" : "#E5EAF2");
        SetColor("LogBrush", darkMode ? "#05080D" : "#111827");
        SetColor("LogTextBrush", darkMode ? "#D1D5DB" : "#E5E7EB");
    }

    private void SetColor(string resourceKey, string hex)
    {
        Resources[resourceKey] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    }

    private static bool IsWindowsAppThemeDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static CodexAccount? GetAccountFromButton(object sender)
        => (sender as FrameworkElement)?.DataContext as CodexAccount;

    private void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }
}
