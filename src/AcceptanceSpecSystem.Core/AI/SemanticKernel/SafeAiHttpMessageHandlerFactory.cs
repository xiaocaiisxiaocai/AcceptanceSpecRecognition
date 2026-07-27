using AcceptanceSpecSystem.Core.AI.Models;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

public interface ISafeAiHttpClientFactory
{
    HttpClient CreateClient(
        AiServiceType serviceType,
        string endpoint,
        TimeSpan? timeout = null);
}

public sealed class SafeAiHttpMessageHandlerFactory :
    ISafeAiHttpClientFactory,
    IDisposable
{
    private const int PoolCacheLimit = 64;

    private readonly object _cacheGate = new();
    private readonly Dictionary<string, PoolEntry> _poolCache = new(StringComparer.Ordinal);
    private long _accessSequence;
    private bool _disposed;

    public HttpClient CreateClient(
        AiServiceType serviceType,
        string endpoint,
        TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var effectiveTimeout = timeout ?? AiServiceHttpClientDefaults.LongRunningNetworkTimeout;
        if (effectiveTimeout != Timeout.InfiniteTimeSpan &&
            (effectiveTimeout <= TimeSpan.Zero ||
             effectiveTimeout > TimeSpan.FromMilliseconds(int.MaxValue)))
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        var normalized = AiEndpointNormalizer.NormalizeRequiredEndpoint(endpoint);
        var origin = GetOrigin(new Uri(normalized));
        PoolEntry entry;
        lock (_cacheGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var key = BuildPoolKey(serviceType, origin);
            if (!_poolCache.TryGetValue(key, out entry!))
            {
                entry = new PoolEntry(CreateHandler(origin));
                _poolCache.Add(key, entry);
            }

            entry.Acquire(++_accessSequence);
            EvictOverflowPools();
        }

        return new LeasedHttpClient(entry.Handler, entry.Release)
        {
            Timeout = effectiveTimeout
        };
    }

    internal static HttpMessageHandler CreateHandler(Uri endpoint)
    {
        var origin = GetOrigin(endpoint);
        var socketsHandler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1)
        };

        return new ExactOriginGuardHandler(origin, socketsHandler);
    }

    public void Dispose()
    {
        PoolEntry[] entries;
        lock (_cacheGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            entries = _poolCache.Values.ToArray();
            _poolCache.Clear();
        }

        foreach (var entry in entries)
            entry.Retire();
    }

    private void EvictOverflowPools()
    {
        while (_poolCache.Count > PoolCacheLimit)
        {
            var oldest = _poolCache.MinBy(static pair => pair.Value.LastAccess);
            _poolCache.Remove(oldest.Key);
            oldest.Value.Retire();
        }
    }

    private static string BuildPoolKey(AiServiceType serviceType, Uri origin)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{(int)serviceType}|{origin.Scheme.ToLowerInvariant()}|{NormalizeHost(origin)}|{origin.Port}");
    }

    internal static Uri GetOrigin(Uri endpoint)
    {
        return new UriBuilder(endpoint)
        {
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;
    }

    internal static bool IsSameOrigin(Uri requestUri, Uri origin)
    {
        return requestUri.IsAbsoluteUri &&
               string.Equals(requestUri.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(NormalizeHost(requestUri), NormalizeHost(origin), StringComparison.Ordinal) &&
               requestUri.Port == origin.Port;
    }

    private static string NormalizeHost(Uri uri) =>
        uri.IdnHost.TrimEnd('.').ToLowerInvariant();

    private sealed class PoolEntry(HttpMessageHandler handler)
    {
        private readonly object _gate = new();
        private int _leases;
        private bool _retired;
        private bool _disposed;

        public HttpMessageHandler Handler { get; } = handler;

        public long LastAccess { get; private set; }

        public void Acquire(long access)
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _leases++;
                LastAccess = access;
            }
        }

        public void Release()
        {
            HttpMessageHandler? dispose = null;
            lock (_gate)
            {
                if (_leases > 0)
                    _leases--;
                if (_retired && _leases == 0 && !_disposed)
                {
                    _disposed = true;
                    dispose = Handler;
                }
            }

            dispose?.Dispose();
        }

        public void Retire()
        {
            HttpMessageHandler? dispose = null;
            lock (_gate)
            {
                if (_retired)
                    return;
                _retired = true;
                if (_leases == 0 && !_disposed)
                {
                    _disposed = true;
                    dispose = Handler;
                }
            }

            dispose?.Dispose();
        }
    }

    private sealed class LeasedHttpClient(
        HttpMessageHandler handler,
        Action release)
        : HttpClient(handler, disposeHandler: false)
    {
        private Action? _release = release;

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }
}

internal sealed class ExactOriginGuardHandler : DelegatingHandler
{
    private readonly Uri _origin;

    public ExactOriginGuardHandler(Uri origin, HttpMessageHandler innerHandler)
    {
        _origin = SafeAiHttpMessageHandlerFactory.GetOrigin(origin);
        InnerHandler = innerHandler;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        EnsureAllowed(request);
        return base.SendAsync(request, cancellationToken);
    }

    protected override HttpResponseMessage Send(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        EnsureAllowed(request);
        return base.Send(request, cancellationToken);
    }

    private void EnsureAllowed(HttpRequestMessage request)
    {
        if (request.RequestUri is not { } requestUri ||
            !string.IsNullOrEmpty(request.Headers.Host) ||
            !SafeAiHttpMessageHandlerFactory.IsSameOrigin(requestUri, _origin))
        {
            throw new AiEndpointAccessException(
                AiEndpointAccessFailureCategory.RequestOriginMismatch,
                "AI 请求地址与配置端点不一致");
        }
    }
}
