using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AnythinkMcp.Tests;

internal sealed class LoopbackTrap : IDisposable
{
    private static readonly byte[] Refusal = Encoding.ASCII.GetBytes("HTTP/1.1 500 Trap\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private int _connections;

    public LoopbackTrap()
    {
        _listener.Start();
        _ = Task.Run(AcceptAsync);
    }

    public string Url => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    public int Connections => Volatile.Read(ref _connections);

    public async Task<bool> WaitForAConnectionAsync(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Connections == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        return Connections > 0;
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                Interlocked.Increment(ref _connections);
                await client.GetStream().WriteAsync(Refusal, _stop.Token);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException or IOException)
        {
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}
