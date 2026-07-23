using System.Text.Json;

namespace AcceptanceSpecSystem.Api.Services;

/// <summary>
/// Application 匹配事件流到 ASP.NET Core SSE 响应的协议适配器。
/// </summary>
public sealed class HttpMatchingEventStream : IMatchingEventStream
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpResponse _response;

    public HttpMatchingEventStream(HttpResponse response)
    {
        _response = response;
    }

    public bool IsClientDisconnected => _response.HttpContext.RequestAborted.IsCancellationRequested;

    public void Prepare()
    {
        _response.Headers.CacheControl = "no-cache";
        _response.Headers.TryAdd("X-Accel-Buffering", "no");
        _response.ContentType = "text/event-stream";
    }

    public async Task WriteEventAsync(string eventName, object data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);
        await _response.WriteAsync($"event: {eventName}\n", cancellationToken);
        await _response.WriteAsync($"data: {json}\n\n", cancellationToken);
        await _response.Body.FlushAsync(cancellationToken);
    }
}
