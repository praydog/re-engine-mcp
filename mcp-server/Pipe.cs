using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace REFrameworkMcp;

static class Pipe
{
    static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    static int _nextId = 1;

    // Serialize all pipe operations so concurrent tool calls don't interleave.
    static readonly SemaphoreSlim _gate = new(1, 1);

    public static bool IsAvailable { get; private set; }

    /// <summary>
    /// Send a request and get the JSON response string.
    /// Returns null if the pipe is not available.
    /// </summary>
    public static Task<string?> Request(string method) => Request(method, null, 5000);

    /// <summary>
    /// Send a request with extra parameters and a custom read timeout.
    /// Each call connects, sends, reads, and disconnects — so multiple
    /// MCP server processes (one per client) can share the single pipe.
    /// </summary>
    public static async Task<string?> Request(string method, Dictionary<string, object>? extra, int readTimeoutMs = 5000)
    {
        var id = Interlocked.Increment(ref _nextId);

        // Build request object with method, id, and any extra params
        var requestDict = new Dictionary<string, object> { ["method"] = method, ["id"] = id };
        if (extra != null)
            foreach (var kv in extra)
                requestDict[kv.Key] = kv.Value;

        var request = JsonSerializer.Serialize(requestDict);

        await _gate.WaitAsync();
        try
        {
            return await ConnectSendReceive(request, readTimeoutMs);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Connect-per-request: open pipe, send, read response, close.
    /// Retries once to cover the race where the game's pipe server is still starting.
    /// </summary>
    static async Task<string?> ConnectSendReceive(string request, int readTimeoutMs)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = new NamedPipeClientStream(".", "REFrameworkNET", PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(2000); // 2-second timeout per attempt

                var writer = new StreamWriter(pipe, Utf8NoBom, leaveOpen: true) { AutoFlush = true };
                var reader = new StreamReader(pipe, Utf8NoBom, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

                await writer.WriteLineAsync(request);
                await writer.FlushAsync();
                pipe.Flush();

                // Read with timeout to avoid hanging forever
                using var cts = new CancellationTokenSource(readTimeoutMs);
                var response = await reader.ReadLineAsync(cts.Token);

                // Clean up streams (pipe disposed in finally)
                writer.Dispose();
                reader.Dispose();

                if (response == null)
                {
                    IsAvailable = false;
                    return null;
                }

                IsAvailable = true;

                // Extract just the "result" or "error" from the response
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("error", out var err))
                    return JsonSerializer.Serialize(new { error = err.GetString() });

                if (root.TryGetProperty("result", out var result))
                    return result.GetRawText();

                return response;
            }
            catch
            {
                // First attempt failed — retry once
            }
            finally
            {
                try { pipe?.Dispose(); } catch { }
            }
        }

        IsAvailable = false;
        return null;
    }
}
