// Scripts/Api/ApiHttpServer.cs
// 本机 HTTP 传输层（唯一监听 socket 的地方）：只解析 JSON 与拼响应，**绝不**碰 Godot 节点或战斗状态。
// 请求交给 `ApiService` → 主线程队列 → 对应通道的路由类。

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public sealed class ApiHttpServer : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Func<ApiRequest, Task<ApiResult>> dispatch;
    private readonly JsonSerializerOptions json = new() { PropertyNameCaseInsensitive = true };

    public ApiHttpServer(Func<ApiRequest, Task<ApiResult>> dispatch, int port)
    {
        this.dispatch = dispatch;
        listener.Prefixes.Add($"http://127.0.0.1:{port}/api/game/");
    }

    public void Start()
    {
        if (listener.IsListening) return;
        listener.Start();
        _ = Task.Run(ServerLoop);
    }

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
            ApiRequest request;
            if (context.Request.HttpMethod == "GET") request = new ApiRequest { Type = "api.read" };
            else
            {
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                request = JsonSerializer.Deserialize<ApiRequest>(await reader.ReadToEndAsync(), json);
            }
            await Write(context.Response, await dispatch(request).WaitAsync(TimeSpan.FromSeconds(10)));
        }
        catch (Exception ex)
        {
            await Write(context.Response, ApiResult.Fail("", ApiLane.Player, "REQUEST_ERROR", ex.Message));
        }
    }

    private static async Task Write(HttpListenerResponse response, ApiResult result)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(result));
        response.StatusCode = result.Ok ? 200 : 400;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    public void Dispose()
    {
        cancellation.Cancel();
        if (listener.IsListening) listener.Stop();
        listener.Close();
        cancellation.Dispose();
    }
}
