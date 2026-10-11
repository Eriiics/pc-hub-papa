using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using PcHub.Agent.Modules;

namespace PcHub.Agent.Server;

/// <summary>
/// Canal rápido para el mouse (touchpad y puntero): paquetes UDP pequeños en vez de JSON por WebSocket.
/// UDP no espera a que lleguen los paquetes anteriores, así que un paquete perdido en el Wi-Fi no frena
/// el movimiento (con TCP sí, y se nota como tirones).
///
/// Formato (little-endian):
///   [0..7]  clave de 8 bytes que entrega "input.udp" a un teléfono ya autenticado
///   [8]     tipo: 1 = mover (int16 dx, int16 dy) · 2 = scroll (int16 dy, int16 dx)
///                 3 = absoluto (uint16 x, uint16 y; 0..65535 sobre la pantalla principal) · 4 = ping
///   [9..10] número de secuencia (uint16), para descartar posiciones absolutas que llegan desordenadas
///   [11..]  datos
/// </summary>
public sealed class UdpInput : IDisposable
{
    private sealed class Client
    {
        public required Session Session { get; init; }
        public ushort LastAbsSeq;
        public bool HasAbs;
    }

    private readonly ConcurrentDictionary<ulong, Client> _clients = new();
    private readonly CancellationTokenSource _cts = new();
    private UdpClient? _udp;

    public int Port { get; private set; }
    public bool Running => _udp != null;

    public void Start(int port)
    {
        try
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            Port = port;
        }
        catch (Exception ex)
        {
            Log.Error($"No se pudo abrir el puerto UDP {port} para el mouse (se usará el WebSocket)", ex);
            _udp = null;
            return;
        }
        _ = Task.Run(LoopAsync);
        Log.Info($"Canal rápido de mouse (UDP) en el puerto {port}");
    }

    /// <summary>Clave para un teléfono autenticado. Se invalida al desconectarse.</summary>
    public ulong Register(Session s)
    {
        Unregister(s);
        var key = BinaryPrimitives.ReadUInt64LittleEndian(RandomNumberGenerator.GetBytes(8));
        _clients[key] = new Client { Session = s };
        s.UdpKey = key;
        return key;
    }

    public void Unregister(Session s)
    {
        if (s.UdpKey is { } key) _clients.TryRemove(key, out _);
        s.UdpKey = null;
    }

    private async Task LoopAsync()
    {
        var udp = _udp!;
        while (!_cts.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try
            {
                r = await udp.ReceiveAsync(_cts.Token);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException) { continue; } // p. ej. "conexión restablecida" por un ICMP anterior
            try
            {
                Handle(r.Buffer);
            }
            catch (Exception ex)
            {
                Log.Error("UDP mouse", ex);
            }
        }
    }

    private void Handle(byte[] b)
    {
        if (b.Length < 11) return;
        var key = BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(0, 8));
        if (!_clients.TryGetValue(key, out var c) || !c.Session.Authenticated) return;
        byte type = b[8];
        ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(9, 2));

        switch (type)
        {
            case 1 when b.Length >= 15:
                InputModule.MouseMove(
                    BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(11, 2)),
                    BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(13, 2)));
                break;
            case 2 when b.Length >= 15:
                InputModule.MouseScroll(
                    BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(11, 2)),
                    BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(13, 2)));
                break;
            case 3 when b.Length >= 15:
                // Descartar una posición más vieja que la última aplicada (diferencia con signo, admite vuelta del contador)
                if (c.HasAbs && (short)(seq - c.LastAbsSeq) <= 0) return;
                c.HasAbs = true;
                c.LastAbsSeq = seq;
                InputModule.MouseAbsolute(
                    BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(11, 2)),
                    BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(13, 2)));
                break;
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _udp?.Dispose();
    }
}
