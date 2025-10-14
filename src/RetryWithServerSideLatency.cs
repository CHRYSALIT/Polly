using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Chrysalit.Polly;

/// <summary>
/// Retry strategy that mitigates server-side replication latency by attempting the work
/// across multiple unique connections (coupon collector / occupancy problem).
/// </summary>
public static class RetryWithServerSideLatency
{
    /// <summary>
    /// Default predicate for <see cref="RetryStrategyOptions"/> that retries on any exception
    /// except <see cref="OperationCanceledException"/>.
    /// </summary>
    private static readonly Func<RetryPredicateArguments<object>, bool> DefaultShouldHandle =
        args => args.Outcome.Exception is not null && args.Outcome.Exception is not OperationCanceledException;

    private static class RetryResiliencePropertyKeys<TConn>
    {
        internal static readonly ResiliencePropertyKey<Func<TConn>> FACTORY_KEY = new("factory");
        internal static readonly ResiliencePropertyKey<Action<TConn>> CLEANER_KEY = new("cleaner");
        internal static readonly ResiliencePropertyKey<TConn> CONN_KEY = new("connection");
        internal static readonly ResiliencePropertyKey<int> COUNT_KEY = new("count");
        internal static readonly ResiliencePropertyKey<HashSet<TConn>> VISITED_KEY = new("visited");
    }

    /// <summary>
    /// Optional logger for internal diagnostics.
    /// </summary>
    public static ILogger? Logger { private get; set; }

    #region Execute overloads
    /// <summary>
    /// Execute <paramref name="Action"/> with a retry strategy that mitigates server-side latency by
    /// attempting to use unique <typeparamref name="TConn"/> instances on each retry.
    /// Retrying continues until <paramref name="maxUniqueTConnExpected"/> unique connections are used,
    /// or the retry policy stops earlier due to success or the predicate decision.
    /// Uniqueness is determined by <paramref name="TConnEqualityComparer"/>, if provided.
    /// </summary>
    /// <typeparam name="TConn">Connection type.</typeparam>
    /// <param name="TConnFactory">Factory delegate to create new <typeparamref name="TConn"/> instances.</param>
    /// <param name="TConnCleaner">Cleanup delegate for <typeparamref name="TConn"/> instances (e.g., dispose).</param>
    /// <param name="maxUniqueTConnExpected">Maximum number of unique connections to attempt.</param>
    /// <param name="maxUniqueTConnAcquisitionAttempts">Maximum attempts to acquire a new unique connection per retry. Use 0 for unlimited (bounded by <paramref name="maxUniqueTConnExpected"/>).</param>
    /// <param name="Action">Action to execute using the current connection.</param>
    public static void Execute<TConn>(
        Func<TConn> TConnFactory,
        Action<TConn> TConnCleaner,
        int maxUniqueTConnExpected,
        int maxUniqueTConnAcquisitionAttempts,
        Action<TConn> Action) where TConn : class
    {
        Execute(TConnFactory, TConnCleaner, ShouldHandle: null, maxUniqueTConnExpected, maxUniqueTConnAcquisitionAttempts, Action, null);
    }

    /// <summary>
    /// Execute <paramref name="Action"/> with a retry strategy and return a result.
    /// See <see cref="Execute{TConn}(Func{TConn}, Action{TConn}, int, int, Action{TConn})"/> for behavior.
    /// </summary>
    public static TResult Execute<TConn, TResult>(
        Func<TConn> TConnFactory,
        Action<TConn> TConnCleaner,
        int maxUniqueTConnExpected,
        int maxUniqueTConnAcquisitionAttempts,
        Func<TConn, TResult> Action) where TConn : class
    {
        return Execute(TConnFactory, TConnCleaner, ShouldHandle: null, maxUniqueTConnExpected, maxUniqueTConnAcquisitionAttempts, Action, null);
    }

