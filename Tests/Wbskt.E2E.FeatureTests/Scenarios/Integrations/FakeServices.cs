using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Wbskt.E2E.FeatureTests.Fixtures;

namespace Wbskt.E2E.FeatureTests.Scenarios.Integrations;

/// <summary>
/// A local HTTP service standing in for Telegram, Discord, ntfy or any API an integration points at.
/// It records every request and answers with <see cref="Respond"/> (200 with an empty JSON object
/// unless a scenario says otherwise). The engine reaches it because the E2E settings allow loopback
/// targets and plain http.
/// </summary>
internal sealed class FakeHttpServer : IAsyncDisposable
{
    /// <summary>The fixed port the engine's E2E settings point Telegram at (WorkflowEngine:Integrations:TelegramApiBaseUrl).</summary>
    public const int TelegramPort = 5098;

    public sealed record Request(string Method, string Path, string Query, IReadOnlyDictionary<string, string> Headers, string Body)
    {
        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }

    private readonly HttpListener _listener = new();
    private readonly ConcurrentQueue<Request> _requests = new();
    private readonly Task _loop;

    private FakeHttpServer(int port)
    {
        BaseUrl = $"http://127.0.0.1:{port}";
        _listener.Prefixes.Add(BaseUrl + "/");
        _listener.Start();
        _loop = Task.Run(LoopAsync);
    }

    /// <summary>http://127.0.0.1:{port}, no trailing slash.</summary>
    public string BaseUrl { get; }

    public Func<Request, (int Status, string Body)> Respond { get; set; } = _ => (200, "{}");

    public IReadOnlyList<Request> Requests => _requests.ToArray();

    public static FakeHttpServer Start(int? port = null) => new(port ?? FreePort());

    /// <summary>The Telegram Bot API on its fixed port: getMe names the bot, sendMessage succeeds.</summary>
    public static FakeHttpServer StartTelegram()
    {
        var server = new FakeHttpServer(TelegramPort);
        server.Respond = request => request.Path.EndsWith("/getMe", StringComparison.Ordinal)
            ? (200, """{"ok":true,"result":{"id":42,"is_bot":true,"first_name":"Garage Bot","username":"garage_bot"}}""")
            : (200, """{"ok":true,"result":{"message_id":1}}""");
        return server;
    }

    /// <summary>Waits for a request matching <paramref name="match"/>, returning it, or null on timeout.</summary>
    public async Task<Request?> WaitForAsync(Func<Request, bool> match, TimeSpan? timeout = null)
    {
        Request? found = null;
        await ServicesFixture.PollAsync(
            () =>
            {
                found = _requests.FirstOrDefault(match);
                return Task.FromResult(found is not null);
            },
            timeout ?? TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(250));
        return found;
    }

    public static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task LoopAsync()
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

            try
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                string body = await reader.ReadToEndAsync();
                var headers = context.Request.Headers.AllKeys
                    .Where(k => k is not null)
                    .ToDictionary(k => k!, k => context.Request.Headers[k] ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                var request = new Request(
                    context.Request.HttpMethod,
                    context.Request.Url!.AbsolutePath,
                    context.Request.Url.Query,
                    headers,
                    body);
                _requests.Enqueue(request);

                var (status, responseBody) = Respond(request);
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json";
                byte[] bytes = Encoding.UTF8.GetBytes(responseBody);
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
            catch (Exception)
            {
                // A client that hung up mid-request is the engine's business, not the fake's.
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
        }

        await _loop;
    }
}

/// <summary>
/// A plaintext SMTP relay, enough of RFC 5321 for MailKit: EHLO, AUTH PLAIN/LOGIN, MAIL, RCPT, DATA,
/// RSET, NOOP, QUIT. It records each accepted message, checks the credential when one is expected,
/// and refuses the recipients a scenario names, so the relay's refusals can be exercised too.
/// </summary>
internal sealed class FakeSmtpServer : IAsyncDisposable
{
    public sealed record Message(string From, IReadOnlyList<string> To, string Data, string? UserName, string? Password);

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly ConcurrentQueue<Message> _messages = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    private FakeSmtpServer(string? userName, string? password)
    {
        ExpectedUserName = userName;
        ExpectedPassword = password;
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(LoopAsync);
    }

    public int Port { get; }

    public string? ExpectedUserName { get; }

    public string? ExpectedPassword { get; }

