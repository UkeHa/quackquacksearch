using System.Diagnostics;

namespace QuackQuackSearch.Core.System;

/// <summary>
/// Provides shared desktop integration helpers for opening files, revealing items in file managers,
/// and copying text to the Wayland/X11 system clipboard.
/// </summary>
public static class DesktopActions
{
    public static bool TryOpenFile(string fullPath, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(fullPath)) return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "xdg-open",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(fullPath);
            Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public static bool TryOpenContainingFolder(string fullPath, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(fullPath)) return false;

        string normalizedFull = Path.GetFullPath(fullPath);
        string? parentDir = Path.GetDirectoryName(normalizedFull);
        if (string.IsNullOrEmpty(parentDir)) parentDir = "/";

        // 1. Prefer standard freedesktop FileManager1 D-Bus interface (works across Dolphin, Nautilus, Nemo, Thunar, PCManFM)
        try
        {
            string fileUri = new Uri(normalizedFull).AbsoluteUri;
            var dbusPsi = new ProcessStartInfo
            {
                FileName = "dbus-send",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            dbusPsi.ArgumentList.Add("--session");
            dbusPsi.ArgumentList.Add("--print-reply");
            dbusPsi.ArgumentList.Add("--dest=org.freedesktop.FileManager1");
            dbusPsi.ArgumentList.Add("/org/freedesktop/FileManager1");
            dbusPsi.ArgumentList.Add("org.freedesktop.FileManager1.ShowItems");
            dbusPsi.ArgumentList.Add($"array:string:{fileUri}");
            dbusPsi.ArgumentList.Add("string:");

            using var proc = Process.Start(dbusPsi);
            if (proc != null && proc.WaitForExit(1500) && proc.ExitCode == 0)
            {
                return true;
            }
        }
        catch
        {
            // Fallback if dbus-send or org.freedesktop.FileManager1 is unavailable
        }

        // 2. Fallback: open parent directory with default handler via xdg-open
        try
        {
            var fallbackPsi = new ProcessStartInfo
            {
                FileName = "xdg-open",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            fallbackPsi.ArgumentList.Add(parentDir);
            Process.Start(fallbackPsi);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    public static bool TryCopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "wl-copy",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add(text);
            using var p = Process.Start(psi);
            p?.WaitForExit(500);
            if (p != null && p.ExitCode == 0)
            {
                return true;
            }
        }
        catch
        {
            // Fallback to X11 clipboard utilities
        }

        try
        {
            var xclipPsi = new ProcessStartInfo
            {
                FileName = "xclip",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true
            };
            xclipPsi.ArgumentList.Add("-selection");
            xclipPsi.ArgumentList.Add("clipboard");
            xclipPsi.ArgumentList.Add("-i");
            using var p = Process.Start(xclipPsi);
            if (p != null)
            {
                p.StandardInput.Write(text);
                p.StandardInput.Close();
                p.WaitForExit(500);
                return true;
            }
        }
        catch
        {
            // Ignore if xclip is also unavailable
        }

        return false;
    }
}
