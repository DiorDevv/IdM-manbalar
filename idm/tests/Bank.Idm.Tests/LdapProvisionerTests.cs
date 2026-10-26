using System.DirectoryServices.Protocols;
using System.Net;
using Bank.Idm.Connectors;
using Bank.Idm.Connectors.Ldap;
using Microsoft.Extensions.Configuration;

namespace Bank.Idm.Tests;

public class LdapProvisionerUnitTests
{
    [Fact]
    public void ObjectGuid_is_searched_as_the_bytes_it_was_read_from()
    {
        var bytes = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var id = new Guid(bytes).ToString();   // LdapSnapshotBuilder o'qiganidek

        Assert.Equal(@"\00\01\02\03\04\05\06\07\08\09\0a\0b\0c\0d\0e\0f", LdapProvisioner.IdFilterValue("objectGUID", id));
        Assert.Throws<ConnectorException>(() => LdapProvisioner.IdFilterValue("objectGUID", "*"));
    }

    [Fact]
    public void Filter_and_dn_values_are_escaped()
    {
        Assert.Equal(@"a\2a\29\28uid=\2a\5c", LdapProvisioner.IdFilterValue("entryUUID", @"a*)(uid=*\"));
        Assert.Equal(@"\#a\,b\=c\+d\ ", LdapSession.EscapeDnValue("#a,b=c+d "));
    }

    [Fact]
    public void Object_classes_from_configuration_replace_the_ad_default_instead_of_merging_into_it()
    {
        static LdapConnectorOptions Bind(params (string Key, string Value)[] values) =>
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
                .Build().Get<LdapConnectorOptions>() ?? new LdapConnectorOptions();

        Assert.Equal(["inetOrgPerson"], Bind(("CreateObjectClasses:0", "inetOrgPerson")).EffectiveObjectClasses);
        Assert.Equal(LdapConnectorOptions.ActiveDirectoryObjectClasses, Bind(("Server", "dc1")).EffectiveObjectClasses);
    }

    [Theory]
    [InlineData("ou=new,ou=People,DC=bank,dc=local", "ou=people,dc=bank,dc=local", true)]
    [InlineData("ou=people, dc=bank, dc=local", "ou=people,dc=bank,dc=local", true)]
    [InlineData("ou=admins,dc=bank,dc=local", "ou=people,dc=bank,dc=local", false)]
    [InlineData("ou=xpeople,dc=bank,dc=local", "ou=people,dc=bank,dc=local", false)]
    public void Writes_are_confined_to_the_configured_subtree(string dn, string root, bool within)
    {
        Assert.Equal(within, LdapProvisioner.IsWithin(dn, root));
    }
}

/// <summary>
/// Haqiqiy OpenLDAP'ga yozish (sinov-ldap). Boshqa testlar o'qiydigan ou=people'ga tegmaslik uchun hamma narsa vaqtinchalik OU'da
/// yaratiladi va test oxirida o'chiriladi. Server ko'rsatilmasa o'tkazib yuboriladi (IDM_TEST_LDAP).
/// </summary>
public class LdapProvisionerLiveTests : IDisposable
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("IDM_TEST_LDAP");
    private const string Admin = "cn=admin,dc=bank,dc=local";
    private static string Password => Environment.GetEnvironmentVariable("IDM_TEST_LDAP_PASSWORD") ?? "TestAdmin123";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private readonly string _ou = $"ou=idm-test-{Guid.NewGuid():N},dc=bank,dc=local";
    private readonly List<string> _created = [];

    private LdapConnectorOptions Options()
    {
        var uri = new Uri(Server!);
        return new LdapConnectorOptions
        {
            Server = uri.Host, Port = uri.Port, UseSsl = uri.Scheme == "ldaps", BindDn = Admin, BindPassword = Password,
            UsersDn = _ou, UserFilter = "(objectClass=inetOrgPerson)", DisabledFilter = "(employeeType=disabled)",
            GroupsDn = _ou, GroupFilter = "(objectClass=posixGroup)",
            UserIdAttribute = "entryUUID", LoginAttribute = "uid", DepartmentAttribute = "departmentNumber", ManagerAttribute = null,
            GroupIdAttribute = "entryUUID", GroupMemberAttribute = "memberUid", GroupMemberIsDn = false,
            CreateObjectClasses = ["inetOrgPerson"], RdnAttribute = "uid", DisableAttribute = "employeeType"
        };
    }

    private LdapConnection Raw()
    {
        var uri = new Uri(Server!);
        var c = new LdapConnection(new LdapDirectoryIdentifier(uri.Host, uri.Port), new NetworkCredential(Admin, Password), AuthType.Basic);
        c.SessionOptions.ProtocolVersion = 3;
        c.Bind();
        return c;
    }

    private void Add(LdapConnection c, string dn, params (string Name, string Value)[] attributes)
    {
        var request = new AddRequest(dn, attributes.GroupBy(a => a.Name).Select(g => new DirectoryAttribute(g.Key, g.Select(a => (object)a.Value).ToArray())).ToArray());
        c.SendRequest(request);
        _created.Add(dn);
    }

    public void Dispose()
    {
        if (Server is null || _created.Count == 0) return;
        using var c = Raw();
        // Avval ichidagilar (test o'zi yaratganlar va konnektor yaratgan akkauntlar), keyin OU
        var children = ((SearchResponse)c.SendRequest(new SearchRequest(_ou, "(objectClass=*)", SearchScope.OneLevel, "1.1"))).Entries
            .Cast<SearchResultEntry>().Select(e => e.DistinguishedName).ToList();
        foreach (var dn in children) c.SendRequest(new DeleteRequest(dn));
        c.SendRequest(new DeleteRequest(_ou));
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Creates_disabled_account_manages_posix_group_membership_and_disables_idempotently()
    {
        Assert.SkipWhen(Server is null, "IDM_TEST_LDAP ko'rsatilmagan");
        using (var c = Raw())
        {
            Add(c, _ou, ("objectClass", "organizationalUnit"), ("ou", _ou.Split(',')[0][3..]));
            Add(c, $"cn=idm-test-group,{_ou}", ("objectClass", "posixGroup"), ("cn", "idm-test-group"), ("gidNumber", "19999"));
        }
        var options = Options();
        using var provisioner = new LdapProvisioner(options);
        var groupId = (await new LdapConnector(options).ReadAsync(Ct)).Groups.Single().ExternalId;

        // Yaratish: bloklangan, parolsiz, tabel raqami bilan
        Assert.Null(await provisioner.FindByEmployeeNumberAsync("TEST-1", Ct));
        var created = await provisioner.CreateAccountAsync(new NewAccount("t.testov", "Test", "Testov", "Testov Test", "TEST-1", "t.testov@bank.local", "IT", "Sinovchi"), Ct);
        Assert.True(Guid.TryParse(created.ExternalId, out _));
        Assert.Equal(("t.testov", false, "TEST-1", $"uid=t.testov,{_ou}"), (created.Login, created.IsEnabled, created.EmployeeNumber, created.DistinguishedName));
        Assert.Equal(created.ExternalId, (await provisioner.FindByLoginAsync("t.testov", Ct))?.ExternalId);
        Assert.Equal(created.ExternalId, (await provisioner.FindByEmployeeNumberAsync("TEST-1", Ct))?.ExternalId);
        var duplicate = await Assert.ThrowsAsync<ConnectorException>(() => provisioner.CreateAccountAsync(
            new NewAccount("t.testov", "Test", "Testov", "Testov Test", "TEST-2"), Ct));
        Assert.False(duplicate.IsTransient);

        // A'zolik: ikkinchi marta — o'zgarishsiz
        Assert.True(await provisioner.AddMemberAsync(created.ExternalId, groupId, Ct));
        Assert.False(await provisioner.AddMemberAsync(created.ExternalId, groupId, Ct));
        var snapshot = await new LdapConnector(options).ReadAsync(Ct);
        var account = snapshot.Accounts.Single();
        Assert.Equal((groupId, false, "IT"), (account.GroupIds.Single(), account.IsEnabled, account.Department));
        Assert.True(await provisioner.RemoveMemberAsync(created.ExternalId, groupId, Ct));
        Assert.False(await provisioner.RemoveMemberAsync(created.ExternalId, groupId, Ct));
        Assert.Empty((await new LdapConnector(options).ReadAsync(Ct)).Accounts.Single().GroupIds);

        // Bloklash: allaqachon bloklangan — false; faollashtirilgandan keyin — true va o'qishda bloklangan
        Assert.False(await provisioner.DisableAccountAsync(created.ExternalId, Ct));
        using (var c = Raw()) c.SendRequest(new ModifyRequest(created.DistinguishedName, DirectoryAttributeOperation.Delete, "employeeType"));
        Assert.True((await new LdapConnector(options).ReadAsync(Ct)).Accounts.Single().IsEnabled);
        Assert.True(await provisioner.DisableAccountAsync(created.ExternalId, Ct));
        Assert.False((await new LdapConnector(options).ReadAsync(Ct)).Accounts.Single().IsEnabled);

        // Qamrovdan tashqarida yoki mavjud bo'lmagan obyekt — doimiy xato
        var missing = await Assert.ThrowsAsync<ConnectorException>(() => provisioner.DisableAccountAsync(Guid.NewGuid().ToString(), Ct));
        Assert.Contains("topilmadi", missing.Message);
        Assert.False(missing.IsTransient);
        using var outside = new LdapProvisioner(new LdapConnectorOptions
        {
            Server = options.Server, Port = options.Port, UseSsl = false, BindDn = Admin, BindPassword = Password, UsersDn = _ou, GroupsDn = _ou,
            CreateDn = "ou=people,dc=bank,dc=local", CreateObjectClasses = ["inetOrgPerson"], RdnAttribute = "uid", LoginAttribute = "uid", DisableAttribute = "employeeType"
        });
        await Assert.ThrowsAsync<ConnectorException>(() => outside.CreateAccountAsync(new NewAccount("x.out", "X", "Out", "Out X", "TEST-3"), Ct));
    }

    [Fact]
    public async Task Unreachable_server_is_a_transient_error()
    {
        Assert.SkipWhen(Server is null, "IDM_TEST_LDAP ko'rsatilmagan");
        var options = Options();
        options.Port = 1;   // hech kim tinglamaydi
        options.TimeoutSeconds = 5;
        using var down = new LdapProvisioner(options);

        var e = await Assert.ThrowsAsync<ConnectorException>(() => down.FindByLoginAsync("a.karimov", Ct));
        Assert.True(e.IsTransient);
    }
}
