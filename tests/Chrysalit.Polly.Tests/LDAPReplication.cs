namespace Chrysalit.Polly.Tests;

/// <summary>
/// This test class demonstrates an advanced retry strategy using the Polly library.
/// When using LDAP with a domain name 'contoso.corp', the DNS server can return multiple IP addresses for the same domain name.
/// 
/// When a client connects to one of these IP addresses, it may experience server-side latency due to replication delays.
/// When unable to perform the LDAP operation on the same connection to ensure the LDAP object exist, we can retry the operation until all servers are tried.
/// 
/// Admitting 3 servers participating in the replication for a naming context, we can retry 2 times = number of servers - 1).
/// Admitting 2 servers participating in the replication for a naming context, we can retry 1 times = number of servers - 1).
/// 
/// </summary>
public class LDAPReplication
{
    private const int LDAP_REPLICAS_COUNT = 3;

    readonly ITestOutputHelper _logger;
    readonly LdapConnectionPoolService _ldapPoolService = new(LDAP_REPLICAS_COUNT);
    readonly EqualityComparer<LdapConnection> _ldapEqualityComparer = EqualityComparer<LdapConnection>.Create(
        equals: (x, y) => {
            if (x is null && y is null) return true;
            if (x == null || y == null) return false;
            return x.Name.Equals(y.Name, StringComparison.OrdinalIgnoreCase);
        },
        getHashCode: x => x.Name.GetHashCode()
    );

    public LDAPReplication(ITestOutputHelper logger)
    {
        _logger = logger;
    }

    private T? GetOrThrow<T>(bool throwing, LdapConnection ldapConnection) where T : new()
    {
        _logger.WriteLine($"Executing with connection {ldapConnection.Name}");
        return throwing ? throw new Exception("exception") : new T();
    }

    /// <summary>
    /// Throws an exception after exhausting all LDAP connections.
    /// </summary>
    [Fact]
    public void GetException()
    {
        var logger = Utils.CreateLogger<LDAPReplication>(_logger);
        RetryWithServerSideLatency.Logger = logger;

        int tries = 0;
        var throwing = true;

        // ensure throwing after exhausting all LDAP connections
        Assert.Throws<Exception>(() =>
        {
            RetryWithServerSideLatency.Execute<LdapConnection>(
                TConnFactory: () => _ldapPoolService.GetLdapConnection(),
                TConnCleaner: (con) => _ldapPoolService.Free(con),
                maxUniqueTConnExpected: LDAP_REPLICAS_COUNT,
                maxUniqueTConnAcquisitionAttempts: 20,
                Action: (con) => {
                    tries++;
                    Assert.InRange(tries, 1, LDAP_REPLICAS_COUNT);
                    GetOrThrow<object>(throwing, con);
                },
                TConnEqualityComparer: _ldapEqualityComparer
            );
        });
    }

    /// <summary>
    /// Result is obtained before exhausting all LDAP connections.
    /// </summary>
    [Fact]
    public void GetResult()
    {
        var logger = Utils.CreateLogger<LDAPReplication>(_logger);
        RetryWithServerSideLatency.Logger = logger;

        var throwing = false;

        var result = RetryWithServerSideLatency.Execute<LdapConnection, object?>(
                TConnFactory: () => _ldapPoolService.GetLdapConnection(),
                TConnCleaner: (con) => _ldapPoolService.Free(con),
                maxUniqueTConnExpected: LDAP_REPLICAS_COUNT,
                maxUniqueTConnAcquisitionAttempts: 20,
                Action: (con) => GetOrThrow<object>(throwing, con),
                TConnEqualityComparer: _ldapEqualityComparer
        );

        // ensure we got a result
        Assert.NotNull(result);

        // ensure the LDAP connection pool is empty after the operation.
        Assert.Equal(0, _ldapPoolService.Count);
    }
}
