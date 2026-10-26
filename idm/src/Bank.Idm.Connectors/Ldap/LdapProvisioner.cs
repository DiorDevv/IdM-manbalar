using System.DirectoryServices.Protocols;

namespace Bank.Idm.Connectors.Ldap;

/// <summary>
/// Active Directory / OpenLDAP'ga yozish. Obyektlar o'zgarmas identifikator (objectGUID, entryUUID) bo'yicha faqat UsersDn va GroupsDn
/// ichida qidiriladi — boshqa OU'dagi obyektga yozib bo'lmaydi. Har bir amal oldin joriy holatni tekshiradi (idempotent).
/// Bitta ulanish butun amallar to'plami uchun; Dispose bilan yopiladi.
/// </summary>
public sealed class LdapProvisioner(LdapConnectorOptions options) : IProvisioningConnector
{
    private const int AccountDisable = 2;
    private LdapConnection? _connection;

    private LdapConnection Connection => _connection ??= Open();

    private LdapConnection Open()
    {
        if (options.WriteBindDn is not null && options.WriteBindPassword is null)
            throw new ConnectorException("WriteBindDn ko'rsatilgan, lekin WriteBindPassword yo'q");
        return LdapSession.Open(options, options.WriteBindDn ?? options.BindDn, options.WriteBindPassword ?? options.BindPassword);
    }

    public void Dispose() => _connection?.Dispose();

    public Task<ExternalAccount?> FindByLoginAsync(string login, CancellationToken ct = default) =>
        FindAccountAsync($"({options.LoginAttribute}={LdapSession.EscapeFilter(login)})", ct);

    public Task<ExternalAccount?> FindByEmployeeNumberAsync(string employeeNumber, CancellationToken ct = default) =>
        FindAccountAsync($"({options.EmployeeNumberAttribute}={LdapSession.EscapeFilter(employeeNumber)})", ct);

    public async Task<ExternalAccount> CreateAccountAsync(NewAccount a, CancellationToken ct = default)
    {
        var parent = options.CreateDn ?? options.UsersDn;
        if (!IsWithin(parent, options.UsersDn))
            throw new ConnectorException($"CreateDn ({parent}) UsersDn ({options.UsersDn}) ichida emas");
        var dn = $"{options.RdnAttribute}={LdapSession.EscapeDnValue(a.Login)},{parent}";

        var attributes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { ["objectClass"] = options.EffectiveObjectClasses };
        void Set(string? attribute, string? value)
        {
            if (attribute is not null && !string.IsNullOrWhiteSpace(value)) attributes.TryAdd(attribute, [value.Trim()]);
        }
        Set(options.RdnAttribute, a.Login);
        Set(options.LoginAttribute, a.Login);
        Set("cn", a.DisplayName);
        Set(options.LastNameAttribute, a.LastName);
        Set(options.FirstNameAttribute, a.FirstName);
        Set(options.DisplayNameAttribute, a.DisplayName);
        Set(options.EmployeeNumberAttribute, a.EmployeeNumber);
        Set(options.EmailAttribute, a.Email);
        Set(options.DepartmentAttribute, a.Department);
        Set(options.JobTitleAttribute, a.JobTitle);
        if (!string.IsNullOrWhiteSpace(options.UpnSuffix)) Set("userPrincipalName", $"{a.Login}@{options.UpnSuffix}");
        // Bloklangan va parolsiz: kirish faqat helpdesk parol bergandan keyin (parol IdM'da hech qachon bo'lmaydi)
        if (options.DisableAttribute is { } disable) Set(disable, options.DisableValue);
        else Set("userAccountControl", (0x200 | AccountDisable).ToString());   // NORMAL_ACCOUNT | ACCOUNTDISABLE

        var request = new AddRequest(dn, attributes.Select(x => new DirectoryAttribute(x.Key, x.Value.Cast<object>().ToArray())).ToArray());
        await LdapSession.SendAsync<AddResponse>(Connection, request, $"yaratish {dn}");
        ct.ThrowIfCancellationRequested();
        return await ReadAccountAsync(dn, ct) ?? throw new ConnectorException($"{dn} yaratildi, lekin qayta o'qib bo'lmadi", transient: true);
    }

