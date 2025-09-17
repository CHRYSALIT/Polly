using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Chrysalit.Polly;

public static class RetryWithServerSideLatency
{
    private static class RetryResiliencePropertyKeys<TConn>
    {
        internal static readonly ResiliencePropertyKey<Func<TConn>> FACTORY_KEY = new ("factory");
        internal static readonly ResiliencePropertyKey<Action<TConn>> CLEANER_KEY = new ("cleaner");
        internal static readonly ResiliencePropertyKey<TConn> CONN_KEY = new ("connection");
        internal static readonly ResiliencePropertyKey<int> COUNT_KEY = new("count");
        internal static readonly ResiliencePropertyKey<HashSet<TConn>> VISITED_KEY = new("visited");
    }

    /// <summary>
    /// Provides internal logging.
    /// </summary>
    public static ILogger? Logger { private get; set; }

    /// <summary>
    /// Excute <paramref name="Action"/> with a retry strategy that handles server-side latency by attempting
    /// to use different <typeparamref name="TConn"/> instances on each retry.
    /// This is retried until <paramref name="maxUniqueTConnExpected"/> unique <typeparamref name="TConn"/> instances are used.
    /// Uniqueness of <typeparamref name="TConn"/> instances is controlled by an optional <paramref name="TConnEqualityComparer"/>.
    /// </summary>
    /// <typeparam name="TConn"></typeparam>
    /// <param name="TConnFactory">Delegate to create new instances of <typeparamref name="TConn"/>.</param>
    /// <param name="TConnCleaner">Delegate to clean the resources created. Useful when <typeparamref name="TConn"/> inherits <see cref="IDisposable"/>.</param>
    /// <param name="maxUniqueTConnExpected">Max unique number of <typeparamref name="TConn"/> to create.</param>
    /// <param name="maxUniqueTConnAcquisitionAttempts">Max number of attempts to acquire a new unique <typeparamref name="TConn"/>. The more <paramref name="maxUniqueTConnExpected"/>, the more <paramref name="maxUniqueTConnAcquisitionAttempts"/> may be required.</param>
    /// <param name="Action">Action to perform using current <typeparamref name="TConn"/>.</param>
    /// <param name="TConnEqualityComparer">Optional <see cref="IEqualityComparer{TConn}"/> for <typeparamref name="TConn"/>.</param>
    public static void Execute<TConn>(
        Func<TConn> TConnFactory,
        Action<TConn> TConnCleaner,
        int maxUniqueTConnExpected,
        int maxUniqueTConnAcquisitionAttempts,
        Action<TConn> Action,
        IEqualityComparer<TConn>? TConnEqualityComparer = null) where TConn : class
        {
        
        var pipeline = BuildResiliencePipeline(maxUniqueTConnExpected, maxUniqueTConnAcquisitionAttempts, TConnEqualityComparer);

        var context = CreateResilienceContext(TConnFactory, TConnCleaner, maxUniqueTConnExpected);

        try
        {
            pipeline.Execute(ctx =>
            {
                ctx.Properties.TryGetValue(RetryResiliencePropertyKeys<TConn>.CONN_KEY, out var conn);
                Action(conn);
            }, context);
        }
        finally
        {
            if (context.Properties.TryGetValue(RetryResiliencePropertyKeys<TConn>.CONN_KEY, out var conn))
            {
                TConnCleaner(conn);
            }
            ResilienceContextPool.Shared.Return(context);
        }
    }

    public static TResult Execute<TConn, TResult>(
        Func<TConn> TConnFactory,
        Action<TConn> TConnCleaner,
        int maxUniqueTConnExpected,
        int maxUniqueTConnAcquisitionAttempts,
        Func<TConn, TResult> Action,
        IEqualityComparer<TConn>? TConnEqualityComparer = null) where TConn : class
    {
        var pipeline = BuildResiliencePipeline<TConn>(maxUniqueTConnExpected, maxUniqueTConnAcquisitionAttempts, TConnEqualityComparer);

        var context = CreateResilienceContext(TConnFactory, TConnCleaner, maxUniqueTConnExpected);

        try
        {
            return pipeline.Execute(ctx =>
            {
                ctx.Properties.TryGetValue(RetryResiliencePropertyKeys<TConn>.CONN_KEY, out var conn);
                return Action(conn);
            }, context);
        }
        finally
        {
            if (context.Properties.TryGetValue(RetryResiliencePropertyKeys<TConn>.CONN_KEY, out var conn))
            {
                TConnCleaner(conn);
            }
            ResilienceContextPool.Shared.Return(context);
        }
    }

    /// <summary>
    /// Create a resilience context to be used with the retry strategy.
    /// </summary>
    /// <typeparam name="TConn"></typeparam>
    /// <returns><see cref="ResilienceContext"/></returns>
    public static ResilienceContext CreateResilienceContext<TConn>(
        Func<TConn> factory,
        Action<TConn> cleaner,
        int retryCount)
    {
        var context = ResilienceContextPool.Shared.Get();
        context.Properties.Set(RetryResiliencePropertyKeys<TConn>.FACTORY_KEY, factory);
        context.Properties.Set(RetryResiliencePropertyKeys<TConn>.CLEANER_KEY, cleaner);
        context.Properties.Set(RetryResiliencePropertyKeys<TConn>.COUNT_KEY, retryCount);
        context.Properties.Set(RetryResiliencePropertyKeys<TConn>.CONN_KEY, factory.Invoke());
        return context;
    }

    private static ResiliencePipeline BuildResiliencePipeline<TConn>(
        int maxUniqueTConnExpected,
        int maxUniqueTConnAcquisitionAttempts,
        IEqualityComparer<TConn>? comparer) where TConn : class
    {
        return new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = maxUniqueTConnExpected - 1,
                Delay = TimeSpan.Zero,
                OnRetry = args =>
                {
                    var ctx = args.Context;
                    
                    Logger?.LogDebug("Retry {retry} / {maxretry}", args.AttemptNumber, maxUniqueTConnExpected - 1);

                    ctx.Properties.TryGetValue(RetryResiliencePropertyKeys<TConn>.FACTORY_KEY, out var factory);
                    ctx.Properties.TryGetValue(RetryResiliencePropertyKeys<TConn>.CLEANER_KEY, out var cleaner);
                    ctx.Properties.TryGetValue(RetryResiliencePropertyKeys<TConn>.CONN_KEY, out var connection);

                    if (!ctx.Properties.TryGetValue(RetryResiliencePropertyKeys<TConn>.VISITED_KEY, out var visited))
                    {
                        visited = new HashSet<TConn>(comparer);
                        ctx.Properties.Set(RetryResiliencePropertyKeys<TConn>.VISITED_KEY, visited);
                    }

                    if (connection is not null)
                    {
                        visited.Add(connection);
                        cleaner?.Invoke(connection);
                    }
                    
                    TConn? newConnection = null;
                    int attempt = 0;
                    
                    bool remainsAttempts = maxUniqueTConnAcquisitionAttempts == 0 || 
                                           attempt < maxUniqueTConnAcquisitionAttempts;
                    
                    while (newConnection == null && remainsAttempts && visited.Count < maxUniqueTConnExpected)
                    {
                        attempt++;
                        newConnection = factory?.Invoke();

                        if (newConnection is not null && visited.Contains(newConnection))
                        {
                            cleaner?.Invoke(newConnection);
                            newConnection = default;
                        }
                    }

                    if (newConnection is not null)
                    {
                        ctx.Properties.Set(RetryResiliencePropertyKeys<TConn>.CONN_KEY, newConnection);
                    }
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }
}
