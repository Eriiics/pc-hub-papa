using System;
using System.Diagnostics;
using System.Text.Json.Nodes;
using NAudio.CoreAudioApi;

namespace PcHub.Agent.Modules;

/// <summary>Volumen general y por aplicación usando Core Audio (NAudio).</summary>
public static class AudioModule
{
    public static JsonObject Get()
    {
        using var en = new MMDeviceEnumerator();
        using var dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return new JsonObject
        {
            ["level"] = Math.Round(dev.AudioEndpointVolume.MasterVolumeLevelScalar, 3),
            ["muted"] = dev.AudioEndpointVolume.Mute,
            ["device"] = dev.FriendlyName,
        };
    }

    public static void SetLevel(double level)
    {
        using var en = new MMDeviceEnumerator();
        using var dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        dev.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(level, 0, 1);
        if (level > 0 && dev.AudioEndpointVolume.Mute) dev.AudioEndpointVolume.Mute = false;
    }

    public static void SetMute(bool muted)
    {
        using var en = new MMDeviceEnumerator();
        using var dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        dev.AudioEndpointVolume.Mute = muted;
    }

    public static JsonObject ListApps()
    {
        var arr = new JsonArray();
        using var en = new MMDeviceEnumerator();
        using var dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessions = dev.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            try
            {
                uint pid = s.GetProcessID;
                string name;
                if (pid == 0) name = "Sonidos del sistema";
                else
                {
                    try
                    {
                        using var p = Process.GetProcessById((int)pid);
                        name = p.ProcessName;
                    }
                    catch { continue; } // proceso que ya terminó
                }
                arr.Add(new JsonObject
                {
                    ["pid"] = pid,
                    ["name"] = name,
                    ["level"] = Math.Round(s.SimpleAudioVolume.Volume, 3),
                    ["muted"] = s.SimpleAudioVolume.Mute,
                });
            }
            catch (Exception ex)
            {
                Log.Error("Sesión de audio", ex);
            }
        }
        return new JsonObject { ["apps"] = arr };
    }

    public static void SetApp(uint pid, double? level, bool? muted)
    {
        using var en = new MMDeviceEnumerator();
        using var dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var sessions = dev.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (s.GetProcessID != pid) continue;
            if (level.HasValue) s.SimpleAudioVolume.Volume = (float)Math.Clamp(level.Value, 0, 1);
            if (muted.HasValue) s.SimpleAudioVolume.Mute = muted.Value;
        }
    }
}
