using System.IO.Pipes;
using System.Net.Sockets;

namespace HearthGate.Agent;

// Owns: the HTTP transport to the Docker API. Supports an HTTP URL (socket proxy, Linux),
// npipe:////./pipe/docker_engine (Docker Desktop on Windows) and unix:///var/run/docker.sock.
public static class DockerHttp
{
    public static string BaseUrl(string endpoint) =>
        IsPipe(endpoint) || IsUnix(endpoint) ? "http://docker" : endpoint.TrimEnd('/');

    public static SocketsHttpHandler Handler(string? endpoint)
    {
        var handler = new SocketsHttpHandler();
        if (endpoint is null) return handler;
        if (IsPipe(endpoint))
        {
            var pipe = endpoint["npipe://".Length..].TrimStart('/').Replace('\\', '/');
            var name = pipe[(pipe.LastIndexOf('/') + 1)..]; // .//pipe/docker_engine -> docker_engine
            handler.ConnectCallback = async (_, ct) =>
            {
                var stream = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                await stream.ConnectAsync(5000, ct);
                return stream;
            };
        }
        else if (IsUnix(endpoint))
        {
            var path = endpoint["unix://".Length..];
            handler.ConnectCallback = async (_, ct) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), ct);
                return new NetworkStream(socket, ownsSocket: true);
            };
        }
        return handler;
    }

    private static bool IsPipe(string e) => e.StartsWith("npipe://", StringComparison.OrdinalIgnoreCase);
    private static bool IsUnix(string e) => e.StartsWith("unix://", StringComparison.OrdinalIgnoreCase);
}
