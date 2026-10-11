using System;
using System.IO;

namespace PcHub.Agent;

/// <summary>
/// Versión del agente que está corriendo: el commit con que se compiló (archivo version.txt junto al .exe).
/// Si se ejecuta con "dotnet run" no existe y queda en null.
/// </summary>
public static class AgentVersion
{
    public static string? Current { get; } = Read(Path.Combine(AppContext.BaseDirectory, "version.txt"));

    public static string? Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}
