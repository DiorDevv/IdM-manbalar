using System.DirectoryServices.Protocols;
using System.Net;
using Bank.Idm.Connectors;
using Bank.Idm.Connectors.Ldap;
using Bank.Idm.Domain;
using Bank.Idm.Infrastructure;
using Bank.Idm.Infrastructure.Provisioning;
using Bank.Idm.Infrastructure.Sync;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Tests;

/// <summary>
/// Haqiqiy Active Directory (Samba AD DC, sinov-ad/install-ad.sh ma'lumotlari: domen BANK.TEST, OU=Xodimlar — 8 xodim, OU=Guruhlar)
/// ga qarshi. Faqat LDAPS; sertifikat LDAPTLS_CACERT bilan tekshiriladi. Ko'rsatilmasa o'tkazib yuboriladi:
/// IDM_TEST_AD=ldaps://127.0.0.1:636 IDM_TEST_AD_PASSWORD=... (svc-idm) IDM_TEST_AD_WRITE_DN=... IDM_TEST_AD_WRITE_PASSWORD=... (svc-idm-write)
/// IDM_TEST_AD_ADMIN_DN=... IDM_TEST_AD_ADMIN_PASSWORD=... (helpdesk amallari va tozalash) dotnet test
/// Qo'shimcha: it-adminlar ⊂ server-adminlar (ichma-ich), OU=Ommaviy — 1600 akkaunt va ularning katta-guruh'i.
/// </summary>
public class AdLiveTests
{
    private static readonly string? Server = Environment.GetEnvironmentVariable("IDM_TEST_AD");
    private const string Base = "DC=bank,DC=test";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LdapConnectorOptions Ad(string? usersDn = null, string? password = null)
    {
        var uri = new Uri(Server!);
        // Standart qiymatlar — Active Directory uchun (objectGUID, sAMAccountName, member, userAccountControl)
        return new LdapConnectorOptions
        {
            Server = uri.Host, Port = uri.Port, UseSsl = true,
            BindDn = Environment.GetEnvironmentVariable("IDM_TEST_AD_BINDDN") ?? $"CN=svc-idm,CN=Users,{Base}",
            BindPassword = password ?? Environment.GetEnvironmentVariable("IDM_TEST_AD_PASSWORD") ?? "",
            UsersDn = usersDn ?? $"OU=Xodimlar,{Base}", GroupsDn = $"OU=Guruhlar,{Base}",
            WriteBindDn = Environment.GetEnvironmentVariable("IDM_TEST_AD_WRITE_DN"),
            WriteBindPassword = Environment.GetEnvironmentVariable("IDM_TEST_AD_WRITE_PASSWORD"),
            PageSize = 500,
        };
    }

    [Fact]
    public async Task Reads_ad_with_object_guids_disabled_flag_employee_numbers_and_nested_groups()
    {
        Assert.SkipWhen(Server is null, "IDM_TEST_AD ko'rsatilmagan");

        var snapshot = await new LdapConnector(Ad()).ReadAsync(Ct);

        Assert.Empty(snapshot.Warnings);
        Assert.Subset(snapshot.Accounts.Select(a => a.Login).ToHashSet(),
            new HashSet<string> { "a.karimov", "d.rahimova", "s.toshmatov", "n.yusupova", "j.aliyev", "m.qodirov", "z.ergasheva", "b.xolmatov" });
        Assert.All(snapshot.Accounts, a => Assert.True(Guid.TryParse(a.ExternalId, out _)));
        string Group(string id) => snapshot.Groups.Single(g => g.ExternalId == id).Name;

        var karimov = snapshot.Accounts.Single(a => a.Login == "a.karimov");
        Assert.Equal(("TAB-10045", "Kredit bo'limi", "Aziz", "Karimov", true), (karimov.EmployeeNumber, karimov.Department, karimov.FirstName, karimov.LastName, karimov.IsEnabled));
        Assert.Equal(["abs-operatorlar", "kredit-bolimi"], karimov.GroupIds.Select(Group).Order());
        Assert.False(snapshot.Accounts.Single(a => a.Login == "n.yusupova").IsEnabled);   // userAccountControl ACCOUNTDISABLE

        // Ichma-ich: Aliyev bevosita it-adminlar'da, u orqali server-adminlar'da
        var aliyev = snapshot.Accounts.Single(a => a.Login == "j.aliyev");
        Assert.Equal(["it-adminlar"], aliyev.GroupIds.Select(Group));
        var (inherited, via) = Assert.Single(aliyev.InheritedGroupIds);
        Assert.Equal(("server-adminlar", "it-adminlar"), (Group(inherited), Group(via)));
    }

