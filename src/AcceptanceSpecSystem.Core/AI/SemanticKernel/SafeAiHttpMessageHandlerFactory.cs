using System.Net;
using AcceptanceSpecSystem.Core.AI.Models;

namespace AcceptanceSpecSystem.Core.AI.SemanticKernel;

public interface ISafeAiHttpClientFactory
{
    long Generation { get; }

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

    private readonly IAiEndpointAccessPolicy _policy;
    private readonly IAiSocketConnector _connector;
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, PoolEntry> _poolCache = new(StringComparer.Ordinal);
    private long _accessSequence;
    private bool _disposed;

    public SafeAiHttpMessageHandlerFactory(
        IAiEndpointAccessPolicy policy,
        IAiSocketConnector connector)
    {
        _policy = policy;
        _connector = connector;
    }

    public long Generation => _policy.Generation;

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
        var generation = Generation;
        PoolEntry entry;
        lock (_cacheGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var key = BuildPoolKey(serviceType, origin, generation);
            if (!_poolCache.TryGetValue(key, out entry!))
            {
                entry = new PoolEntry(CreateHandler(serviceType, origin, generation));
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

    public HttpMessageHandler CreateHandler(
        AiServiceType serviceType,
        Uri endpoint,
        long generation)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var origin = GetOrigin(endpoint);
        var socketsHandler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (!IsSameHostAndPort(context.DnsEndPoint, origin))
                    throw OriginMismatch();

                var resolution = await _policy.ValidateAsync(
                    origin,
                    serviceType,
                    generation,
                    cancellationToken).ConfigureAwait(false);
                foreach (var address in resolution.Addresses)
                {
                    _policy.EnsureCurrent(resolution.Generation);
                    try
                    {
                        return await _connector.ConnectAsync(
                            address,
                            origin.Port,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                    }
                }

                throw new AiEndpointAccessException(
                    AiEndpointAccessFailureCategory.ConnectFailed,
                    "AI 端点连接失败");
            }
        };

        return new OriginGuardHandler(origin, _policy, generation)
        {
            InnerHandler = socketsHandler
        };
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

    private static string BuildPoolKey(
        AiServiceType serviceType,
        Uri origin,
        long generation)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{(int)serviceType}|{origin.Scheme.ToLowerInvariant()}|{origin.IdnHost.TrimEnd('.').ToLowerInvariant()}|{origin.Port}|{generation}");
    }

    private static Uri GetOrigin(Uri endpoint)
    {
        return new UriBuilder(endpoint)
        {
            Path = string.Empty,
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;
    }

    private static bool IsSameHostAndPort(DnsEndPoint target, Uri origin)
    {
        return target.Port == origin.Port &&
               string.Equals(
                   target.Host.TrimEnd('.'),
                   origin.IdnHost.TrimEnd('.'),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static AiEndpointAccessException OriginMismatch() =>
        new(
            AiEndpointAccessFailureCategory.RequestOriginMismatch,
            "AI 请求地址与配置端点不一致");

    private sealed class OriginGuardHandler(
        Uri origin,
        IAiEndpointAccessPolicy policy,
        long generation) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            policy.EnsureCurrent(generation);

            var requestUri = request.RequestUri;
            if (requestUri == null ||
                !requestUri.IsAbsoluteUri ||
                !string.IsNullOrEmpty(request.Headers.Host) ||
                !string.Equals(requestUri.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    requestUri.IdnHost.TrimEnd('.'),
                    origin.IdnHost.TrimEnd('.'),
                    StringComparison.OrdinalIgnoreCase) ||
                requestUri.Port != origin.Port)
            {
                throw OriginMismatch();
            }

            return base.SendAsync(request, cancellationToken);
        }
    }

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
