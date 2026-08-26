using System.Diagnostics;

namespace CodexCompanion.Bridge.Configuration;

public sealed class CodexExecutableResolver
{
    public string Resolve()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_EXECUTABLE");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return Path.GetFullPath(configured);
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(directory, "codex.exe");
            if (File.Exists(candidate) && !candidate.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        var npmCache = ReadNpmCachePath();
        if (!string.IsNullOrWhiteSpace(npmCache))
        {
            var npxRoot = Path.Combine(npmCache, "_npx");
            if (Directory.Exists(npxRoot))
            {
                var native = Directory.EnumerateFiles(npxRoot, "codex.exe", SearchOption.AllDirectories)
                    .Where(path => path.Contains("@openai", StringComparison.OrdinalIgnoreCase)
                                   && path.Contains("codex-win32-x64", StringComparison.OrdinalIgnoreCase)
                                   && path.EndsWith(Path.Combine("bin", "codex.exe"), StringComparison.OrdinalIgnoreCase))
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .FirstOrDefault();
                if (native is not null)
                {
                    return native.FullName;
                }
            }
        }

        throw new FileNotFoundException(
            "找不到可启动的 Codex CLI。MSIX 包内的受保护二进制不能由 Bridge 直接启动；请安装 @openai/codex 或设置 CODEX_EXECUTABLE。");
    }

    private static string? ReadNpmCachePath()
    {
        var npm = FindOnPath("npm.cmd");
        if (npm is null)
        {
            return null;
        }

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = npm,
                Arguments = "config get cache",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5_000);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? FindOnPath(string fileName)
    {
        return (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(directory => Path.Combine(directory, fileName))
            .FirstOrDefault(File.Exists);
    }
}
