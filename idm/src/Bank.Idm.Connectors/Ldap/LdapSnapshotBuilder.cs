using System.Text;

namespace Bank.Idm.Connectors.Ldap;

/// <summary>LDAP yozuvi: DN va atributlar (qiymat matn yoki ikkilik, masalan objectGUID).</summary>
public record LdapRecord(string Dn, IReadOnlyDictionary<string, object[]> Attributes)
{
    public static LdapRecord Create(string dn, IEnumerable<KeyValuePair<string, object[]>> attributes) =>
        new(dn, new Dictionary<string, object[]>(attributes, StringComparer.OrdinalIgnoreCase));
}

/// <summary>
/// LDAP yozuvlaridan konnektor holatini quradi: atributlarni xaritalaydi, bloklanganlarni belgilaydi,
/// guruh a'zoligini (guruh tomonidagi "member"/"memberUid") akkauntlarga o'tkazadi. Tarmoqsiz, shuning uchun to'liq test qilinadi.
/// </summary>
public static class LdapSnapshotBuilder
{
    public static ConnectorSnapshot Build(LdapConnectorOptions o, IReadOnlyList<LdapRecord> users, IReadOnlyCollection<string> disabledDns,
        IReadOnlyList<LdapRecord> groups)
    {
        var warnings = new List<string>();
        var disabled = new HashSet<string>(disabledDns, StringComparer.OrdinalIgnoreCase);
        var accounts = new List<(ExternalAccount Account, List<string> Groups)>();
        var byId = new HashSet<string>(StringComparer.Ordinal);
        var byDn = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var byLogin = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var u in users)
        {
            var id = ReadId(u, o.UserIdAttribute);
            var login = ReadString(u, o.LoginAttribute);
            if (id is null || login is null)
            {
                warnings.Add($"{u.Dn}: {(id is null ? o.UserIdAttribute : o.LoginAttribute)} yo'q, o'tkazib yuborildi");
                continue;
            }
            if (!byId.Add(id))
            {
                warnings.Add($"{u.Dn}: {id} identifikatori takrorlangan, o'tkazib yuborildi");
                continue;
            }
            var account = new ExternalAccount(id, login, !disabled.Contains(u.Dn),
                EmployeeNumber: ReadString(u, o.EmployeeNumberAttribute),
                FirstName: ReadString(u, o.FirstNameAttribute),
                LastName: ReadString(u, o.LastNameAttribute),
                DisplayName: ReadString(u, o.DisplayNameAttribute),
                Email: ReadString(u, o.EmailAttribute),
                Department: ReadString(u, o.DepartmentAttribute),
                JobTitle: ReadString(u, o.JobTitleAttribute),
                ManagerDn: o.ManagerAttribute is null ? null : ReadString(u, o.ManagerAttribute),
                DistinguishedName: u.Dn);
            byDn[u.Dn] = accounts.Count;
            byLogin.TryAdd(login, accounts.Count);
            accounts.Add((account, []));
        }

        var result = new List<ExternalGroup>();
        var groupIds = new HashSet<string>(StringComparer.Ordinal);
        var valid = new List<(LdapRecord Record, string Id)>();
        foreach (var g in groups)
        {
            var id = ReadId(g, o.GroupIdAttribute);
            var name = ReadString(g, o.GroupNameAttribute);
            if (id is null || name is null || !groupIds.Add(id))
            {
                warnings.Add($"{g.Dn}: guruh identifikatori yoki nomi yo'q yoki takrorlangan, o'tkazib yuborildi");
                continue;
            }
            result.Add(new ExternalGroup(id, name, ReadString(g, o.GroupDescriptionAttribute), g.Dn));
            valid.Add((g, id));
        }

        // Qamrovdan tashqaridagi a'zolar (boshqa OU) hisobga olinmaydi
        var groupByDn = valid.ToDictionary(v => v.Record.Dn, v => v.Id, StringComparer.OrdinalIgnoreCase);
        var nested = o.NestedGroups && o.GroupMemberIsDn;
        var parents = new Dictionary<string, List<string>>(StringComparer.Ordinal);   // ichki guruh → u a'zo bo'lgan guruhlar
        foreach (var (g, id) in valid)
            foreach (var member in ReadStrings(g, o.GroupMemberAttribute))
            {
                if ((o.GroupMemberIsDn ? byDn : byLogin).TryGetValue(member, out var index)) accounts[index].Groups.Add(id);
                else if (nested && groupByDn.TryGetValue(member, out var child) && child != id)
                    (parents.TryGetValue(child, out var list) ? list : parents[child] = []).Add(id);
            }

        return new ConnectorSnapshot(accounts.Select(a =>
        {
            var direct = a.Groups.Distinct().ToList();
            return a.Account with { GroupIds = direct, InheritedGroupIds = Inherited(direct, parents) };
        }).ToList(), result) { Warnings = warnings };
    }

    /// <summary>Bevosita guruhlardan yuqoriga (kenglik bo'yicha): har meros guruh uchun eng yaqin bevosita guruh. Halqalar xavfsiz.</summary>
    private static Dictionary<string, string> Inherited(List<string> direct, Dictionary<string, List<string>> parents)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parents.Count == 0) return result;
        var seen = new HashSet<string>(direct, StringComparer.Ordinal);
        var queue = new Queue<(string Group, string Via)>(direct.Select(d => (d, d)));
        while (queue.TryDequeue(out var current))
            foreach (var parent in parents.GetValueOrDefault(current.Group) ?? [])
                if (seen.Add(parent))
                {
                    result[parent] = current.Via;
                    queue.Enqueue((parent, current.Via));
                }
        return result;
    }

    /// <summary>Serverdan bayt sifatida o'qiladigan atributlar.</summary>
    public static bool IsBinary(string attribute) => attribute.Equals("objectGUID", StringComparison.OrdinalIgnoreCase);

    /// <summary>objectGUID (16 bayt) AD'dagi ko'rinishda GUID matniga, boshqa ikkilik qiymatlar hex'ga aylantiriladi.</summary>
    internal static string? ReadId(LdapRecord r, string attribute)
    {
        if (!r.Attributes.TryGetValue(attribute, out var values) || values.Length == 0) return null;
        return values[0] switch
        {
            byte[] { Length: 16 } bytes when IsBinary(attribute) => new Guid(bytes).ToString(),
            byte[] bytes => Convert.ToHexStringLower(bytes),
            var v => Clean(v.ToString())
        };
    }

    internal static string? ReadString(LdapRecord r, string attribute) => ReadStrings(r, attribute).FirstOrDefault();

    private static IEnumerable<string> ReadStrings(LdapRecord r, string attribute) =>
        r.Attributes.TryGetValue(attribute, out var values)
            ? values.Select(v => Clean(v is byte[] b ? Encoding.UTF8.GetString(b) : v.ToString())).OfType<string>()
            : [];

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
