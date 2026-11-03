using System.DirectoryServices.Protocols;
using System.Net;

namespace Bank.Idm.Connectors.Ldap;

/// <summary>LDAP ulanishi va so'rovlar: o'qish (LdapConnector) va yozish (LdapProvisioner) uchun umumiy.</summary>
internal static class LdapSession
{
    /// <summary>Serverlarni tartib bilan sinaydi: vaqtinchalik xatoda keyingisi, noto'g'ri parolda (49) darhol to'xtaydi.</summary>
    public static LdapConnection Open(LdapConnectorOptions options, string bindDn, string password)
    {
        var endpoints = options.Endpoints;
        if (endpoints.Count == 0) throw new ConnectorException("LDAP serveri ko'rsatilmagan (Server yoki Servers)");
        var errors = new List<string>();
        foreach (var (host, port) in endpoints)
        {
            try
            {
                return OpenOne(options, host, port, bindDn, password);
            }
            catch (ConnectorException e) when (e.IsTransient)
            {
                errors.Add(e.Message);
            }
        }
        throw new ConnectorException(errors.Count == 1 ? errors[0] : $"Hech bir LDAP serveriga ulanib bo'lmadi: {string.Join("; ", errors)}", transient: true);
    }

    private static LdapConnection OpenOne(LdapConnectorOptions options, string host, int port, string bindDn, string password)
    {
        var connection = new LdapConnection(new LdapDirectoryIdentifier(host, port), new NetworkCredential(bindDn, password), AuthType.Basic)
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
            // Ulanish xatosi vaqtinchalik bo'lishi mumkin (server o'chgan, tarmoq); noto'g'ri parol (49) — doimiy
            var transient = e is LdapException { ErrorCode: not 49 };
            throw new ConnectorException($"{scheme}://{host}:{port} ga ulanib bo'lmadi ({bindDn}): {e.Message}", e, transient);
        }
    }

    public static async Task<T> SendAsync<T>(LdapConnection connection, DirectoryRequest request, string what) where T : DirectoryResponse
    {
        try
        {
            return (T)await Task.Factory.FromAsync(
                (callback, state) => connection.BeginSendRequest(request, PartialResultProcessing.NoPartialResultSupport, callback, state),
                connection.EndSendRequest, null);
        }
        catch (DirectoryOperationException e)
        {
            throw new ConnectorException($"LDAP so'rovi bajarilmadi ({what}): {e.Message}", e, IsTransient(e.Response?.ResultCode)) { ResultCode = (int?)e.Response?.ResultCode };
        }
        catch (LdapException e)
        {
            // Tarmoq, vaqt tugashi, server yopildi: qayta urinish mumkin
            throw new ConnectorException($"LDAP so'rovi bajarilmadi ({what}): {e.Message}", e, transient: true);
        }
    }

    /// <summary>busy (51), unavailable (52), timeLimitExceeded (3), adminLimitExceeded (11), other (80) — server tomonida vaqtinchalik.</summary>
    private static bool IsTransient(ResultCode? code) => code is ResultCode.Busy or ResultCode.Unavailable or ResultCode.TimeLimitExceeded
        or ResultCode.AdminLimitExceeded or ResultCode.Other;

    /// <summary>RFC 4515: filtr qiymatidagi maxsus belgilar (LDAP in'eksiyasidan himoya).</summary>
    public static string EscapeFilter(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
            sb.Append(c switch { '\\' => @"\5c", '*' => @"\2a", '(' => @"\28", ')' => @"\29", '\0' => @"\00", _ => c.ToString() });
        return sb.ToString();
    }

    public static string EscapeFilter(byte[] value) => string.Concat(value.Select(b => $"\\{b:x2}"));

    /// <summary>RFC 4514: DN komponenti qiymati.</summary>
    public static string EscapeDnValue(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length + 4);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is ',' or '+' or '"' or '\\' or '<' or '>' or ';' or '=' || (i == 0 && c is '#' or ' ') || (i == value.Length - 1 && c == ' '))
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
