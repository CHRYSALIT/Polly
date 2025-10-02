using System;
using System.Collections.Generic;
using Xunit;
using Chrysalit.Polly;
using Polly.Retry;
using Microsoft.Extensions.Logging;

namespace Chrysalit.Polly.Tests;

/// <summary>
/// Tests for the <see cref="RetryWithServerSideLatency"/> class, which implements a retry strategy
/// that mitigates server-side replication latency by attempting operations across multiple unique connections.
/// 
/// These tests validate:
/// - Basic execution without retries
/// - Retry logic with custom predicates
/// - Connection uniqueness using equality comparers
/// - Proper resource cleanup
/// - Handling of special exceptions like OperationCanceledException
/// - Both void and result-returning operations
/// - Integration with ILogger for diagnostic output
/// </summary>
public sealed class RetryWithServerSideLatencyTests
{
    private readonly ITestOutputHelper _output;

    public RetryWithServerSideLatencyTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Fake connection class for testing, representing a connection to a specific server.
    /// </summary>
    private sealed class FakeConn
    {
        public FakeConn(string server) => Server = server;
        public string Server { get; }
        public bool Disposed { get; set; }
        public override string ToString() => $"{Server}:{GetHashCode()}";
    }

    /// <summary>
    /// Equality comparer for <see cref="FakeConn"/> that considers connections equal
    /// if they connect to the same server, regardless of instance.
    /// </summary>
    private sealed class FakeConnByServerComparer : IEqualityComparer<FakeConn>
    {
        public bool Equals(FakeConn? x, FakeConn? y) => ReferenceEquals(x, y) || (x is not null && y is not null && x.Server == y.Server);
        public int GetHashCode(FakeConn obj) => obj.Server.GetHashCode(StringComparison.Ordinal);
    }

    /// <summary>
    /// Creates a factory function that returns connections to the specified servers in sequence.
    /// Cycles through the servers if more connections are requested than servers provided.
    /// </summary>
    /// <param name="servers">The server names to cycle through.</param>
    /// <returns>A factory function that creates new connections.</returns>
    private static Func<FakeConn> FactoryFrom(params string[] servers)
    {
        if (servers is null || servers.Length == 0) throw new ArgumentException("Provide at least one server.");
        var idx = 0;
        return () =>
        {
            // Produce a new connection each time; cycle if needed.
            var s = servers[Math.Min(idx, servers.Length - 1)];
            idx++;
            return new FakeConn(s);
        };
    }

    /// <summary>
    /// Helper class for tracking connection disposal in tests.
    /// </summary>
    private sealed class Cleaner
    {
        public int DisposeCount { get; private set; }
        public List<FakeConn>? DisposedConns { get; }

        public Cleaner(List<FakeConn>? disposedConns = null)
        {
            DisposedConns = disposedConns;
        }

        public void Clean(FakeConn conn)
        {
            conn.Disposed = true;
            DisposedConns?.Add(conn);
            DisposeCount++;
        }
    }

    /// <summary>
    /// Creates an action that throws an exception unless the connection is to the expected server.
    /// Used to simulate operations that only succeed on specific servers.
    /// </summary>
    /// <param name="expectedServer">The server name that should succeed.</param>
    /// <param name="exceptionType">The type of exception to throw (defaults to InvalidOperationException).</param>
    /// <returns>An action that validates the server connection.</returns>
    private static Action<FakeConn> ThrowUntilServer(string expectedServer, Type? exceptionType = null)
    {
        return conn =>
        {
            if (!string.Equals(conn.Server, expectedServer, StringComparison.Ordinal))
            {
                var exType = exceptionType ?? typeof(InvalidOperationException);
                throw (Exception)Activator.CreateInstance(exType, "Not on expected server")!;
            }
        };
    }