    [Fact]
    public async Task Pages_through_1600_accounts_and_a_1600_member_group()
    {
        Assert.SkipWhen(Server is null, "IDM_TEST_AD ko'rsatilmagan");

        var snapshot = await new LdapConnector(Ad(usersDn: $"OU=Ommaviy,{Base}")).ReadAsync(Ct);

        Assert.Equal(1600, snapshot.Accounts.Count);
        var big = snapshot.Groups.Single(g => g.Name == "katta-guruh").ExternalId;
        Assert.All(snapshot.Accounts, a => Assert.Contains(big, a.GroupIds));
    }

    [Fact]
    public async Task Wrong_password_is_a_permanent_error_and_plain_ldap_bind_is_refused()
    {
        Assert.SkipWhen(Server is null, "IDM_TEST_AD ko'rsatilmagan");

        var wrong = await Assert.ThrowsAsync<ConnectorException>(() => new LdapConnector(Ad(password: "noto'g'ri")).ReadAsync(Ct));
        Assert.False(wrong.IsTransient);

        // Bankdagi kabi: 389'da shifrlanmagan simple bind rad etiladi
        var plain = Ad();
        (plain.Port, plain.UseSsl) = (389, false);
        var refused = await Assert.ThrowsAsync<ConnectorException>(() => new LdapConnector(plain).ReadAsync(Ct));
        Assert.Contains("ulanib bo'lmadi", refused.Message);
    }

    [Fact]
    public async Task Writes_to_ad_create_disabled_with_an_unknown_password_membership_disable_and_enable()
    {
        Assert.SkipWhen(Server is null || Environment.GetEnvironmentVariable("IDM_TEST_AD_WRITE_DN") is null, "IDM_TEST_AD_WRITE_DN ko'rsatilmagan");
        var options = Ad();
        options.UpnSuffix = "bank.test";
        var login = $"t.ad{Random.Shared.Next(1000, 9999)}";
        using var provisioner = new LdapProvisioner(options);
        var groupId = (await new LdapConnector(options).ReadAsync(Ct)).Groups.Single(g => g.Name == "kredit-bolimi").ExternalId;
        ExternalAccount? created = null;
        try
        {
            created = await provisioner.CreateAccountAsync(new NewAccount(login, "Test", "Adov", "Adov Test", $"TEST-{login}", $"{login}@bank.test", "IT", "Sinovchi"), Ct);
            Assert.True(Guid.TryParse(created.ExternalId, out _));
            Assert.False(created.IsEnabled);   // bloklangan yaratiladi
            Assert.Equal(created.ExternalId, (await provisioner.FindByEmployeeNumberAsync($"TEST-{login}", Ct))?.ExternalId);

            Assert.True(await provisioner.AddMemberAsync(created.ExternalId, groupId, Ct));
            Assert.False(await provisioner.AddMemberAsync(created.ExternalId, groupId, Ct));
            Assert.Contains(groupId, (await new LdapConnector(options).ReadAsync(Ct)).Accounts.Single(a => a.Login == login).GroupIds);
            Assert.True(await provisioner.RemoveMemberAsync(created.ExternalId, groupId, Ct));
            Assert.False(await provisioner.DisableAccountAsync(created.ExternalId, Ct));   // allaqachon bloklangan

            // Yaratishda tasodifiy parol qo'yilgan (pwdLastSet != 0): yoqilsa ham bo'sh parol bilan qolmaydi
            Assert.NotEqual("0", ReadAttribute(created.DistinguishedName!, "pwdLastSet"));
            Assert.True(await provisioner.EnableAccountAsync(created.ExternalId, Ct));
            // Eng kam huquq: yozish akkaunti parolni almashtira olmaydi (bu — helpdesk ishi)
            Assert.ThrowsAny<DirectoryOperationException>(() =>
            {
                using var w = WriteConnection();
                w.SendRequest(new ModifyRequest(created.DistinguishedName, DirectoryAttributeOperation.Replace, "unicodePwd",
                    System.Text.Encoding.Unicode.GetBytes("\"Buzg'unchi-Parol-1!\"")));
            });
            // Helpdesk parol bergach xodim kira oladi
            SetPassword(created.DistinguishedName!, "Yangi-Parol-2026!");
            using (var user = Connect($"{login}@bank.test", "Yangi-Parol-2026!")) { }
            Assert.False(await provisioner.EnableAccountAsync(created.ExternalId, Ct));
            Assert.True((await new LdapConnector(options).ReadAsync(Ct)).Accounts.Single(a => a.Login == login).IsEnabled);
            Assert.True(await provisioner.DisableAccountAsync(created.ExternalId, Ct));
        }
        finally
        {
            if (created?.DistinguishedName is { } dn) using (var c = AdminConnection()) c.SendRequest(new DeleteRequest(dn));
        }
    }