    /// <summary>Recipients answered with 550.</summary>
    public HashSet<string> RefusedRecipients { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many sessions connected, including ones that only signed in (a test without a recipient).</summary>
    public int Sessions;

    /// <summary>How many sessions signed in successfully.</summary>
    public int Authentications;

    public IReadOnlyList<Message> Messages => _messages.ToArray();

    public static FakeSmtpServer Start(string? userName = null, string? password = null) => new(userName, password);

    public async Task<Message?> WaitForMessageAsync(Func<Message, bool> match, TimeSpan? timeout = null)
    {
        Message? found = null;
        await ServicesFixture.PollAsync(
            () =>
            {
                found = _messages.FirstOrDefault(match);
                return Task.FromResult(found is not null);
            },
            timeout ?? TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(250));
        return found;
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception)
            {
                return;
            }

            _ = Task.Run(() => SessionAsync(client));
        }
    }

    private async Task SessionAsync(TcpClient client)
    {
        Interlocked.Increment(ref Sessions);
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII);
                await using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

                await writer.WriteLineAsync("220 fake-smtp ESMTP ready");
                string? from = null;
                var to = new List<string>();
                string? user = null;
                string? pass = null;
                bool authenticated = ExpectedUserName is null;

                while (await reader.ReadLineAsync() is { } line)
                {
                    string verb = line.Split(' ', 2)[0].ToUpperInvariant();
                    switch (verb)
                    {
                        case "EHLO":
                            await writer.WriteLineAsync("250-fake-smtp");
                            await writer.WriteLineAsync("250-AUTH PLAIN LOGIN");
                            await writer.WriteLineAsync("250 8BITMIME");
                            break;
                        case "HELO":
                            await writer.WriteLineAsync("250 fake-smtp");
                            break;
                        case "AUTH":
                            (user, pass) = await AuthenticateAsync(line, reader, writer);
                            authenticated = user == ExpectedUserName && pass == ExpectedPassword;
                            if (authenticated)
                            {
                                Interlocked.Increment(ref Authentications);
                                await writer.WriteLineAsync("235 2.7.0 Authentication successful");
                            }
                            else
                            {
                                await writer.WriteLineAsync("535 5.7.8 Authentication credentials invalid");
                            }

                            break;
                        case "MAIL":
                            if (!authenticated)
                            {
                                await writer.WriteLineAsync("530 5.7.0 Authentication required");
                                break;
                            }

                            from = Address(line);
                            to.Clear();
                            await writer.WriteLineAsync("250 OK");
                            break;
                        case "RCPT":
                            string recipient = Address(line);
                            if (RefusedRecipients.Contains(recipient))
                            {
                                await writer.WriteLineAsync("550 5.1.1 No such user here");
                            }
                            else
                            {
                                to.Add(recipient);
                                await writer.WriteLineAsync("250 OK");
                            }

                            break;
                        case "DATA":
                            await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                            var data = new StringBuilder();
                            while (await reader.ReadLineAsync() is { } dataLine && dataLine != ".")
                            {
                                data.AppendLine(dataLine.StartsWith("..", StringComparison.Ordinal) ? dataLine[1..] : dataLine);
                            }

                            _messages.Enqueue(new Message(from ?? string.Empty, to.ToList(), data.ToString(), user, pass));
                            await writer.WriteLineAsync("250 OK queued");
                            break;
                        case "RSET":
                            from = null;
                            to.Clear();
                            await writer.WriteLineAsync("250 OK");
                            break;
                        case "NOOP":
                            await writer.WriteLineAsync("250 OK");
                            break;
                        case "QUIT":
                            await writer.WriteLineAsync("221 Bye");
                            return;
                        default:
                            await writer.WriteLineAsync("502 5.5.2 Command not implemented");
                            break;
                    }
                }
            }
            catch (Exception)
            {
                // The client hung up.
            }
        }
    }

    private static async Task<(string? User, string? Password)> AuthenticateAsync(string line, StreamReader reader, StreamWriter writer)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string mechanism = parts.Length > 1 ? parts[1].ToUpperInvariant() : string.Empty;
        if (mechanism == "PLAIN")
        {
            string? initial = parts.Length > 2 ? parts[2] : null;
            if (initial is null)
            {
                await writer.WriteLineAsync("334 ");
                initial = await reader.ReadLineAsync();
            }

            string[] fields = Decode(initial).Split('\0');
            return fields.Length == 3 ? (fields[1], fields[2]) : (null, null);
        }

        if (mechanism == "LOGIN")
        {
            string? user = parts.Length > 2 ? Decode(parts[2]) : null;
            if (user is null)
            {
                await writer.WriteLineAsync("334 VXNlcm5hbWU6");
                user = Decode(await reader.ReadLineAsync());
            }

            await writer.WriteLineAsync("334 UGFzc3dvcmQ6");
            return (user, Decode(await reader.ReadLineAsync()));
        }

        return (null, null);
    }

    private static string Decode(string? base64)
    {
        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(base64 ?? string.Empty));
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    // "MAIL FROM:<a@b> SIZE=123" or "RCPT TO:<a@b>" -> a@b
    private static string Address(string line)
    {
        int start = line.IndexOf('<');
        int end = line.IndexOf('>', start + 1);
        return start >= 0 && end > start ? line[(start + 1)..end] : string.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();
        await _loop;
        _stop.Dispose();
    }
}
