using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chrysalit.Polly.Tests
{
    #region Fake LDAP services
    /// <summary>
    /// fake LDAP connection object to simulate different LDAP servers.
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
    /// fake intermediary service to get LDAP connections.
    /// </summary>
    internal class LdapConnectionService
    {
        public static LdapConnection GetLdapConnection(int ldap_servers_count)
        {
            return new LdapConnection(ldap_servers_count);
        }
    }

    /// <summary>
    /// fake LDAP connection pool service to manage LDAP connections.
    /// </summary>
    internal class LdapConnectionPoolService
    {
        private readonly int _ldap_servers_count;
        private readonly HashSet<LdapConnection> _inUse = [];

        internal LdapConnectionPoolService(int ldap_servers_count)
        {
            _ldap_servers_count = ldap_servers_count;
        }

        internal LdapConnection GetLdapConnection()
        {
            var some = LdapConnectionService.GetLdapConnection(_ldap_servers_count);
            _inUse.Add(some);
            return some;
        }

        internal void Free(LdapConnection someDisposable)
        {
            _inUse.Remove(someDisposable);
            someDisposable.Dispose();
        }

        internal int Count => _inUse.Count;
    }
    #endregion
}
