using System.Globalization;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Bank.Idm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Siem;

public class SiemOptions
{
    public bool Enabled { get; set; }
    public string Name { get; set; } = "siem";
    public string Host { get; set; } = null!;
    public int Port { get; set; } = 6514;
    /// <summary>"tcp-tls" (RFC 5425, standart) yoki "tcp" (faqat sinov: ishlab chiqarishda taqiqlangan). UDP yo'q — yozuv yo'qolishi mumkin.</summary>
    public string Protocol { get; set; } = SiemProtocols.TcpTls;
    /// <summary>SIEM sertifikatini tekshirish uchun bankning ichki CA'si (PEM). Bo'sh — tizim ishonchli sertifikatlari.</summary>
    public string? CaCertificatePath { get; set; }
    public string AppName { get; set; } = "bank-idm";
    public int BatchSize { get; set; } = 500;
    public int TimeoutSeconds { get; set; } = 15;
    public int IntervalSeconds { get; set; } = 5;
}

public static class SiemProtocols
{
    public const string TcpTls = "tcp-tls";
    public const string Tcp = "tcp";
}

public record ForwardResult(int Sent, long LastEventId, long Lag);

/// <summary>
/// Audit jurnalini SIEM'ga yuboradi: syslog RFC 5424, TCP ustida TLS (RFC 5425), RFC 6587 octet-counting ramka. Xabar — JSON
/// (kim, nima, qachon, obyekt, tafsilot, IP, xesh va oldingi xesh: SIEM tomonda zanjir yaxlitligini ham tekshirish mumkin).
/// Kamida bir marta yetkaziladi: to'plam yuborilgach checkpoint yangilanadi; uzilishda keyingi urinish shu joydan davom etadi
/// (takror yuborilgan yozuvni SIEM "id" bo'yicha tashlab yuborishi mumkin). Yozuvlar Id tartibida commit qilinadi (AuditLog qulfi).
/// </summary>
public class SiemForwarder(IdmDbContext db, TimeProvider time)
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private const int Facility = 13;   // log audit (RFC 5424)
    private const int Severity = 5;    // notice

    public async Task<ForwardResult> ForwardAsync(SiemOptions options, CancellationToken ct = default)
    {
        var checkpoint = await db.SiemCheckpoints.FirstOrDefaultAsync(c => c.Name == options.Name, ct);
        if (checkpoint is null)
        {
            checkpoint = new SiemCheckpoint { Name = options.Name };
            db.SiemCheckpoints.Add(checkpoint);
        }
        var events = await db.AuditEvents.AsNoTracking().Where(e => e.Id > checkpoint.LastEventId).OrderBy(e => e.Id).Take(options.BatchSize).ToListAsync(ct);
        var max = await db.AuditEvents.MaxAsync(e => (long?)e.Id, ct) ?? 0;
        if (events.Count == 0)
        {
            if (db.Entry(checkpoint).State == EntityState.Added) await db.SaveChangesAsync(ct);
            return new ForwardResult(0, checkpoint.LastEventId, 0);
        }
        try
        {
            await SendAsync(options, events.Select(e => Format(e, options.AppName)), ct);
        }
        // Tarmoq, TLS yoki o'z vaqt chegaramiz (server to'xtatilishi emas)
        catch (Exception e) when (e is IOException or SocketException or System.Security.Authentication.AuthenticationException or TimeoutException
                                  || e is OperationCanceledException && !ct.IsCancellationRequested)
        {
            (checkpoint.LastError, checkpoint.LastErrorAt) = (Truncate($"{e.GetType().Name}: {e.Message}"), time.GetUtcNow());
            await db.SaveChangesAsync(CancellationToken.None);
            throw new IOException($"SIEM'ga ({options.Host}:{options.Port}) yuborib bo'lmadi: {e.Message}", e);
        }
        (checkpoint.LastEventId, checkpoint.LastSentAt, checkpoint.LastError) = (events[^1].Id, time.GetUtcNow(), null);
        await db.SaveChangesAsync(CancellationToken.None);
        return new ForwardResult(events.Count, checkpoint.LastEventId, max - checkpoint.LastEventId);
    }

    private static async Task SendAsync(SiemOptions options, IEnumerable<string> messages, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        using var client = new TcpClient();
        await client.ConnectAsync(options.Host, options.Port, timeout.Token);
        Stream stream = client.GetStream();
        if (options.Protocol == SiemProtocols.TcpTls)
        {
            var ssl = new SslStream(stream, leaveInnerStreamOpen: false, (_, certificate, chain, errors) => Validate(options, certificate, chain, errors));
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = options.Host }, timeout.Token);
            stream = ssl;
        }
        else if (options.Protocol != SiemProtocols.Tcp) throw new IOException($"Noma'lum SIEM protokoli: {options.Protocol}");
        await using (stream)
        {
            foreach (var message in messages)
            {
                var bytes = Encoding.UTF8.GetBytes(message);
                // RFC 6587 octet-counting: "<uzunlik> <xabar>" — xabar ichidagi yangi qator ramkani buzmaydi
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"{bytes.Length} "), timeout.Token);
                await stream.WriteAsync(bytes, timeout.Token);
            }
            await stream.FlushAsync(timeout.Token);
        }
    }

    /// <summary>Bankning ichki CA'si berilgan bo'lsa — zanjir faqat shu CA'ga quriladi; aks holda tizim ishonchli sertifikatlari.</summary>
    private static bool Validate(SiemOptions options, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        if (options.CaCertificatePath is null) return errors == SslPolicyErrors.None;
        if (certificate is null || errors.HasFlag(SslPolicyErrors.RemoteCertificateNameMismatch) || errors.HasFlag(SslPolicyErrors.RemoteCertificateNotAvailable))
            return false;
        using var custom = new X509Chain();
        custom.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        custom.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        custom.ChainPolicy.CustomTrustStore.ImportFromPemFile(options.CaCertificatePath);
        return custom.Build(new X509Certificate2(certificate));
    }

    /// <summary>RFC 5424: &lt;PRI&gt;1 VAQT HOST ILOVA - MSGID [idm@32473 ...] JSON</summary>
    internal static string Format(AuditEvent e, string appName)
    {
        var json = JsonSerializer.Serialize(new
        {
            id = e.Id, occurredAt = e.OccurredAt, actor = e.ActorId, actorName = e.ActorName, action = e.Action, targetType = e.TargetType,
            targetId = e.TargetId, details = e.Details, ip = e.IpAddress, correlationId = e.CorrelationId, hash = e.Hash, previousHash = e.PreviousHash
        }, Json);
        var timestamp = e.OccurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
        var host = Header(Environment.MachineName);
        var msgId = Header(e.Action.Length <= 32 ? e.Action : e.Action[..32]);
        return $"<{Facility * 8 + Severity}>1 {timestamp} {host} {Header(appName)} - {msgId} [idm@32473 id=\"{e.Id}\" actor=\"{Param(e.ActorId)}\" hash=\"{e.Hash}\"] {json}";
    }

    /// <summary>Sarlavha maydoni: faqat bosma ASCII, bo'shliqsiz (RFC 5424 PRINTUSASCII).</summary>
    private static string Header(string value)
    {
        var clean = new string(value.Where(c => c is > ' ' and <= '~').ToArray());
        return clean.Length == 0 ? "-" : clean.Length <= 48 ? clean : clean[..48];
    }

    /// <summary>SD-PARAM qiymati: ", \ va ] ekranlanadi.</summary>
    private static string Param(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("]", "\\]");

    private static string Truncate(string value) => value.Length <= 2000 ? value : value[..1997] + "...";
}
