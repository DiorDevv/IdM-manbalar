using System.DirectoryServices.Protocols;
using System.Net;

namespace Bank.Idm.Connectors.Ldap;

/// <summary>
/// Active Directory / OpenLDAP konnektori (faqat o'qish). Hamma so'rovlar sahifalab (Paged Results), katta guruhlar
/// a'zolari AD'ning "member;range=" mexanizmi bilan bo'laklab o'qiladi (AD bitta javobda 1500 tadan ko'p qiymat bermaydi).
/// </summary>
public sealed class LdapConnector(LdapConnectorOptions options) : IConnector
{
    private static readonly string RangePrefix = ";range=";

    public async Task<ConnectorSnapshot> ReadAsync(CancellationToken ct = default)
    {
        using var connection = Connect();
        var o = options;
        string[] userAttributes = [o.UserIdAttribute, o.LoginAttribute, o.EmployeeNumberAttribute, o.FirstNameAttribute, o.LastNameAttribute,
            o.DisplayNameAttribute, o.EmailAttribute, o.DepartmentAttribute, o.JobTitleAttribute, .. o.ManagerAttribute is null ? [] : new[] { o.ManagerAttribute }];
        var users = await SearchAsync(connection, o.UsersDn, o.UserFilter, userAttributes, ct);
        var disabled = string.IsNullOrWhiteSpace(o.DisabledFilter) ? []
            : (await SearchAsync(connection, o.UsersDn, $"(&{Wrap(o.UserFilter)}{Wrap(o.DisabledFilter)})", ["1.1"], ct)).Select(r => r.Dn).ToList();
        var groups = await SearchAsync(connection, o.GroupsDn, o.GroupFilter, [o.GroupIdAttribute, o.GroupNameAttribute, o.GroupDescriptionAttribute, o.GroupMemberAttribute], ct);
        groups = await CompleteRangedMembersAsync(connection, groups, ct);
        return LdapSnapshotBuilder.Build(o, users, disabled, groups);
    }

    private LdapConnection Connect()
    {
        var connection = new LdapConnection(new LdapDirectoryIdentifier(options.Server, options.Port), new NetworkCredential(options.BindDn, options.BindPassword), AuthType.Basic)
        {
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;
        if (options.UseSsl) connection.SessionOptions.SecureSocketLayer = true;
        try
        {
            connection.Bind();
            return connection;
        }
        catch (Exception e) when (e is LdapException or DirectoryOperationException)
        {
            connection.Dispose();
            var scheme = options.UseSsl ? "ldaps" : "ldap";
            throw new ConnectorException($"{scheme}://{options.Server}:{options.Port} ga ulanib bo'lmadi ({options.BindDn}): {e.Message}", e);
        }
    }

    private async Task<List<LdapRecord>> SearchAsync(LdapConnection connection, string baseDn, string filter, string[] attributes, CancellationToken ct)
    {
        // Ikkilik atributlar (objectGUID) bayt sifatida, qolganlari matn (entryUUID, uidNumber va h.k.)
        var binary = attributes.Where(LdapSnapshotBuilder.IsBinary).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var request = new SearchRequest(baseDn, filter, SearchScope.Subtree, attributes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        var paging = new PageResultRequestControl(options.PageSize) { IsCritical = false };
        request.Controls.Add(paging);
        var result = new List<LdapRecord>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var response = await SendAsync<SearchResponse>(connection, request, $"{baseDn} {filter}");
            foreach (SearchResultEntry entry in response.Entries) result.Add(ToRecord(entry, binary));
            var cookie = response.Controls.OfType<PageResultResponseControl>().FirstOrDefault()?.Cookie;
            if (cookie is not { Length: > 0 }) return result;
            paging.Cookie = cookie;
        }
    }

    /// <summary>AD katta guruhda "member" o'rniga "member;range=0-1499" qaytaradi: qolgan bo'laklarni ketma-ket so'raymiz.</summary>
    private async Task<List<LdapRecord>> CompleteRangedMembersAsync(LdapConnection connection, List<LdapRecord> groups, CancellationToken ct)
    {
        var member = options.GroupMemberAttribute;
        var result = new List<LdapRecord>(groups.Count);
        foreach (var group in groups)
        {
            var ranged = group.Attributes.Keys.FirstOrDefault(k => k.StartsWith(member + RangePrefix, StringComparison.OrdinalIgnoreCase));
            if (ranged is null) { result.Add(group); continue; }
            var values = new List<object>(group.Attributes[ranged]);
            while (NextRangeStart(ranged, member) is { } start)
            {
                ct.ThrowIfCancellationRequested();
                var request = new SearchRequest(group.Dn, "(objectClass=*)", SearchScope.Base, $"{member}{RangePrefix}{start}-*");
                var response = await SendAsync<SearchResponse>(connection, request, group.Dn);
                var entry = response.Entries.Cast<SearchResultEntry>().FirstOrDefault();
                ranged = entry?.Attributes.AttributeNames.Cast<string>().FirstOrDefault(k => k.StartsWith(member + RangePrefix, StringComparison.OrdinalIgnoreCase));
                if (entry is null || ranged is null) break;
                values.AddRange(entry.Attributes[ranged].GetValues(typeof(string)));
            }
            var attributes = group.Attributes.Where(a => !a.Key.StartsWith(member + RangePrefix, StringComparison.OrdinalIgnoreCase))
                .Append(new KeyValuePair<string, object[]>(member, values.ToArray()));
            result.Add(LdapRecord.Create(group.Dn, attributes));
        }
        return result;
    }

    /// <summary>"member;range=0-1499" → 1500; "member;range=1500-*" (oxirgi bo'lak) → null.</summary>
    internal static int? NextRangeStart(string attributeName, string member)
    {
        var range = attributeName[(member.Length + RangePrefix.Length)..];
        var dash = range.IndexOf('-');
        if (dash < 0) return null;
        var end = range[(dash + 1)..];
        return end == "*" || !int.TryParse(end, out var last) ? null : last + 1;
    }

    private static LdapRecord ToRecord(SearchResultEntry entry, HashSet<string> binary) =>
        LdapRecord.Create(entry.DistinguishedName, entry.Attributes.AttributeNames.Cast<string>().Select(name =>
            new KeyValuePair<string, object[]>(name, entry.Attributes[name].GetValues(binary.Contains(name) ? typeof(byte[]) : typeof(string)))));

    private async Task<T> SendAsync<T>(LdapConnection connection, DirectoryRequest request, string what) where T : DirectoryResponse
    {
        try
        {
            return (T)await Task.Factory.FromAsync(
                (callback, state) => connection.BeginSendRequest(request, PartialResultProcessing.NoPartialResultSupport, callback, state),
                connection.EndSendRequest, null);
        }
        catch (Exception e) when (e is LdapException or DirectoryOperationException)
        {
            throw new ConnectorException($"LDAP so'rovi bajarilmadi ({what}): {e.Message}", e);
        }
    }

    private static string Wrap(string filter) => filter.StartsWith('(') ? filter : $"({filter})";
}