    /// <summary>
    /// Execute <paramref name="Action"/> with a retry strategy that uses a custom predicate to decide whether to retry.
    /// </summary>
    /// <param name="ShouldHandle">A predicate that decides whether to retry for a given outcome. Defaults to retrying on any exception except <see cref="OperationCanceledException"/>.</param>
    public static void Execute<TConn>(
        Func<TConn> TConnFactory,
        Action<TConn> TConnCleaner,
        Func<RetryPredicateArguments<object>, bool> ShouldHandle,
        int maxUniqueTConnExpected,
        int maxUniqueTConnAcquisitionAttempts,
        Action<TConn> Action) where TConn : class
    {
        Execute(TConnFactory, TConnCleaner, ShouldHandle, maxUniqueTConnExpected, maxUniqueTConnAcquisitionAttempts, Action, null);
    }

    /// <summary>
    /// Execute <paramref name="Action"/> with a retry strategy and return a result.
    /// Uses a custom predicate to decide whether to retry.
    /// </summary>
    public static TResult Execute<TConn, TResult>(
        Func<TConn> TConnFactory,
        Action<TConn> TConnCleaner,
        Func<RetryPredicateArguments<object>, bool> ShouldHandle,
        int maxUniqueTConnExpected,
        int maxUniqueTConnAcquisitionAttempts,
        Func<TConn, TResult> Action) where TConn : class
    {
        return Execute(TConnFactory, TConnCleaner, ShouldHandle, maxUniqueTConnExpected, maxUniqueTConnAcquisitionAttempts, Action, null);
    }

    /// <summary>
    /// Execute <paramref name="Action"/> with a retry strategy and an optional equality comparer for uniqueness.
    /// </summary>
    /// <param name="TConnEqualityComparer">Optional equality comparer used to determine unique connections.</param>
    public static void Execute<TConn>(
        Func<TConn> TConnFactory,
        Action<TConn> TConnCleaner,
        int maxUniqueTConnExpected,
        int maxUniqueTConnAcquisitionAttempts,
        Action<TConn> Action,
        IEqualityComparer<TConn>? TConnEqualityComparer = null) where TConn : class
    {
        Execute(TConnFactory, TConnCleaner, ShouldHandle: null, maxUniqueTConnExpected, maxUniqueTConnAcquisitionAttempts, Action, TConnEqualityComparer);
    }

    /// <summary>
    /// Execute <paramref name="Action"/> with a retry strategy and return a result.
    /// Includes an optional equality comparer for uniqueness.
    /// </summary>
    public static TResult Execute<TConn, TResult>(
        Func<TConn> TConnFactory,
        Action<TConn> TConnCleaner,
        int maxUniqueTConnExpected,
        int maxUniqueTConnAcquisitionAttempts,
        Func<TConn, TResult> Action,
        IEqualityComparer<TConn>? TConnEqualityComparer = null) where TConn : class
    {
        return Execute(TConnFactory, TConnCleaner, ShouldHandle: null, maxUniqueTConnExpected, maxUniqueTConnAcquisitionAttempts, Action, TConnEqualityComparer);
    }

