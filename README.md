# Chrysalit.Polly

This package provides a generic implementation of a retry strategy, built on top of [Polly](https://www.nuget.org/packages/Polly), that helps when the target system may require multiple attempts across different servers (e.g., due to replication lag).

## Original Problem

After creating an object on an LDAP directory server, the object may not have replicated to all servers participating in the same Naming Context. Ideally, the initial LDAP connection that created the object should be reused for subsequent operations. However, when that connection cannot be reused, a new LDAP connection may reach a server where the object has not yet replicated (seconds or more depending on topology). As a result, operations can fail even though the object exists (or will exist shortly).

Delaying the operation may not be acceptable depending on caller requirements.

By using this advanced retry strategy, the operation is attempted on an initial LDAP connection and then retried using new connections targeting different servers until success or until the configured limits are reached.

This situation commonly arises when using a generic DNS name such as "contoso.corp", where DNS may resolve to multiple LDAP servers.

## Solution

This retry strategy is not bound to LDAP. It can be used for any scenario where server-side replication latency exists and an operation may fail on one server but succeed on another.

## Mathematical principles

AI assisted in exploring some [mathematical principles](/MATH.md) related to the coupon collector/occupancy problem. Keep in mind that AI can make mistakes; no guarantees are provided regarding the quality of that content.

## Installation

### NuGet

The package is available on [NuGet](https://www.nuget.org/packages/Chrysalit.Polly/).

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