    public async Task<bool> DisableAccountAsync(string accountId, CancellationToken ct = default)
    {
        var attribute = options.DisableAttribute ?? "userAccountControl";
        var user = await FindByIdAsync(options.UsersDn, options.UserIdAttribute, accountId, [attribute], "akkaunt", ct);
        var current = Read(user, attribute);
        string value;
        if (options.DisableAttribute is null)
        {
            if (!int.TryParse(current, out var uac)) throw new ConnectorException($"{user.DistinguishedName}: userAccountControl o'qilmadi");
            if ((uac & AccountDisable) != 0) return false;
            value = (uac | AccountDisable).ToString();
        }
        else
        {
            if (string.Equals(current, options.DisableValue, StringComparison.OrdinalIgnoreCase)) return false;
            value = options.DisableValue;
        }
        await LdapSession.SendAsync<ModifyResponse>(Connection,
            new ModifyRequest(user.DistinguishedName, DirectoryAttributeOperation.Replace, attribute, value), $"bloklash {user.DistinguishedName}");
        return true;
    }

    public Task<bool> AddMemberAsync(string accountId, string groupId, CancellationToken ct = default) =>
        ChangeMemberAsync(accountId, groupId, add: true, ct);

    public Task<bool> RemoveMemberAsync(string accountId, string groupId, CancellationToken ct = default) =>
        ChangeMemberAsync(accountId, groupId, add: false, ct);

