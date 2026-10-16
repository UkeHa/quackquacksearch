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

        string? parentDir = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(parentDir)) parentDir = "/";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dolphin",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--select");
            psi.ArgumentList.Add(fullPath);
            Process.Start(psi);
            return true;
        }
        catch
        {
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
