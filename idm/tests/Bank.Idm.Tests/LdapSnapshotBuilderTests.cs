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
