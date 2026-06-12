using System.Diagnostics;
using Microsoft.Win32;
using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Services;

internal sealed class SteamPathService
{
    public SteamPaths GetSteamPaths()
    {
        var installPath =
            GetInstallPathFromRegistry() ??
            GetInstallPathFromRunningProcess() ??
            GetInstallPathFromProtocolRegistry();

        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
        {
            throw new InvalidOperationException("没有找到 Steam 安装目录。");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            throw new InvalidOperationException("没有找到 LocalAppData 目录。");
        }

        return new SteamPaths(
            installPath,
            Path.Combine(localAppData, "Steam", "local.vdf"),
            Path.Combine(installPath, "config"));
    }

    private static string? GetInstallPathFromRegistry()
    {
        return ReadRegistryString(
                RegistryHive.LocalMachine,
                RegistryView.Registry64,
                @"SOFTWARE\WOW6432Node\Valve\Steam",
                "InstallPath") ??
            ReadRegistryString(
                RegistryHive.LocalMachine,
                RegistryView.Registry32,
                @"SOFTWARE\Valve\Steam",
                "InstallPath");
    }

    private static string? GetInstallPathFromRunningProcess()
    {
        foreach (var processName in new[] { "steam", "steamwebhelper" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        var fileName = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(fileName))
                        {
                            return Path.GetDirectoryName(fileName);
                        }
                    }
                    catch
                    {
                        // Some processes deny MainModule access; registry lookup usually covers this.
                    }
                }
            }
        }

        return null;
    }

    private static string? GetInstallPathFromProtocolRegistry()
    {
        var command = ReadRegistryString(
            RegistryHive.CurrentUser,
            RegistryView.Default,
            @"Software\Classes\steam\Shell\Open\Command",
            "");

        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var exePath = ExtractExecutablePath(command);
        return string.IsNullOrWhiteSpace(exePath) ? null : Path.GetDirectoryName(exePath);
    }

    private static string? ReadRegistryString(
        RegistryHive hive,
        RegistryView view,
        string keyPath,
        string valueName)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(keyPath);
            return key?.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractExecutablePath(string command)
    {
        command = command.Trim();

        if (command.StartsWith('"'))
        {
            var endQuote = command.IndexOf('"', 1);
            return endQuote > 1 ? command[1..endQuote] : null;
        }

        var exeIndex = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exeIndex >= 0 ? command[..(exeIndex + 4)] : null;
    }
}