    private async Task<bool> ChangeMemberAsync(string accountId, string groupId, bool add, CancellationToken ct)
    {
        var user = await FindByIdAsync(options.UsersDn, options.UserIdAttribute, accountId, [options.LoginAttribute], "akkaunt", ct);
        var group = await FindByIdAsync(options.GroupsDn, options.GroupIdAttribute, groupId, ["1.1"], "guruh", ct);
        var member = options.GroupMemberIsDn ? user.DistinguishedName
            : Read(user, options.LoginAttribute) ?? throw new ConnectorException($"{user.DistinguishedName}: {options.LoginAttribute} yo'q");
        var check = new SearchRequest(group.DistinguishedName, $"({options.GroupMemberAttribute}={LdapSession.EscapeFilter(member)})", SearchScope.Base, "1.1");
        var isMember = (await LdapSession.SendAsync<SearchResponse>(Connection, check, group.DistinguishedName)).Entries.Count > 0;
        if (isMember == add) return false;
        ct.ThrowIfCancellationRequested();
        try
        {
            await LdapSession.SendAsync<ModifyResponse>(Connection, new ModifyRequest(group.DistinguishedName,
                add ? DirectoryAttributeOperation.Add : DirectoryAttributeOperation.Delete, options.GroupMemberAttribute, member),
                $"{(add ? "a'zo qo'shish" : "a'zolikdan chiqarish")} {group.DistinguishedName}");
            return true;
        }
        // Tekshiruv va yozish orasida boshqa joydan o'zgartirilgan: natija baribir kerakli holat
        catch (ConnectorException e) when (add ? e.ResultCode is (int)ResultCode.AttributeOrValueExists or (int)ResultCode.EntryAlreadyExists
                                               : e.ResultCode is (int)ResultCode.NoSuchAttribute)
        {
            return false;
        }
    }

    private async Task<SearchResultEntry> FindByIdAsync(string baseDn, string idAttribute, string id, string[] attributes, string what, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var filter = $"({idAttribute}={IdFilterValue(idAttribute, id)})";
        var response = await LdapSession.SendAsync<SearchResponse>(Connection, new SearchRequest(baseDn, filter, SearchScope.Subtree, attributes), $"{baseDn} {filter}");
        return response.Entries.Count switch
        {
            1 => response.Entries[0],
            0 => throw new ConnectorException($"{what} {id} {baseDn} ichida topilmadi (o'chirilgan yoki boshqa OU'ga ko'chirilgan)"),
            _ => throw new ConnectorException($"{what} {id}: {response.Entries.Count} ta obyekt topildi, identifikator noyob emas")
        };
    }

    private async Task<ExternalAccount?> FindAccountAsync(string filter, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var request = new SearchRequest(options.UsersDn, filter, SearchScope.Subtree, AccountAttributes);
        var response = await LdapSession.SendAsync<SearchResponse>(Connection, request, $"{options.UsersDn} {filter}");
        return response.Entries.Count == 0 ? null : ToAccount(response.Entries[0]);
    }

    private async Task<ExternalAccount?> ReadAccountAsync(string dn, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var response = await LdapSession.SendAsync<SearchResponse>(Connection, new SearchRequest(dn, "(objectClass=*)", SearchScope.Base, AccountAttributes), dn);
        return response.Entries.Count == 0 ? null : ToAccount(response.Entries[0]);
    }

    private string[] AccountAttributes =>
    [
        options.UserIdAttribute, options.LoginAttribute, options.EmployeeNumberAttribute, options.FirstNameAttribute, options.LastNameAttribute,
        options.DisplayNameAttribute, options.EmailAttribute, options.DepartmentAttribute, options.JobTitleAttribute, options.DisableAttribute ?? "userAccountControl"
    ];

    private ExternalAccount? ToAccount(SearchResultEntry entry)
    {
        var record = LdapRecord.Create(entry.DistinguishedName, entry.Attributes.AttributeNames.Cast<string>().Select(name =>
            new KeyValuePair<string, object[]>(name, entry.Attributes[name].GetValues(LdapSnapshotBuilder.IsBinary(name) ? typeof(byte[]) : typeof(string)))));
        var id = LdapSnapshotBuilder.ReadId(record, options.UserIdAttribute);
        var login = LdapSnapshotBuilder.ReadString(record, options.LoginAttribute);
        if (id is null || login is null) return null;
        var flag = LdapSnapshotBuilder.ReadString(record, options.DisableAttribute ?? "userAccountControl");
        var enabled = options.DisableAttribute is null
            ? !int.TryParse(flag, out var uac) || (uac & AccountDisable) == 0
            : !string.Equals(flag, options.DisableValue, StringComparison.OrdinalIgnoreCase);
        return new ExternalAccount(id, login, enabled,
            EmployeeNumber: LdapSnapshotBuilder.ReadString(record, options.EmployeeNumberAttribute),
            FirstName: LdapSnapshotBuilder.ReadString(record, options.FirstNameAttribute),
            LastName: LdapSnapshotBuilder.ReadString(record, options.LastNameAttribute),
            DisplayName: LdapSnapshotBuilder.ReadString(record, options.DisplayNameAttribute),
            Email: LdapSnapshotBuilder.ReadString(record, options.EmailAttribute),
            Department: LdapSnapshotBuilder.ReadString(record, options.DepartmentAttribute),
            JobTitle: LdapSnapshotBuilder.ReadString(record, options.JobTitleAttribute),
            DistinguishedName: entry.DistinguishedName);
    }

    /// <summary>objectGUID ikkilik qiymat sifatida qidiriladi (o'qishdagi GUID matnining teskari aylantirilishi), boshqalari matn.</summary>
    internal static string IdFilterValue(string attribute, string id)
    {
        if (!LdapSnapshotBuilder.IsBinary(attribute)) return LdapSession.EscapeFilter(id);
        return Guid.TryParse(id, out var guid) ? LdapSession.EscapeFilter(guid.ToByteArray())
            : throw new ConnectorException($"{attribute} qiymati GUID emas: {id}");
    }

    private static string? Read(SearchResultEntry entry, string attribute) =>
        entry.Attributes.Contains(attribute) ? entry.Attributes[attribute].GetValues(typeof(string)).OfType<string>().FirstOrDefault() : null;

    /// <summary>DN boshqa DN ichidami (katta-kichik harf va bo'shliqlarsiz solishtirish).</summary>
    internal static bool IsWithin(string dn, string root)
    {
        static string Normalize(string value) => string.Join(',', value.Split(',').Select(p => p.Trim())).ToLowerInvariant();
        var (d, r) = (Normalize(dn), Normalize(root));
        return d == r || d.EndsWith("," + r, StringComparison.Ordinal);
    }
}
