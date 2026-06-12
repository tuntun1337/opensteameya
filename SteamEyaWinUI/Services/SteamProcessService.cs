using System.Diagnostics;
using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Services;

internal sealed class SteamProcessService
{
    public void EnsureSteamStopped(SteamPaths paths, IProgress<string>? progress)
    {
        if (!IsSteamRunning())
        {
            return;
        }

        progress?.Report("Steam 正在运行，正在关闭...");

        var steamExe = Path.Combine(paths.InstallPath, "steam.exe");
        if (File.Exists(steamExe))
        {
            using var shutdown = Process.Start(new ProcessStartInfo
            {
                FileName = steamExe,
                UseShellExecute = false
            }.WithArguments("-shutdown"));

            shutdown?.WaitForExit(3000);
        }

        for (var i = 0; i < 10; i++)
        {
            if (!IsSteamRunning())
            {
                return;
            }

            Thread.Sleep(1000);
        }

        progress?.Report("Steam 未及时退出，正在结束相关进程...");
        KillProcesses("steam");
        KillProcesses("steamwebhelper");
    }

    public void LaunchSteamWithLogin(SteamPaths paths, string accountName)
    {
        var steamExe = Path.Combine(paths.InstallPath, "steam.exe");
        if (!File.Exists(steamExe))
        {
            throw new InvalidOperationException($"没有找到 Steam 可执行文件：{steamExe}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = steamExe,
            WorkingDirectory = paths.InstallPath,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-login");
        startInfo.ArgumentList.Add(accountName);
        Process.Start(startInfo);
    }

    private static bool IsSteamRunning()
    {
        return Process.GetProcessesByName("steam").Length > 0 ||
            Process.GetProcessesByName("steamwebhelper").Length > 0;
    }

    private static void KillProcesses(string processName)
    {
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                try
                {
                    process.Kill(true);
                    process.WaitForExit(3000);
                }
                catch
                {
                    // Best effort. The caller will surface launch/write failures if Steam keeps files locked.
                }
            }
        }
    }
}

internal static class ProcessStartInfoExtensions
{
    public static ProcessStartInfo WithArguments(this ProcessStartInfo startInfo, params string[] arguments)
    {
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}
