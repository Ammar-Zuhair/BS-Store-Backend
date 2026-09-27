namespace BSStore.Infrastructure.Common;

/// <summary>
/// Lightweight loader for .env files into Environment variables for development and production.
/// </summary>
public static class EnvLoader
{
    public static void Load(string? startDirectory = null)
    {
        var currentDir = new DirectoryInfo(startDirectory ?? Directory.GetCurrentDirectory());
        FileInfo? envFile = null;

        // Traverse upwards to find .env file
        var dir = currentDir;
        while (dir != null)
        {
            var testPath = Path.Combine(dir.FullName, ".env");
            if (File.Exists(testPath))
            {
                envFile = new FileInfo(testPath);
                break;
            }

            var backendEnv = Path.Combine(dir.FullName, "backend", ".env");
            if (File.Exists(backendEnv))
            {
                envFile = new FileInfo(backendEnv);
                break;
            }

            dir = dir.Parent;
        }

        if (envFile == null || !envFile.Exists)
            return;

        try
        {
            foreach (var line in File.ReadAllLines(envFile.FullName))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                    continue;

                var equalsIdx = trimmed.IndexOf('=');
                if (equalsIdx <= 0) continue;

                var key = trimmed[..equalsIdx].Trim();
                var val = trimmed[(equalsIdx + 1)..].Trim();

                if (val.Length >= 2 && ((val.StartsWith('"') && val.EndsWith('"')) || (val.StartsWith('\'') && val.EndsWith('\''))))
                {
                    val = val[1..^1];
                }

                // Set in process environment
                Environment.SetEnvironmentVariable(key, val);
            }
        }
        catch
        {
            // Ignore file read errors to avoid startup crashes
        }
    }
}
