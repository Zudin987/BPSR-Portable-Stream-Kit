using System.IO;

namespace BPSRStreamKit.Infrastructure;

public static class AppPaths
{
    public static string Root => Path.GetFullPath(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
    public static string ObsDirectory => Path.Combine(Root, "OBS");
    public static string AssetsDirectory => Path.Combine(Root, "Assets");
    public static string TemplatesDirectory => Path.Combine(Root, "templates");
    public static string CacheDirectory => Path.Combine(Root, ".cache");
    public static string BackupDirectory => Path.Combine(Root, "config-backup");

    public static string? FindObsExe()
    {
        if (!Directory.Exists(ObsDirectory))
            return null;

        return Directory.EnumerateFiles(ObsDirectory, "obs64.exe", SearchOption.AllDirectories)
            .FirstOrDefault(path => path.Replace('\\', '/').EndsWith("/bin/64bit/obs64.exe", StringComparison.OrdinalIgnoreCase));
    }

    public static string? FindObsRoot()
    {
        var exe = FindObsExe();
        if (exe is null) return null;

        var x64 = Directory.GetParent(exe)?.FullName;
        var bin = x64 is null ? null : Directory.GetParent(x64)?.FullName;
        return bin is null ? null : Directory.GetParent(bin)?.FullName;
    }

    public static string? ObsConfigRoot()
    {
        var obsRoot = FindObsRoot();
        return obsRoot is null ? null : Path.Combine(obsRoot, "config", "obs-studio");
    }
}
