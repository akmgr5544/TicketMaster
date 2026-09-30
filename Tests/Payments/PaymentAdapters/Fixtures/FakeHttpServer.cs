using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Web;

namespace PaymentAdapters.Fixtures;

public sealed record RecordedRequest(string Method, string Path, string Query, IReadOnlyDictionary<string, string> Headers, string Body)
{
    public NameValueCollection Form => HttpUtility.ParseQueryString(Body);

    public NameValueCollection QueryString => HttpUtility.ParseQueryString(Query);

    public string? Header(string name) =>
        Headers.FirstOrDefault(header => string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase)).Value;
}

public sealed record FakeResponse(int Status, string Body, string ContentType, IReadOnlyDictionary<string, string>? Headers = null);

// Stands in for the provider's API, so the real SDK does its real serialisation, HTTP and parsing,
// and a test can see exactly what went over the wire.
public sealed class FakeHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();
    private volatile Func<RecordedRequest, FakeResponse> _handler =
        _ => new FakeResponse(500, "no handler configured", "text/plain");

    public FakeHttpServer()
    {
        BaseUrl = $"http://127.0.0.1:{FreePort()}";
        _listener.Prefixes.Add(BaseUrl + "/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    public string BaseUrl { get; }

    public IReadOnlyList<RecordedRequest> Requests => _requests.ToArray();

    public void Respond(Func<RecordedRequest, FakeResponse> handler) => _handler = handler;

    public void Dispose()
    {
        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        _listener.Close();
    }

    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (!_listener.IsListening)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var request = new RecordedRequest(
                context.Request.HttpMethod,
                context.Request.Url!.AbsolutePath,
                context.Request.Url.Query,
                context.Request.Headers.AllKeys.Where(key => key is not null)
                    .ToDictionary(key => key!, key => context.Request.Headers[key]!),
                await reader.ReadToEndAsync());
            _requests.Enqueue(request);

            FakeResponse response;
            try
            {
                response = _handler(request);
            }
            catch (Exception exception)
            {
                response = new FakeResponse(599, exception.ToString(), "text/plain");
            }

            context.Response.StatusCode = response.Status;
            context.Response.ContentType = response.ContentType;
            foreach (var (name, value) in response.Headers ?? new Dictionary<string, string>())
            {
                context.Response.Headers[name] = value;
            }

            var bytes = Encoding.UTF8.GetBytes(response.Body);
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            context.Response.Close();
        }
    }

    private static int FreePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }
}