    /// <summary>
    /// Execute <paramref name="Action"/> with a retry strategy, a custom predicate, and an optional equality comparer for uniqueness.
    /// </summary>
    public static void Execute<TConn>(
       Func<TConn> TConnFactory,
       Action<TConn> TConnCleaner,
       Func<RetryPredicateArguments<object>, bool>? ShouldHandle,
       int maxUniqueTConnExpected,
       int maxUniqueTConnAcquisitionAttempts,
       Action<TConn> Action,
       IEqualityComparer<TConn>? TConnEqualityComparer = null) where TConn : class
    {
        var shouldHandle = ShouldHandle ?? DefaultShouldHandle;

        var pipeline = BuildResiliencePipeline(shouldHandle, maxUniqueTConnExpected, maxUniqueTConnAcquisitionAttempts, TConnEqualityComparer);

        var context = CreateResilienceContext(TConnFactory, TConnCleaner, maxUniqueTConnExpected);

        try
        {
            pipeline.Execute(ctx =>
            {
                ctx.Properties.TryGetValue(RetryResiliencePropertyKeys<TConn>.CONN_KEY, out var conn);
                if (conn is not null)
                {
                    Action(conn);
                }
                else
                {
                    throw new InvalidOperationException("No connection available for execution.");
                }
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
    /// Execute <paramref name="Action"/> with a retry strategy that returns a result, a custom predicate,
    /// and an optional equality comparer for uniqueness.
    /// </summary>
    public static TResult Execute<TConn, TResult>(
        Func<TConn> TConnFactory,
        Action<TConn> TConnCleaner,
        Func<RetryPredicateArguments<object>, bool>? ShouldHandle,
        int maxUniqueTConnExpected,
        int maxUniqueTConnAcquisitionAttempts,
        Func<TConn, TResult> Action,
        IEqualityComparer<TConn>? TConnEqualityComparer = null) where TConn : class
    {
        var shouldHandle = ShouldHandle ?? DefaultShouldHandle;

        var pipeline = BuildResiliencePipeline<TConn>(shouldHandle, maxUniqueTConnExpected, maxUniqueTConnAcquisitionAttempts, TConnEqualityComparer);

        var context = CreateResilienceContext(TConnFactory, TConnCleaner, maxUniqueTConnExpected);

        try
        {
            return pipeline.Execute(ctx =>
            {
                // connection generated first at context creation
                // on next retries, connection is generated in OnRetry and set on the context
                ctx.Properties.TryGetValue(RetryResiliencePropertyKeys<TConn>.CONN_KEY, out var conn);
                if (conn is not null)
                {
                    return Action(conn);
                }
                else
                {
                    throw new InvalidOperationException("No connection available for execution.");
                }
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
    #endregion

    /// <summary>
    /// Create a resilience context to be used with the retry strategy.
    /// </summary>
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
        Func<RetryPredicateArguments<object>, bool>? ShouldHandle,
        int maxUniqueTConnExpected,
        int maxUniqueTConnAcquisitionAttempts,
        IEqualityComparer<TConn>? comparer) where TConn : class
    {
        var shouldHandle = ShouldHandle ?? DefaultShouldHandle;
        var builder = new ResiliencePipelineBuilder();
        if (maxUniqueTConnExpected > 1)
        {
            builder.AddRetry(new RetryStrategyOptions
            {
                Name = nameof(RetryWithServerSideLatency),
                ShouldHandle = args => ValueTask.FromResult(shouldHandle.Invoke(args)),
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
                        visited = comparer is null ? new HashSet<TConn>() : new HashSet<TConn>(comparer);
                        ctx.Properties.Set(RetryResiliencePropertyKeys<TConn>.VISITED_KEY, visited);
                    }

                    // Mark current as visited, but DO NOT dispose yet
                    if (connection is not null)
                    {
                        visited.Add(connection);
                    }

                    // Try to acquire a new unique connection
                    TConn? newConnection = null;
                    var attempt = 0;

                    // Re-evaluate the acquisition-attempt bound each iteration.
                    while (newConnection == null
                           && (maxUniqueTConnAcquisitionAttempts == 0 || attempt < maxUniqueTConnAcquisitionAttempts)
                           && visited.Count < maxUniqueTConnExpected)
                    {
                        attempt++;
                        var candidate = factory?.Invoke();

                        if (candidate is null)
                        {
                            continue;
                        }

                        if (visited.Contains(candidate))
                        {
                            // Candidate already seen → dispose candidate and continue
                            cleaner?.Invoke(candidate);
                            continue;
                        }

                        // Found a unique connection
                        newConnection = candidate;
                    }

                    if (newConnection is not null)
                    {
                        // Put new connection into the context first
                        ctx.Properties.Set(RetryResiliencePropertyKeys<TConn>.CONN_KEY, newConnection);

                        // Now it's safe to dispose the previous one
                        if (connection is not null)
                        {
                            cleaner?.Invoke(connection);
                        }
                    }
                    return ValueTask.CompletedTask;
                }
            });
        }
        return builder.Build();
    }
}