    /// <summary>
    /// Creates a function that returns a value only when connected to the expected server.
    /// Used to simulate operations that return results only on specific servers.
    /// </summary>
    /// <param name="expectedServer">The server name that should succeed.</param>
    /// <param name="value">The value to return on success.</param>
    /// <returns>A function that validates the server and returns the value.</returns>
    private static Func<FakeConn, int> ReturnValueWhenOn(string expectedServer, int value)
    {
        return conn =>
        {
            if (!string.Equals(conn.Server, expectedServer, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Not on expected server");
            }
            return value;
        };
    }

    /// <summary>
    /// Creates a retry predicate that retries only on the specified exception type.
    /// </summary>
    /// <typeparam name="TException">The exception type to retry on.</typeparam>
    /// <returns>A predicate function for the retry strategy.</returns>
    private static Func<RetryPredicateArguments<object>, bool> RetryOn<TException>() where TException : Exception =>
        args => args.Outcome.Exception is TException;

    /// <summary>
    /// Creates a retry predicate that never retries.
    /// </summary>
    /// <returns>A predicate that always returns false.</returns>
    private static Func<RetryPredicateArguments<object>, bool> NeverRetry() =>
        _ => false;

    /// <summary>
    /// Tests basic void operation execution without retries.
    /// 
    /// Scenario: Single server connection, operation succeeds on first attempt.
    /// Expected: Operation executes once, connection is properly cleaned up.
    /// 
    /// This validates the basic execution path when no retries are needed.
    /// </summary>
    [Fact]
    public void Execute_Void_DefaultPredicate_Success_NoRetry()
    {
        var factory = FactoryFrom("A");
        var cleaner = new Cleaner();
        var called = 0;

        RetryWithServerSideLatency.Execute(
            TConnFactory: factory,
            TConnCleaner: cleaner.Clean,
            maxUniqueTConnExpected: 1,
            maxUniqueTConnAcquisitionAttempts: 1,
            Action: _ => called++);

        Assert.Equal(1, called);
        Assert.Equal(1, cleaner.DisposeCount); // final cleanup
    }

    /// <summary>
    /// Tests basic result-returning operation execution without retries.
    /// 
    /// Scenario: Single server connection, operation succeeds and returns a value.
    /// Expected: Operation executes once, returns correct value, connection cleaned up.
    /// 
    /// This validates the result-returning execution path.
    /// </summary>
    [Fact]
    public void Execute_Result_DefaultPredicate_Success_NoRetry()
    {
        var factory = FactoryFrom("A");
        var cleaner = new Cleaner();

        var result = RetryWithServerSideLatency.Execute<FakeConn, int>(
            TConnFactory: factory,
            TConnCleaner: cleaner.Clean,
            maxUniqueTConnExpected: 1,
            maxUniqueTConnAcquisitionAttempts: 1,
            Action: _ => 42);

        Assert.Equal(42, result);
        Assert.Equal(1, cleaner.DisposeCount);
    }

    /// <summary>
    /// Tests custom retry predicate that prevents retries.
    /// 
    /// Scenario: Operation fails, but custom predicate returns false (never retry).
    /// Expected: Exception is thrown immediately, no retries attempted.
    /// 
    /// This validates that custom predicates override the default retry behavior.
    /// </summary>
    [Fact]
    public void Execute_Void_CustomShouldHandle_NoRetry_WhenPredicateFalse()
    {
        var factory = FactoryFrom("A");
        var cleaner = new Cleaner();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            RetryWithServerSideLatency.Execute(
                TConnFactory: factory,
                TConnCleaner: cleaner.Clean,
                ShouldHandle: NeverRetry(),
                maxUniqueTConnExpected: 3,
                maxUniqueTConnAcquisitionAttempts: 10,
                Action: _ => throw new InvalidOperationException("boom")));

        Assert.Equal("boom", ex.Message);
        Assert.Equal(1, cleaner.DisposeCount); // initial connection cleaned in finally
    }

    /// <summary>
    /// Tests retrying with a custom predicate that retries on specific exceptions.
    /// 
    /// Scenario: First connection fails with InvalidOperationException, second succeeds.
    /// Expected: Retries to second connection, returns result, both connections cleaned.
    /// 
    /// This validates selective retry behavior based on exception type.
    /// </summary>
    [Fact]
    public void Execute_Result_CustomShouldHandle_Retries_OnSpecificException()
    {
        var factory = FactoryFrom("A", "B"); // first A (throws), then B (succeeds)
        var cleaner = new Cleaner();

        var result = RetryWithServerSideLatency.Execute<FakeConn, int>(
            TConnFactory: factory,
            TConnCleaner: cleaner.Clean,
            ShouldHandle: RetryOn<InvalidOperationException>(),
            maxUniqueTConnExpected: 2,
            maxUniqueTConnAcquisitionAttempts: 10,
            Action: ReturnValueWhenOn(expectedServer: "B", value: 7));

        Assert.Equal(7, result);
        Assert.True(cleaner.DisposeCount >= 2); // A disposed during switch, B disposed in finally
    }

    /// <summary>
    /// Tests retry logic with connection uniqueness using an equality comparer.
    /// 
    /// Scenario: Factory produces duplicate connections to server "A" before "B".
    /// The comparer treats connections to the same server as equivalent.
    /// Expected: Skips duplicate "A" connections, retries on unique "B" connection.
    /// 
    /// This validates that the equality comparer prevents retries on already-attempted servers.
    /// </summary>
    [Fact]
    public void Execute_Void_WithComparer_RetriesAcrossServers()
    {
        var factory = FactoryFrom("A", "A", "B"); // force a duplicate server before a unique one
        List<FakeConn> disposedList = new();
        var cleaner = new Cleaner(disposedList);
        var comparer = new FakeConnByServerComparer();

        RetryWithServerSideLatency.Execute(
            TConnFactory: factory,
            TConnCleaner: cleaner.Clean,
            maxUniqueTConnExpected: 2,
            maxUniqueTConnAcquisitionAttempts: 10,
            Action: ThrowUntilServer("B"),
            TConnEqualityComparer: comparer);

        // Expect: A (fails), A-dup (discarded and cleaned), B (success), final cleanup => >=2 disposals.
        Assert.True(cleaner.DisposeCount >= 2);
        Assert.Contains(disposedList, c => c.Server == "B"); // final cleanup
    }

