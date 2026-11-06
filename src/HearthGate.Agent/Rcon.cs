using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;

namespace HearthGate.Agent;

// Owns: one Source RCON exchange (connect, authenticate, run one command, read the answer).
// Used by Minecraft, ARK, Factorio, Source-engine games and others. One TCP connection per command.
public sealed class RconAuthException() : Exception("RCON authentication failed.");

public static class Rcon
{
    private const int Auth = 3, ExecCommand = 2, AuthResponse = 2;

    public static async Task<string> Send(string address, string password, string command, TimeSpan timeout, CancellationToken ct)
    {
        var colon = address.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(address[(colon + 1)..], out var port)) throw new InvalidOperationException("The rcon address must be host:port.");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(address[..colon].Trim('[', ']'), port, cts.Token);
        var stream = tcp.GetStream();

        await Write(stream, 1, Auth, password, cts.Token);
        while (true)
        {
            // Some servers send an empty response packet before the auth response.
            var p = await Read(stream, cts.Token);
            if (p.Type != AuthResponse) continue;
            if (p.Id == -1) throw new RconAuthException();
            break;
        }

        await Write(stream, 2, ExecCommand, command, cts.Token);
        var output = new StringBuilder((await Read(stream, cts.Token)).Body);
        // Long answers come in more packets; collect them until the server is quiet.
        while (output.Length < 65536)
        {
            using var quiet = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
            quiet.CancelAfter(300);
            try { output.Append((await Read(stream, quiet.Token)).Body); }
            catch (OperationCanceledException) when (!cts.IsCancellationRequested) { break; }
            catch (EndOfStreamException) { break; }
        }
        return output.ToString();
    }

    private static async Task Write(NetworkStream s, int id, int type, string body, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var packet = new byte[14 + bytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(packet, 10 + bytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(4), id);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(8), type);
        bytes.CopyTo(packet, 12);
        await s.WriteAsync(packet, ct);
    }

    private static async Task<(int Id, int Type, string Body)> Read(NetworkStream s, CancellationToken ct)
    {
        var head = new byte[4];
        await s.ReadExactlyAsync(head, ct);
        var size = BinaryPrimitives.ReadInt32LittleEndian(head);
        if (size < 10 || size > 1 << 20) throw new InvalidDataException("Bad RCON packet size.");
        var data = new byte[size];
        await s.ReadExactlyAsync(data, ct);
        var end = Array.IndexOf(data, (byte)0, 8);
        var body = Encoding.UTF8.GetString(data, 8, (end < 0 ? size : end) - 8);
        return (BinaryPrimitives.ReadInt32LittleEndian(data), BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)), body);
    }
}
