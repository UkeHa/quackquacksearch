using System.Text;

namespace QuackQuackSearch.Core.System;

public static class MountScanner
{
    private static readonly HashSet<string> IgnoredFsTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "sysfs", "proc", "devtmpfs", "devpts", "securityfs", "cgroup", "cgroup2",
        "pstore", "bpf", "configfs", "autofs", "mqueue", "hugetlbfs", "debugfs",
        "tracefs", "fusectl", "ramfs", "overlay", "squashfs", "tmpfs", "efivarfs",
        "fuse.portal", "rpc_pipefs", "binfmt_misc"
    };

    private static readonly HashSet<string> LocalFsTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ext4", "ext3", "ext2", "btrfs", "xfs", "f2fs", "vfat", "exfat", "ntfs", "ntfs3"
    };

    private static readonly HashSet<string> NetworkFsTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "nfs", "nfs4", "cifs", "smbfs", "fuse.sshfs", "fuse.davfs", "davfs"
    };

    /// <summary>
    /// Scans available mounts from /proc/mounts and GVFS directories.
    /// </summary>
    public static IReadOnlyList<MountCandidate> ScanMounts(string procMountsPath = "/proc/mounts")
    {
        var candidates = new List<MountCandidate>();

        if (File.Exists(procMountsPath))
        {
            var lines = File.ReadAllLines(procMountsPath);
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3) continue;

                string device = UnescapeOctal(parts[0]);
                string mountPoint = UnescapeOctal(parts[1]);
                string fsType = parts[2];

                if (IgnoredFsTypes.Contains(fsType)) continue;

                // Ignore system mount points that shouldn't normally be indexed by user search
                if (mountPoint.StartsWith("/sys") || mountPoint.StartsWith("/proc") ||
                    mountPoint.StartsWith("/dev") || mountPoint.StartsWith("/var/log") ||
                    mountPoint.StartsWith("/var/cache") || mountPoint.StartsWith("/var/tmp") ||
                    mountPoint == "/boot" || mountPoint.StartsWith("/boot/"))
                {
                    continue;
                }

                MountType type;
                string suggestedMode;

                if (fsType.Equals("fuse.gvfsd-fuse", StringComparison.OrdinalIgnoreCase) ||
                    mountPoint.Contains("/gvfs"))
                {
                    type = MountType.GVFS;
                    suggestedMode = "network";
                }
                else if (NetworkFsTypes.Contains(fsType) || device.StartsWith("//") || device.Contains(":/"))
                {
                    type = MountType.Network;
                    suggestedMode = "network";
                }
                else if (LocalFsTypes.Contains(fsType))
                {
                    type = MountType.Local;
                    suggestedMode = "local";
                }
                else
                {
                    type = MountType.Other;
                    suggestedMode = "local";
                }

                bool isUserAccessible = mountPoint.StartsWith("/home") ||
                                       mountPoint.StartsWith("/media") ||
                                       mountPoint.StartsWith("/mnt") ||
                                       mountPoint.StartsWith("/run/media") ||
                                       mountPoint == "/";

                candidates.Add(new MountCandidate(
                    device,
                    mountPoint,
                    fsType,
                    type,
                    suggestedMode,
                    isUserAccessible
                ));
            }
        }

        // Also inspect user's GVFS directory directly for submounts
        ScanGvfsUserMounts(candidates);

        return candidates;
    }

    private static void ScanGvfsUserMounts(List<MountCandidate> candidates)
    {
        int uid = Environment.UserName == "root" ? 0 : 1000;
        string? xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        string gvfsBase = !string.IsNullOrEmpty(xdgRuntime) 
            ? Path.Combine(xdgRuntime, "gvfs") 
            : $"/run/user/{uid}/gvfs";

        if (Directory.Exists(gvfsBase))
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(gvfsBase))
                {
                    string name = Path.GetFileName(dir);
                    if (!candidates.Any(c => c.MountPoint == dir))
                    {
                        candidates.Add(new MountCandidate(
                            $"gvfs:{name}",
                            dir,
                            "gvfs",
                            MountType.GVFS,
                            "network",
                            IsRemovableOrUserAccessible: true
                        ));
                    }
                }
            }
            catch (Exception)
            {
                // Inaccessible GVFS directories are ignored
            }
        }
    }

    /// <summary>
    /// Decodes octal escape sequences used by kernel mount format (e.g. \040 for space).
    /// </summary>
    private static string UnescapeOctal(string input)
    {
        if (!input.Contains('\\')) return input;

        var sb = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '\\' && i + 3 < input.Length &&
                char.IsBetween(input[i + 1], '0', '7') &&
                char.IsBetween(input[i + 2], '0', '7') &&
                char.IsBetween(input[i + 3], '0', '7'))
            {
                int octalVal = ((input[i + 1] - '0') << 6) | ((input[i + 2] - '0') << 3) | (input[i + 3] - '0');
                sb.Append((char)octalVal);
                i += 3;
            }
            else
            {
                sb.Append(input[i]);
            }
        }
        return sb.ToString();
    }
}
