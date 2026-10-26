using Bank.Idm.Connectors;
using Bank.Idm.Connectors.Ldap;

namespace Bank.Idm.Tests;

/// <summary>
/// Haqiqiy LDAP serverga qarshi (sinov-ldap/bank-test-data.ldif yuklangan OpenLDAP). Server ko'rsatilmasa o'tkazib yuboriladi:
/// IDM_TEST_LDAP="ldap://127.0.0.1:3389" IDM_TEST_LDAP_PASSWORD=TestAdmin123 dotnet test
/// </summary>
public class LdapConnectorLiveTests
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("IDM_TEST_LDAP");

    private static LdapConnectorOptions OpenLdap(string? password = null)
    {
        var uri = new Uri(Server!);
        return new LdapConnectorOptions
        {
            Server = uri.Host, Port = uri.Port, UseSsl = uri.Scheme == "ldaps",
            BindDn = "cn=admin,dc=bank,dc=local", BindPassword = password ?? Environment.GetEnvironmentVariable("IDM_TEST_LDAP_PASSWORD") ?? "TestAdmin123",
            UsersDn = "ou=people,dc=bank,dc=local", UserFilter = "(objectClass=inetOrgPerson)", DisabledFilter = "(employeeType=disabled)",
            GroupsDn = "ou=groups,dc=bank,dc=local", GroupFilter = "(objectClass=posixGroup)",
            UserIdAttribute = "entryUUID", LoginAttribute = "uid", DepartmentAttribute = "departmentNumber",
            GroupIdAttribute = "entryUUID", GroupMemberAttribute = "memberUid", GroupMemberIsDn = false,
            PageSize = 3   // sahifalash haqiqatda ishlashi uchun kichik sahifa
        };
    }

    [Fact]
    public async Task Reads_test_directory_with_paging_and_posix_group_membership()
    {
        Assert.SkipWhen(Server is null, "IDM_TEST_LDAP ko'rsatilmagan");

        var snapshot = await new LdapConnector(OpenLdap()).ReadAsync(TestContext.Current.CancellationToken);

        Assert.Empty(snapshot.Warnings);
        // bank-test-data.ldif'dagi 8 xodim (IdM tizimga yozish yoqilgan stendda yangi akkauntlar ham qo'shilgan bo'lishi mumkin)
        Assert.Subset(snapshot.Accounts.Select(a => a.Login).ToHashSet(),
            new HashSet<string> { "a.karimov", "d.rahimova", "s.toshmatov", "n.yusupova", "j.aliyev", "m.qodirov", "z.ergasheva", "b.xolmatov" });
        Assert.Equal(4, snapshot.Groups.Count);
        var karimov = snapshot.Accounts.Single(a => a.Login == "a.karimov");
        Assert.Equal("TAB-10045", karimov.EmployeeNumber);
        Assert.Equal("Kredit bo'limi", karimov.Department);
        Assert.True(Guid.TryParse(karimov.ExternalId, out _));
        var groupNames = karimov.GroupIds.Select(id => snapshot.Groups.Single(g => g.ExternalId == id).Name).Order();
        Assert.Equal(["abs-operatorlar", "kredit-bolimi"], groupNames);
    }

    [Fact]
    public async Task Wrong_password_gives_a_readable_connector_error()
    {
        Assert.SkipWhen(Server is null, "IDM_TEST_LDAP ko'rsatilmagan");

        var e = await Assert.ThrowsAsync<ConnectorException>(() => new LdapConnector(OpenLdap("noto'g'ri")).ReadAsync(TestContext.Current.CancellationToken));
        Assert.Contains("ulanib bo'lmadi", e.Message);
    }
}