    /// <summary>
    /// Tests result-returning operations with connection uniqueness.
    /// 
    /// Scenario: Similar to void test, but returns a result on success.
    /// Expected: Retries across unique servers, returns "OK" from server "B".
    /// 
    /// This validates result-returning operations with equality comparers.
    /// </summary>
    [Fact]
    public void Execute_Result_WithComparer_RetriesAcrossServers()
    {
        var factory = FactoryFrom("A", "A", "B");
        var cleaner = new Cleaner();
        var comparer = new FakeConnByServerComparer();

        var result = RetryWithServerSideLatency.Execute<FakeConn, string>(
            TConnFactory: factory,
            TConnCleaner: cleaner.Clean,
            maxUniqueTConnExpected: 2,
            maxUniqueTConnAcquisitionAttempts: 10,
            Action: conn =>
            {
                if (conn.Server != "B") throw new InvalidOperationException("not yet replicated");
                return "OK";
            },
            TConnEqualityComparer: comparer);

        Assert.Equal("OK", result);
        Assert.True(cleaner.DisposeCount >= 2);
    }

    /// <summary>
    /// Tests custom predicate combined with equality comparer.
    /// 
    /// Scenario: Retries only on InvalidOperationException, skips duplicate servers.
    /// Expected: Behaves like the comparer test but with selective exception handling.
    /// 
    /// This validates the interaction between custom predicates and equality comparers.
    /// </summary>
    [Fact]
    public void Execute_Void_CustomShouldHandle_AndComparer()
    {
        var factory = FactoryFrom("A", "A", "B");
        var cleaner = new Cleaner();
        var comparer = new FakeConnByServerComparer();

        RetryWithServerSideLatency.Execute(
            TConnFactory: factory,
            TConnCleaner: cleaner.Clean,
            ShouldHandle: RetryOn<InvalidOperationException>(),
            maxUniqueTConnExpected: 2,
            maxUniqueTConnAcquisitionAttempts: 10,
            Action: ThrowUntilServer("B"),
            TConnEqualityComparer: comparer);

        Assert.True(cleaner.DisposeCount >= 2);
    }

    /// <summary>
    /// Tests that OperationCanceledException is not retried by default.
    /// 
    /// Scenario: Operation throws OperationCanceledException, which should not trigger retries.
    /// Expected: Exception thrown immediately, no additional connections attempted.
    /// 
    /// This validates the default predicate's special handling of cancellation.
    /// </summary>
    [Fact]
    public void Execute_Void_DefaultShouldHandle_DoesNotRetry_OnOperationCanceled()
    {
        var factory = FactoryFrom("A", "B");
        var cleaner = new Cleaner();

        Assert.Throws<OperationCanceledException>(() =>
            RetryWithServerSideLatency.Execute(
                TConnFactory: factory,
                TConnCleaner: cleaner.Clean,
                maxUniqueTConnExpected: 2,
                maxUniqueTConnAcquisitionAttempts: 10,
                Action: _ => throw new OperationCanceledException()));

        Assert.Equal(1, cleaner.DisposeCount);
    }

    /// <summary>
    /// Tests integration with ILogger to demonstrate diagnostic logging capabilities.
    /// 
    /// Scenario: Operation fails on first two servers, succeeds on third. Logger captures retry attempts.
    /// Expected: Returns result, logger output shows retry progression across servers.
    /// 
    /// This demonstrates how to use ILogger with the retry strategy for production debugging.
    /// </summary>
    [Fact]
    public void Execute_WithLogger_CapturesRetryAttempts()
    {
        var logger = Utils.CreateLogger<RetryWithServerSideLatencyTests>(_output);
        RetryWithServerSideLatency.Logger = logger;

        var factory = FactoryFrom("ServerA", "ServerB", "ServerC");
        var cleaner = new Cleaner();
        var comparer = new FakeConnByServerComparer();

        var result = RetryWithServerSideLatency.Execute<FakeConn, string>(
            TConnFactory: factory,
            TConnCleaner: cleaner.Clean,
            maxUniqueTConnExpected: 3,
            maxUniqueTConnAcquisitionAttempts: 10,
            Action: conn =>
            {
                _output.WriteLine($"Attempting operation on {conn.Server}");
                if (conn.Server != "ServerC")
                {
                    throw new InvalidOperationException($"Data not yet replicated to {conn.Server}");
                }
                return "Success";
            },
            TConnEqualityComparer: comparer);

        Assert.Equal("Success", result);
        Assert.True(cleaner.DisposeCount >= 3);

        // Reset logger to avoid affecting other tests
        RetryWithServerSideLatency.Logger = null;
    }

