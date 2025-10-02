using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chrysalit.Polly.Tests
{
    #region Fake LDAP services
    /// <summary>
    /// Fake LDAP connection object for testing purposes.
    /// 
    /// Simulates connections to different LDAP servers by randomly assigning
    /// server names based on the total number of servers in the replication topology.
    /// </summary>
    internal class LdapConnection : IDisposable
    {
        public LdapConnection(int ldap_servers_count)
        {
            Name = $"Server {Random.Shared.Next(1, ldap_servers_count + 1)}";
        }

        public string Name { get; set; }

        public void Dispose()
        { }

        public override string ToString()
        {
            return Name;
        }
    }

    /// <summary>
    /// Static factory for creating LDAP connections.
    /// 
    /// Provides a simple way to create new connection instances for testing.
    /// </summary>
    internal class LdapConnectionService
    {
        /// <summary>
        /// Creates a new LDAP connection to a randomly selected server.
        /// </summary>
        /// <param name="ldap_servers_count">Total number of servers in the topology.</param>
        /// <returns>A new LDAP connection instance.</returns>
        public static LdapConnection GetLdapConnection(int ldap_servers_count)
        {
            return new LdapConnection(ldap_servers_count);
        }
    }

    /// <summary>
    /// Fake LDAP connection pool service for testing connection management.
    /// 
    /// Simulates a connection pool that tracks active connections and ensures
    /// proper cleanup. Used to validate that the retry strategy correctly
    /// manages pooled resources.
    /// </summary>
    internal class LdapConnectionPoolService
    {
        private readonly int _ldap_servers_count;
        private readonly HashSet<LdapConnection> _inUse = [];

        internal LdapConnectionPoolService(int ldap_servers_count)
        {
            _ldap_servers_count = ldap_servers_count;
        }

        /// <summary>
        /// Gets a connection from the pool (creates new instance for testing).
        /// </summary>
        /// <returns>A new LDAP connection instance.</returns>
        internal LdapConnection GetLdapConnection()
        {
            var some = LdapConnectionService.GetLdapConnection(_ldap_servers_count);
            _inUse.Add(some);
            return some;
        }

        /// <summary>
        /// Returns a connection to the pool, marking it as available.
        /// </summary>
        /// <param name="someDisposable">The connection to return.</param>
        internal void Free(LdapConnection someDisposable)
        {
            _inUse.Remove(someDisposable);
            someDisposable.Dispose();
        }

        /// <summary>
        /// Gets the number of currently active connections in the pool.
        /// </summary>
        internal int Count => _inUse.Count;
    }
    #endregion
}
