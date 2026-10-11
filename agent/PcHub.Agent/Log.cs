using System;
using System.IO;

namespace PcHub.Agent;

/// <summary>Log simple a %APPDATA%\PcHub\agent.log (se reinicia al pasar 2 MB).</summary>
internal static class Log
{
    private static readonly object Lock = new();
    private static string _path = "";

    public static string Folder => ConfigStore.Folder;

    public static void Init()
    {
        Directory.CreateDirectory(Folder);
        _path = Path.Combine(Folder, "agent.log");
        try
        {
            if (File.Exists(_path) && new FileInfo(_path).Length > 2 * 1024 * 1024)
                File.Delete(_path);
        }
        catch { /* sin log no pasa nada */ }
    }

    public static void Info(string msg) => Write("INFO", msg);

    public static void Error(string msg, Exception? ex = null) =>
        Write("ERROR", ex == null ? msg : $"{msg}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    private static void Write(string level, string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}";
        System.Diagnostics.Debug.WriteLine(line);
        if (_path.Length == 0) return;
        lock (Lock)
        {
            try { File.AppendAllText(_path, line + Environment.NewLine); }
            catch { /* ignorar */ }
        }
    }
}
