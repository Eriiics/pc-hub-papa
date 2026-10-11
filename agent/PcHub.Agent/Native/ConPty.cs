using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PcHub.Agent.Native;

/// <summary>
/// Pseudoconsola de Windows (ConPTY, Windows 10 1809+): corre un programa de consola sin ventana y entrega su
/// salida como texto con secuencias VT (colores, cursor). Es lo mismo que usa Windows Terminal por dentro.
/// </summary>
public sealed class ConPty : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct COORD { public short X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const int STARTF_USESTDHANDLES = 0x00000100;
    private static readonly IntPtr PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = (IntPtr)0x00020016;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(COORD size, SafeFileHandle hInput, SafeFileHandle hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle hReadPipe, out SafeFileHandle hWritePipe, IntPtr lpPipeAttributes, int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref IntPtr lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue,
        IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(string? lpApplicationName, StringBuilder lpCommandLine, IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);

    private IntPtr _hPc;
    private IntPtr _attrList;
    private PROCESS_INFORMATION _pi;
    private readonly FileStream _input;
    private bool _disposed;

    /// <summary>Lo que el programa escribe (texto UTF-8 con secuencias VT). Se lee hasta que devuelve 0.</summary>
    public FileStream Output { get; }
    public Process Process { get; }

    public ConPty(string commandLine, string? workDir, int cols, int rows)
    {
        if (!CreatePipe(out var inputRead, out var inputWrite, IntPtr.Zero, 0)) throw new Win32Exception();
        if (!CreatePipe(out var outputRead, out var outputWrite, IntPtr.Zero, 0)) throw new Win32Exception();

        var hr = CreatePseudoConsole(Size(cols, rows), inputRead, outputWrite, 0, out _hPc);
        if (hr != 0) throw new Win32Exception(hr, "No se pudo crear la pseudoconsola (requiere Windows 10 1809 o superior)");

        // La pseudoconsola ya tiene su copia de estos extremos
        inputRead.Dispose();
        outputWrite.Dispose();

        var size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        _attrList = Marshal.AllocHGlobal(size);
        if (!InitializeProcThreadAttributeList(_attrList, 1, 0, ref size)) throw new Win32Exception();
        if (!UpdateProcThreadAttribute(_attrList, 0, PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE, _hPc, (IntPtr)IntPtr.Size, IntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception();

        var si = new STARTUPINFOEX();
        si.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
        // Sin esto, el proceso podría heredar los identificadores estándar del agente en vez de la pseudoconsola
        si.StartupInfo.dwFlags = STARTF_USESTDHANDLES;
        si.lpAttributeList = _attrList;

        if (!CreateProcessW(null, new StringBuilder(commandLine), IntPtr.Zero, IntPtr.Zero, false,
                EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT, IntPtr.Zero, workDir, ref si, out _pi))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo iniciar " + commandLine);

        Process = Process.GetProcessById(_pi.dwProcessId);
        _input = new FileStream(inputWrite, FileAccess.Write, 1);
        Output = new FileStream(outputRead, FileAccess.Read, 1);
    }

    private static COORD Size(int cols, int rows) => new()
    {
        X = (short)Math.Clamp(cols, 10, 500),
        Y = (short)Math.Clamp(rows, 4, 300),
    };

    public void Write(byte[] data)
    {
        if (_disposed) return;
        _input.Write(data, 0, data.Length);
        _input.Flush();
    }

    public void Resize(int cols, int rows)
    {
        if (!_disposed && _hPc != IntPtr.Zero) ResizePseudoConsole(_hPc, Size(cols, rows));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (!Process.HasExited) Process.Kill(true); } catch { /* ya terminó */ }
        try { _input.Dispose(); } catch { }
        // ClosePseudoConsole puede esperar a que se lea lo último: el lector sigue drenando Output en su hilo
        var hpc = _hPc;
        _hPc = IntPtr.Zero;
        if (hpc != IntPtr.Zero) System.Threading.Tasks.Task.Run(() => ClosePseudoConsole(hpc));
        if (_attrList != IntPtr.Zero)
        {
            DeleteProcThreadAttributeList(_attrList);
            Marshal.FreeHGlobal(_attrList);
            _attrList = IntPtr.Zero;
        }
        if (_pi.hProcess != IntPtr.Zero) CloseHandle(_pi.hProcess);
        if (_pi.hThread != IntPtr.Zero) CloseHandle(_pi.hThread);
    }
}
