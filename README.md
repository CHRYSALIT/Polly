# Chrysalit.Polly

This package provide a generic implementation of a `retry strategy` as offered with package such as [Polly](https://www.nuget.org/packages/Polly).


# Original Problem

After creating an object on a LDAP directory server, this object may not yet be replicated to all LDAP directory servers participating in the replication topology of the Naming Context.
Ideally the initial LDAP connection that created the object should be reused to ensure the following LDAP operations are performed on the server having the new object.
But sometimes the LDAP connection cannot be reused., and so building a new LDAP connection may access a server on which replication has not yet occured (matter of seconds or more, depending the replication topology).
So the LDAP operation may finally fail while it is certain the LDAP object exist, or will exist in a short amount of time.

We could delay the operation, but depending the caller requirements, waiting the object to be available may not be an option.

By using this advanced retry strategy, the required operation is attempted with on any LDAP connection at first, and then retried on any new server that may be reached.

This issue arise when using a generic DNS name, like 'contoso.corp', for which the DNS server may answer any LDAP server.


# Solution

This advanced retry strategy is not explicitely bounded to LDAP. It can be used for anything on which a replication delay may occurs on server side, meaning the operation attempted against a server may fail on a server but success on another.


# Installation 

## Nuget

The package is available on [Nuget](https://www.nuget.org/packages/Chrysalit.Polly/).

```csharp
dotnet add package Chrysalit.Polly
```

# Example

```csharp
RetryWithServerSideLatency.Execute<LdapConnection>(
  TConnFactory: () => new LdapConnection('contoso.corp') // how to build a new connection
  TConnCleaner: (con) => con.Dispose(), // how to clean a connection
  maxUniqueTConnExpected: 2, //2 servers in the LDAP replication 
  maxUniqueTConnAcquisitionAttempts: 20, // 20 attempts to dicover a new unique server for 'contoso.corp", 0 for infinite or until the number of max unique servers is discovered.
  Action: (con) => {
    var request = new ModifyRequest();
    con.SendRequest(request);
  },
  TConnEqualityComparer: new LdapConnectionEqualityComparer(); // use DnsHostName attribute on RootDSE to distinguishe unique LDAP servers.
);
```
