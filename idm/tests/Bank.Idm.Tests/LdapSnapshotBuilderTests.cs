using Bank.Idm.Connectors.Ldap;

namespace Bank.Idm.Tests;

public class LdapSnapshotBuilderTests
{
    private static LdapRecord Rec(string dn, params (string Name, object[] Values)[] attributes) =>
        LdapRecord.Create(dn, attributes.Select(a => new KeyValuePair<string, object[]>(a.Name, a.Values)));

    [Fact]
    public void Active_directory_entries_are_mapped_with_guid_ids_disabled_flag_and_group_membership()
    {
        var karimovId = Guid.NewGuid();
        var rahimovaId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        const string karimovDn = "CN=Aziz Karimov,OU=Xodimlar,DC=bank,DC=test";
        const string rahimovaDn = "CN=Dilnoza Rahimova,OU=Xodimlar,DC=bank,DC=test";
        var users = new[]
        {
            Rec(karimovDn, ("objectGUID", [karimovId.ToByteArray()]), ("sAMAccountName", ["a.karimov"]), ("employeeNumber", ["TAB-10045"]),
                ("givenName", ["Aziz"]), ("sn", ["Karimov"]), ("department", ["Kredit bo'limi"]), ("manager", [rahimovaDn])),
            Rec(rahimovaDn, ("objectGUID", [rahimovaId.ToByteArray()]), ("SAMACCOUNTNAME", ["d.rahimova"])),
        };
        var groups = new[]
        {
            // AD DN'larni katta-kichik harfda farqli qaytarishi mumkin
            Rec("CN=kredit-bolimi,OU=Guruhlar,DC=bank,DC=test", ("objectGUID", [groupId.ToByteArray()]), ("cn", ["kredit-bolimi"]),
                ("member", [karimovDn.ToUpperInvariant(), rahimovaDn, "CN=Boshqa OU,DC=bank,DC=test"])),
        };

        var snapshot = LdapSnapshotBuilder.Build(new LdapConnectorOptions(), users, [rahimovaDn], groups);

        Assert.Empty(snapshot.Warnings);
        var karimov = snapshot.Accounts.Single(a => a.Login == "a.karimov");
        Assert.Equal(karimovId.ToString(), karimov.ExternalId);
        Assert.True(karimov.IsEnabled);
        Assert.Equal("TAB-10045", karimov.EmployeeNumber);
        Assert.Equal("Kredit bo'limi", karimov.Department);
        Assert.Equal(rahimovaDn, karimov.ManagerDn);
        Assert.Equal([groupId.ToString()], karimov.GroupIds);
        var rahimova = snapshot.Accounts.Single(a => a.Login == "d.rahimova");
        Assert.False(rahimova.IsEnabled);
        Assert.Equal([groupId.ToString()], rahimova.GroupIds);
        Assert.Equal("kredit-bolimi", Assert.Single(snapshot.Groups).Name);
    }

    [Fact]
    public void Nested_groups_give_inherited_membership_via_the_nearest_direct_group_and_cycles_are_safe()
    {
        Guid Id() => Guid.NewGuid();
        var (userId, tellers, branch, all, admins) = (Id(), Id(), Id(), Id(), Id());
        const string userDn = "CN=Aziz Karimov,OU=Xodimlar,DC=bank,DC=test";
        string Dn(string cn) => $"CN={cn},OU=Guruhlar,DC=bank,DC=test";
        LdapRecord Group(Guid id, string cn, params string[] members) => Rec(Dn(cn), ("objectGUID", [id.ToByteArray()]), ("cn", [cn]), ("member", members));
        var users = new[] { Rec(userDn, ("objectGUID", [userId.ToByteArray()]), ("sAMAccountName", ["a.karimov"])) };
        // kassirlar ⊂ filial ⊂ hammasi ⊂ filial (halqa); admins — bog'liq emas
        var groups = new[]
        {
            Group(tellers, "kassirlar", userDn),
            Group(branch, "filial", Dn("kassirlar").ToLowerInvariant(), Dn("hammasi")),
            Group(all, "hammasi", Dn("filial")),
            Group(admins, "Domain Admins"),
        };

        var account = Assert.Single(LdapSnapshotBuilder.Build(new LdapConnectorOptions(), users, [], groups).Accounts);

        Assert.Equal([tellers.ToString()], account.GroupIds);
        Assert.Equal(new Dictionary<string, string> { [branch.ToString()] = tellers.ToString(), [all.ToString()] = tellers.ToString() },
            account.InheritedGroupIds);

        // O'chirilgan bo'lsa — faqat bevosita
        var flat = Assert.Single(LdapSnapshotBuilder.Build(new LdapConnectorOptions { NestedGroups = false }, users, [], groups).Accounts);
        Assert.Empty(flat.InheritedGroupIds);
    }

