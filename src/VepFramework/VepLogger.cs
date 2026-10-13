using BepInEx.Logging;
using Logger = BepInEx.Logging.Logger;

namespace VepMod.VepFramework;

public sealed class VepLogger
{
    private const string Prefix = "VepMod";

    // A dev build writes every Debug line, so an in-game test can be checked from the log afterwards.
#if DEBUG
    private const bool DebugBuild = true;
#else
    private const bool DebugBuild = false;
#endif

    private readonly ManualLogSource _log;

    private VepLogger(string className, bool debugEnabled = false)
    {
        _log = Logger.CreateLogSource($"{Prefix}.{className}");
        DebugEnabled = debugEnabled || DebugBuild;
    }

    public bool DebugEnabled { get; set; }

    public static VepLogger Create<T>(bool debugEnabled = false)
    {
        return new VepLogger(typeof(T).Name, debugEnabled);
    }

    public static VepLogger Create(string className, bool debugEnabled = false)
    {
        return new VepLogger(className, debugEnabled);
    }

    public void Debug(string message)
    {
        if (DebugEnabled)
        {
            _log.LogDebug(message);
        }
    }

    public void Info(string message)
    {
        _log.LogInfo(message);
    }

    public void Warning(string message)
    {
        _log.LogWarning(message);
    }

    public void Error(string message)
    {
        _log.LogError(message);
    }
}
