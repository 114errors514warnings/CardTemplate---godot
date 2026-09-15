using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>Localhost-only HTTP transport. It never accesses Godot or battle state directly.</summary>
public sealed class BattleApiHttpServer : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Func<BattleCommandRequest, Task<BattleCommandResult>> dispatch;
    private readonly JsonSerializerOptions json = new() { PropertyNameCaseInsensitive = true };

    public BattleApiHttpServer(Func<BattleCommandRequest, Task<BattleCommandResult>> dispatch, int port)
    { this.dispatch = dispatch; listener.Prefixes.Add($"http://127.0.0.1:{port}/api/game/"); }

    public void Start() { if (!listener.IsListening) { listener.Start(); _ = Task.Run(ServerLoop); } }
    private async Task ServerLoop()
    {
        while (!cancellation.IsCancellationRequested)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync(); }
            catch when (cancellation.IsCancellationRequested) { break; }
            catch { continue; }
            _ = Task.Run(() => Handle(context));
        }
    }

    private async Task Handle(HttpListenerContext context)
    {
        try
        {
            BattleCommandRequest request;
            if (context.Request.HttpMethod == "GET") request = new BattleCommandRequest { Type = "battle.state" };
            else
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                request = JsonSerializer.Deserialize<BattleCommandRequest>(await reader.ReadToEndAsync(), json);
            }
            await Write(context.Response, await dispatch(request).WaitAsync(TimeSpan.FromSeconds(10)));
        }
        catch (Exception ex) { await Write(context.Response, BattleCommandResult.Fail("REQUEST_ERROR", ex.Message)); }
    }

    private static async Task Write(HttpListenerResponse response, BattleCommandResult result)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result));
        response.StatusCode = result.Ok ? 200 : 400; response.ContentType = "application/json; charset=utf-8"; response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes); response.Close();
    }

    public void Dispose() { cancellation.Cancel(); if (listener.IsListening) listener.Stop(); listener.Close(); cancellation.Dispose(); }
}