    [Theory]
    [InlineData("dc1.bank.uz", "dc1.bank.uz", 636)]
    [InlineData("dc2.bank.uz:3269", "dc2.bank.uz", 3269)]
    [InlineData("10.0.0.5:389", "10.0.0.5", 389)]
    [InlineData("[fd00::5]:636", "fd00::5", 636)]
    [InlineData("fd00::5", "fd00::5", 636)]
    public void Domain_controller_list_entries_take_an_optional_port(string value, string host, int port)
    {
        Assert.Equal((host, port), LdapConnectorOptions.ParseEndpoint(value, 636));
    }

    [Fact]
    public void Servers_list_overrides_the_single_server_in_order()
    {
        Assert.Equal([("dc1", 636)], new LdapConnectorOptions { Server = "dc1" }.Endpoints);
        Assert.Equal([("dc2", 636), ("dc3", 3269)], new LdapConnectorOptions { Server = "dc1", Servers = ["dc2", " dc3:3269 ", ""] }.Endpoints);
        Assert.Throws<ArgumentException>(() => new LdapConnectorOptions { Servers = ["dc1:abc"] }.Endpoints);
    }

    [Fact]
    public void Posix_groups_resolve_members_by_login()
    {
        var o = new LdapConnectorOptions
        {
            UserIdAttribute = "uidNumber", LoginAttribute = "uid", GroupIdAttribute = "gidNumber",
            GroupMemberAttribute = "memberUid", GroupMemberIsDn = false
        };
        var users = new[] { Rec("uid=a.karimov,ou=people,dc=bank,dc=local", ("uidNumber", ["10045"]), ("uid", ["a.karimov"])) };
        var groups = new[] { Rec("cn=kredit,ou=groups,dc=bank,dc=local", ("gidNumber", ["5001"]), ("cn", ["kredit"]), ("memberUid", ["A.Karimov", "yoq"])) };

        var snapshot = LdapSnapshotBuilder.Build(o, users, [], groups);

        Assert.Equal(["5001"], Assert.Single(snapshot.Accounts).GroupIds);
    }

    [Fact]
    public void Entries_without_id_or_login_and_duplicates_are_skipped_with_a_warning()
    {
        var id = Guid.NewGuid().ToByteArray();
        var users = new[]
        {
            Rec("CN=a", ("objectGUID", [id]), ("sAMAccountName", ["a"])),
            Rec("CN=b", ("objectGUID", [id]), ("sAMAccountName", ["b"])),
            Rec("CN=c", ("sAMAccountName", ["c"])),
            Rec("CN=d", ("objectGUID", [Guid.NewGuid().ToByteArray()]), ("sAMAccountName", ["  "])),
        };

        var snapshot = LdapSnapshotBuilder.Build(new LdapConnectorOptions(), users, [], []);

        Assert.Equal("a", Assert.Single(snapshot.Accounts).Login);
        Assert.Equal(3, snapshot.Warnings.Count);
    }

    [Theory]
    [InlineData("member;range=0-1499", 1500)]
    [InlineData("member;range=1500-2999", 3000)]
    [InlineData("member;range=3000-*", null)]
    public void Ranged_attribute_gives_the_next_chunk_start(string attribute, int? expected) =>
        Assert.Equal(expected, LdapConnector.NextRangeStart(attribute, "member"));
}