    [Fact]
    public async Task End_to_end_leaver_is_disabled_and_removed_from_groups_in_ad_and_the_next_sync_confirms_it()
    {
        Assert.SkipWhen(Server is null || Environment.GetEnvironmentVariable("IDM_TEST_AD_WRITE_DN") is null, "IDM_TEST_AD_WRITE_DN ko'rsatilmagan");
        var options = Ad();
        options.UpnSuffix = "bank.test";
        var login = $"t.leaver{Random.Shared.Next(1000, 9999)}";
        await using var pg = new PostgresFixture();
        await pg.InitializeAsync();
        ExternalAccount? created = null;
        try
        {
            // AD'da ishlayotgan xodim: kredit-bolimi va it-adminlar (u orqali server-adminlar — meros)
            using (var provisioner = new LdapProvisioner(options))
            {
                created = await provisioner.CreateAccountAsync(new NewAccount(login, "Test", "Ketuvchi", "Ketuvchi Test", $"TEST-{login}"), Ct);
                var groups = (await new LdapConnector(options).ReadAsync(Ct)).Groups.ToDictionary(g => g.Name, g => g.ExternalId);
                await provisioner.EnableAccountAsync(created.ExternalId, Ct);
                await provisioner.AddMemberAsync(created.ExternalId, groups["kredit-bolimi"], Ct);
                await provisioner.AddMemberAsync(created.ExternalId, groups["it-adminlar"], Ct);
            }
            Guid appId;
            await using (var db = pg.CreateContext())
            {
                var app = new Application { Code = "AD", Name = "Active Directory", Kind = ApplicationKind.Directory };
                db.Applications.Add(app);
                await db.SaveChangesAsync(Ct);
                appId = app.Id;
            }
            async Task<ConnectorRun> SyncAsync()
            {
                await using var db = pg.CreateContext();
                var sync = new ConnectorSync(db, new AuditLog(db, TimeProvider.System), TimeProvider.System);
                var run = (await sync.StartRunAsync(appId, ConnectorRunTrigger.Manual, "test", ct: Ct))!;
                return await sync.ExecuteAsync(run.Id, new LdapConnector(options), new SyncSettings(MaxDeletePercent: 100), "test", false, Ct);
            }

            Assert.Equal(ConnectorRunStatus.Succeeded, (await SyncAsync()).Status);
            Guid accountId;
            await using (var db = pg.CreateContext())
            {
                var account = await db.Accounts.Include(a => a.Identity).Include(a => a.Entitlements).ThenInclude(e => e.Entitlement).SingleAsync(a => a.Login == login, Ct);
                accountId = account.Id;
                Assert.Equal(AccountStatus.Enabled, account.Status);
                var byName = account.Entitlements.ToDictionary(e => e.Entitlement.Name);
                Assert.Equal(["it-adminlar", "kredit-bolimi", "server-adminlar"], byName.Keys.Order());
                Assert.Equal(byName["it-adminlar"].EntitlementId, byName["server-adminlar"].ViaEntitlementId);
                // HR: ishdan ketdi
                account.Identity!.Status = IdentityStatus.Terminated;
                await db.SaveChangesAsync(Ct);
            }

            var settings = new ProvisioningSettings(ProvisioningMode.Automatic);
            await using (var db = pg.CreateContext())
            {
                var plan = await new ProvisioningPlanner(db, new AuditLog(db, TimeProvider.System), TimeProvider.System).PlanAsync(appId, settings, "test", null, Ct);
                Assert.Equal(3, plan.Created);   // bloklash + 2 ta bevosita guruhdan chiqarish (meros — o'zi yo'qoladi)
            }
            await using (var db = pg.CreateContext())
            using (var provisioner = new LdapProvisioner(options))
            {
                var result = await new ProvisioningExecutor(db, new AuditLog(db, TimeProvider.System), TimeProvider.System).ExecuteDueAsync(appId, provisioner, settings, ct: Ct);
                Assert.Equal(new ExecuteResult(3, 0, 0, 0), result);
            }

            Assert.Equal(ConnectorRunStatus.Succeeded, (await SyncAsync()).Status);
            await using (var db = pg.CreateContext())
            {
                var account = await db.Accounts.Include(a => a.Entitlements).SingleAsync(a => a.Id == accountId, Ct);
                Assert.Equal(AccountStatus.Disabled, account.Status);
                Assert.Empty(account.Entitlements);   // meros server-adminlar ham yo'qoldi
                Assert.True((await new AuditLog(db, TimeProvider.System).VerifyChainAsync(Ct)).IsValid);
            }
        }
        finally
        {
            if (created?.DistinguishedName is { } dn) using (var c = AdminConnection()) c.SendRequest(new DeleteRequest(dn));
        }
    }