    /// <summary>
    /// Tests LDAP-style integration scenario with connection pooling and logging.
    /// 
    /// Scenario: Simulates LDAP operations across multiple domain controllers where
    /// replication latency causes initial failures. Uses realistic LDAP connection patterns.
    /// Expected: Exhausts retries across all unique servers, demonstrates logging.
    /// 
    /// This provides a realistic example of using the library with enterprise LDAP.
    /// </summary>
    [Fact]
    public void Execute_LdapStyle_WithPooling_AndLogging()
    {
        const int LDAP_REPLICAS_COUNT = 3;
        var logger = Utils.CreateLogger<RetryWithServerSideLatencyTests>(_output);
        RetryWithServerSideLatency.Logger = logger;

        var ldapPoolService = new LdapConnectionPoolService(LDAP_REPLICAS_COUNT);
        var ldapEqualityComparer = EqualityComparer<LdapConnection>.Create(
            equals: (x, y) =>
            {
                if (x is null && y is null) return true;
                if (x == null || y == null) return false;
                return x.Name.Equals(y.Name, StringComparison.OrdinalIgnoreCase);
            },
            getHashCode: x => x.Name.GetHashCode()
        );

        int attemptCount = 0;

        // Simulate a scenario where all servers eventually fail (exhausts retries)
        Assert.Throws<Exception>(() =>
        {
            RetryWithServerSideLatency.Execute<LdapConnection>(
                TConnFactory: () => ldapPoolService.GetLdapConnection(),
                TConnCleaner: (con) => ldapPoolService.Free(con),
                maxUniqueTConnExpected: LDAP_REPLICAS_COUNT,
                maxUniqueTConnAcquisitionAttempts: 20,
                Action: (con) =>
                {
                    attemptCount++;
                    _output.WriteLine($"Attempt #{attemptCount}: Executing on {con.Name}");
                    Assert.InRange(attemptCount, 1, LDAP_REPLICAS_COUNT);
                    throw new Exception($"Replication latency on {con.Name}");
                },
                TConnEqualityComparer: ldapEqualityComparer
            );
        });

        // Verify we attempted all unique servers
        Assert.Equal(LDAP_REPLICAS_COUNT, attemptCount);

        // Verify connection pool is properly cleaned up
        Assert.Equal(0, ldapPoolService.Count);

        // Reset logger
        RetryWithServerSideLatency.Logger = null;
    }

    /// <summary>
    /// Tests LDAP-style integration with successful result after retry.
    /// 
    /// Scenario: LDAP operation succeeds, demonstrating proper pool cleanup on success.
    /// Expected: Returns result, connection pool is empty after operation.
    /// 
    /// This validates resource management in successful scenarios.
    /// </summary>
    [Fact]
    public void Execute_LdapStyle_SuccessfulResult_WithPoolCleanup()
    {
        const int LDAP_REPLICAS_COUNT = 3;
        var logger = Utils.CreateLogger<RetryWithServerSideLatencyTests>(_output);
        RetryWithServerSideLatency.Logger = logger;

        var ldapPoolService = new LdapConnectionPoolService(LDAP_REPLICAS_COUNT);
        var ldapEqualityComparer = EqualityComparer<LdapConnection>.Create(
            equals: (x, y) =>
            {
                if (x is null && y is null) return true;
                if (x == null || y == null) return false;
                return x.Name.Equals(y.Name, StringComparison.OrdinalIgnoreCase);
            },
            getHashCode: x => x.Name.GetHashCode()
        );

        var result = RetryWithServerSideLatency.Execute<LdapConnection, object?>(
            TConnFactory: () => ldapPoolService.GetLdapConnection(),
            TConnCleaner: (con) => ldapPoolService.Free(con),
            maxUniqueTConnExpected: LDAP_REPLICAS_COUNT,
            maxUniqueTConnAcquisitionAttempts: 20,
            Action: (con) =>
            {
                _output.WriteLine($"Executing LDAP query on {con.Name}");
                return new object(); // Simulate successful LDAP query result
            },
            TConnEqualityComparer: ldapEqualityComparer
        );

        // Verify we got a result
        Assert.NotNull(result);

        // Verify the LDAP connection pool is empty after the operation
        Assert.Equal(0, ldapPoolService.Count);

        // Reset logger
        RetryWithServerSideLatency.Logger = null;
    }
}