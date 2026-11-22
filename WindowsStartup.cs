using Microsoft.Win32;

namespace Kontur;

internal sealed class WindowsStartup(string keyPath = WindowsStartup.RunKey)
{
    internal const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string EntryName = "KonturSK0127";
    internal const string LaunchArgument = "--startup";

    public void UpgradeLegacyCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        if (key?.GetValue(EntryName) is string command &&
            string.Equals(command.Trim(), $"\"{Application.ExecutablePath}\"", StringComparison.OrdinalIgnoreCase))
            SetEnabled(true);
    }

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(EntryName) is string command && !string.IsNullOrWhiteSpace(command);
    }

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using var existingKey = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            existingKey?.DeleteValue(EntryName, throwOnMissingValue: false);
            return;
        }

        var command = $"\"{Application.ExecutablePath}\" {LaunchArgument}";
        if (command.Length > 260)
            throw new IOException("Путь к «Контуру» слишком длинный для автозагрузки Windows.");

        using var key = Registry.CurrentUser.CreateSubKey(keyPath)
            ?? throw new IOException("Не удалось открыть параметры автозагрузки Windows.");
        key.SetValue(EntryName, command, RegistryValueKind.String);
    }
}
