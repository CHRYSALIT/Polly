using System.DirectoryServices.Protocols;

namespace Chrysalit.Polly;

/// <summary>
/// <see cref="LdapConnection"/> equality comparer using the LDAP RootDSE "dnsHostName" attribute.
/// </summary>
public class LdapConnectionEqualityComparer
{
    /// <summary>
    /// Provide the LDAP attribute value to use for comparaison for a <see cref="LdapConnection"/>.
    /// This avoid multiple LDAP queries to the same server for the same attribute.
    /// </summary>
    protected Dictionary<LdapConnection, string> ConnectionAttributeMap { get; } = new();

    /// <summary>
    /// LDAP attribute to use for comparaison.
    /// </summary>
    protected string Attribute { get; private set; } = "dnsHostName";

    /// <summary>
    /// Compare <see cref="LdapConnection"/> instances using the LDAP RootDSE "dnsHostName" attribute value.
    /// </summary>
    public LdapConnectionEqualityComparer() { }

    /// <summary>
    /// Compare <see cref="LdapConnection"/> instances using the LDAP RootDSE <paramref name="attributeName"/> attribute value
    /// </summary>
    public LdapConnectionEqualityComparer(string attributeName)
    {
        Attribute = attributeName;
    }

    public bool Equals(LdapConnection? x, LdapConnection? y)
    {
        if (x == null && y == null) return true;
        if (x == null || y == null) return false;

        if (!ConnectionAttributeMap.TryGetValue(x, out string? xvalue))
        {
            xvalue = GetDnsHostName(x);
            ConnectionAttributeMap.Add(x, xvalue);
        }
        if (!ConnectionAttributeMap.TryGetValue(y, out string? yvalue))
        {
            yvalue = GetDnsHostName(y);
            ConnectionAttributeMap.Add(y, yvalue);
        }
        return xvalue.Equals(yvalue, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(LdapConnection obj)
    {
        if (!ConnectionAttributeMap.TryGetValue(obj, out string? value))
        {
            value = GetDnsHostName(obj);
            ConnectionAttributeMap.Add(obj, value);
        }
        return value.GetHashCode();
    }

    /// <summary>
    /// Obtain the DNS host name of the connected LDAP server using the RootDSE "dnsHostName" attribute.
    /// Can be overridden in derived classes to implement a different strategy.
    /// </summary>
    /// <param name="ldapConnection"></param>
    /// <returns>Should return a unique identifier for the server.</returns>
    public virtual string GetDnsHostName(LdapConnection ldapConnection)
    {
        try
        {
            var request = new SearchRequest(null, "(objectClass=*)", SearchScope.Base, this.Attribute);
            var response = (SearchResponse)ldapConnection.SendRequest(request);
            var connectedServer = ((string[])response.Entries[0].Attributes[this.Attribute].GetValues(typeof(string)))[0];
            return connectedServer;
        }
        catch
        {
            throw;
        }
    }
}