    private static LdapConnection WriteConnection() =>
        Connect(Environment.GetEnvironmentVariable("IDM_TEST_AD_WRITE_DN")!, Environment.GetEnvironmentVariable("IDM_TEST_AD_WRITE_PASSWORD")!);

    /// <summary>Helpdesk (parol berish) va test tozalashi uchun; berilmasa — yozish akkaunti.</summary>
    private static LdapConnection AdminConnection() => Environment.GetEnvironmentVariable("IDM_TEST_AD_ADMIN_DN") is { } dn
        ? Connect(dn, Environment.GetEnvironmentVariable("IDM_TEST_AD_ADMIN_PASSWORD")!)
        : WriteConnection();

    private static string? ReadAttribute(string dn, string attribute)
    {
        using var c = AdminConnection();
        var r = (SearchResponse)c.SendRequest(new SearchRequest(dn, "(objectClass=*)", SearchScope.Base, attribute));
        return r.Entries[0].Attributes.Contains(attribute) ? r.Entries[0].Attributes[attribute].GetValues(typeof(string)).OfType<string>().FirstOrDefault() : null;
    }

    private static LdapConnection Connect(string user, string password)
    {
        var uri = new Uri(Server!);
        var c = new LdapConnection(new LdapDirectoryIdentifier(uri.Host, uri.Port), new NetworkCredential(user, password), AuthType.Basic);
        c.SessionOptions.ProtocolVersion = 3;
        c.SessionOptions.SecureSocketLayer = true;
        c.Bind();
        return c;
    }

    /// <summary>AD paroli: unicodePwd = "parol" qo'shtirnoqda, UTF-16LE (faqat LDAPS orqali).</summary>
    private static void SetPassword(string dn, string password)
    {
        using var c = AdminConnection();
        c.SendRequest(new ModifyRequest(dn, DirectoryAttributeOperation.Replace, "unicodePwd", System.Text.Encoding.Unicode.GetBytes($"\"{password}\"")));
    }
}
