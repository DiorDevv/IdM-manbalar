using System.Net;
using System.Net.Mail;
using Bank.Idm.Domain;
using Microsoft.EntityFrameworkCore;

namespace Bank.Idm.Infrastructure.Notifications;

public class NotificationOptions
{
    public bool Enabled { get; set; }
    /// <summary>Xabardagi havolalar uchun portal manzili, masalan https://idm.bank.uz.</summary>
    public string PortalUrl { get; set; } = "http://localhost:5173";
    public SmtpOptions Smtp { get; set; } = new();
    /// <summary>Ariza bosqichi shuncha soat hal qilinmasa — eslatma (keyin har shuncha soatda, MaxReminders martagacha).</summary>
    public int ReminderAfterHours { get; set; } = 24;
    public int MaxReminders { get; set; } = 3;
    /// <summary>Attestatsiya muddatidan shuncha kun oldin ko'rib chiqilmagan yozuvlari bor tekshiruvchilarga eslatma.</summary>
    public int ReviewDueReminderDays { get; set; } = 3;
    public int IntervalSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 5;
}

public class SmtpOptions
{
    public string Host { get; set; } = null!;
    /// <summary>587 (STARTTLS). 465 (implicit TLS) qo'llab-quvvatlanmaydi.</summary>
    public int Port { get; set; } = 587;
    /// <summary>STARTTLS majburiy (ishlab chiqarishda o'chirib bo'lmaydi).</summary>
    public bool UseTls { get; set; } = true;
    public string? User { get; set; }
    public string? Password { get; set; }
    public string From { get; set; } = null!;
    public string FromName { get; set; } = "Bank IdM";
    public int TimeoutSeconds { get; set; } = 30;
}

public record OutgoingMail(string To, string Subject, string Body);

public interface INotificationSender
{
    Task SendAsync(OutgoingMail mail, CancellationToken ct);
}

/// <summary>SMTP (STARTTLS). Har chaqiriqda yangi ulanish — kam hajm uchun yetarli.</summary>
public class SmtpNotificationSender(NotificationOptions options) : INotificationSender
{
    public async Task SendAsync(OutgoingMail mail, CancellationToken ct)
    {
        var s = options.Smtp;
        using var client = new SmtpClient(s.Host, s.Port) { EnableSsl = s.UseTls, Timeout = s.TimeoutSeconds * 1000, DeliveryMethod = SmtpDeliveryMethod.Network };
        if (!string.IsNullOrEmpty(s.User)) client.Credentials = new NetworkCredential(s.User, s.Password);
        using var message = new MailMessage(new MailAddress(s.From, s.FromName), new MailAddress(mail.To))
        {
            Subject = mail.Subject, Body = mail.Body, IsBodyHtml = false,
            BodyEncoding = System.Text.Encoding.UTF8, SubjectEncoding = System.Text.Encoding.UTF8
        };
        await client.SendMailAsync(message, ct);
    }
}

public record DispatchResult(int Sent, int Retrying, int Failed);

/// <summary>
/// Xabarnomalar: <see cref="PlanAsync"/> holatdan (kutilayotgan bosqichlar, yopilgan arizalar, attestatsiya) kerakli xabarlarni navbatga
/// yozadi, <see cref="DispatchAsync"/> yuboradi. Ikkalasi ham faqat yetakchi serverda chaqiriladi; takrordan DedupKey himoya qiladi.
/// Emaili yo'q xodimga xabar yozilmaydi. O'rinbosar bo'lsa — xabar unga ham.
/// </summary>
public class NotificationService(IdmDbContext db, TimeProvider time)
{
    private static readonly TimeZoneInfo Tashkent = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tashkent");
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)];
    /// <summary>Yoqilganda eski (yopilgan) arizalar haqida xabar yog'ilmasligi uchun.</summary>
    private static readonly TimeSpan ClosedLookback = TimeSpan.FromDays(2);

    private record Pending(string Key, string Kind, Guid RecipientId, string Subject, string Body);

    public async Task<int> PlanAsync(NotificationOptions options, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var portal = options.PortalUrl.TrimEnd('/');
        var planned = new List<Pending>();

        // O'rinbosarliklar: tasdiqlovchi → amaldagi o'rinbosarlari
        var delegates = (await db.ApproverDelegations.AsNoTracking()
                .Where(d => d.RevokedAt == null && d.ValidFrom <= now && d.ValidTo > now)
                .Select(d => new { d.DelegatorId, d.DelegateId }).ToListAsync(ct))
            .ToLookup(d => d.DelegatorId, d => d.DelegateId);
        IEnumerable<(Guid Id, string Suffix)> WithDelegates(Guid approver) =>
            new[] { (approver, "") }.Concat(delegates[approver].Select(d => (d, $":d:{d:N}")));

        // 1. Ariza bosqichlari: navbatdagi bosqich egasiga — yangi bosqich va eslatmalar
        var steps = await db.AccessRequests.AsNoTracking().Where(r => r.Status == AccessRequestStatus.Pending)
            .Select(r => new
            {
                r.Id, Role = r.Role.Name, Beneficiary = r.Beneficiary.LastName + " " + r.Beneficiary.FirstName, r.CreatedAt,
                Current = r.Approvals.Where(a => a.Decision == ApprovalDecision.Pending).OrderBy(a => a.Step)
                    .Select(a => new { a.Id, a.Step, a.ApproverId }).FirstOrDefault(),
                PreviousDecidedAt = r.Approvals.Where(a => a.Decision != ApprovalDecision.Pending).Max(a => a.DecidedAt)
            }).ToListAsync(ct);
        foreach (var s in steps.Where(s => s.Current?.ApproverId is not null))
        {
            var since = s.PreviousDecidedAt ?? s.CreatedAt;
            var link = $"{portal}/requests/{s.Id}";
            var elapsed = now - since;
            var reminder = options.ReminderAfterHours > 0 ? (int)(elapsed.TotalHours / options.ReminderAfterHours) : 0;
            foreach (var (to, suffix) in WithDelegates(s.Current!.ApproverId!.Value))
            {
                planned.Add(new($"request-step:{s.Current.Id:N}{suffix}", "request-step", to,
                    $"Ariza tasdiqlashingizni kutmoqda / Заявка ждёт согласования: {s.Role}",
                    Text($"{s.Beneficiary} uchun \"{s.Role}\" roliga ariza sizning qaroringizni kutmoqda.",
                         $"Заявка на роль «{s.Role}» для {s.Beneficiary} ожидает вашего решения.", link)));
                if (reminder >= 1 && reminder <= options.MaxReminders)
                    planned.Add(new($"request-remind:{s.Current.Id:N}:{reminder}{suffix}", "request-reminder", to,
                        $"Eslatma / Напоминание: {s.Role} — {(int)elapsed.TotalHours} soat / ч",
                        Text($"{s.Beneficiary} uchun \"{s.Role}\" roliga ariza {(int)elapsed.TotalHours} soatdan beri qaroringizni kutmoqda.",
                             $"Заявка на роль «{s.Role}» для {s.Beneficiary} ожидает вашего решения уже {(int)elapsed.TotalHours} ч.", link)));
            }
        }

        // 2. Ariza natijasi: so'ragan va kim uchun so'ralgan xodimga (o'zi bekor qilganlar — yo'q)
        var since2 = now - ClosedLookback;
        var closed = await db.AccessRequests.AsNoTracking()
            .Where(r => r.ClosedAt > since2 && (r.Status == AccessRequestStatus.Approved || r.Status == AccessRequestStatus.Rejected))
            .Select(r => new { r.Id, r.Status, Role = r.Role.Name, r.RequesterId, r.BeneficiaryId }).ToListAsync(ct);
        foreach (var r in closed)
        {
            var approved = r.Status == AccessRequestStatus.Approved;
            foreach (var to in new[] { r.RequesterId, r.BeneficiaryId }.Distinct())
                planned.Add(new($"request-closed:{r.Id:N}:{to:N}", "request-closed", to,
                    approved ? $"Ariza tasdiqlandi / Заявка согласована: {r.Role}" : $"Ariza rad etildi / Заявка отклонена: {r.Role}",
                    Text(approved ? $"\"{r.Role}\" roliga ariza tasdiqlandi. Ruxsatlar tizimlarda tez orada paydo bo'ladi." : $"\"{r.Role}\" roliga ariza rad etildi. Sababini portalda ko'ring.",
                         approved ? $"Заявка на роль «{r.Role}» согласована. Права появятся в системах в ближайшее время." : $"Заявка на роль «{r.Role}» отклонена. Причина — на портале.",
                         $"{portal}/requests/{r.Id}")));
        }

        // 3. Attestatsiya: kampaniya boshlanganda va muddat yaqinlashganda — ko'rib chiqilmagan yozuvlari bor tekshiruvchilarga
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, Tashkent).DateTime);
        var reviews = await db.AccessReviewItems.AsNoTracking()
            .Where(i => i.Decision == AccessReviewDecision.Pending && i.ReviewerId != null && i.Campaign.Status == AccessReviewStatus.Active)
            .GroupBy(i => new { i.CampaignId, i.Campaign.Name, i.Campaign.DueDate, ReviewerId = i.ReviewerId!.Value })
            .Select(g => new { g.Key.CampaignId, g.Key.Name, g.Key.DueDate, g.Key.ReviewerId, Count = g.Count() }).ToListAsync(ct);
        foreach (var r in reviews)
        {
            var link = $"{portal}/reviews";
            var due = r.DueDate.ToString("dd.MM.yyyy");
            foreach (var (to, suffix) in WithDelegates(r.ReviewerId))
            {
                planned.Add(new($"review-start:{r.CampaignId:N}:{r.ReviewerId:N}{suffix}", "review-start", to,
                    $"Attestatsiya / Аттестация: {r.Name}",
                    Text($"\"{r.Name}\" kampaniyasida {r.Count} ta ruxsatni ko'rib chiqishingiz kerak. Muddat: {due}.",
                         $"В кампании «{r.Name}» вам нужно рассмотреть прав: {r.Count}. Срок: {due}.", link)));
                if (r.DueDate.DayNumber - today.DayNumber is var left && left >= 0 && left <= options.ReviewDueReminderDays)
                    planned.Add(new($"review-due:{r.CampaignId:N}:{r.ReviewerId:N}{suffix}", "review-due", to,
                        $"Attestatsiya muddati yaqin / Срок аттестации: {due}",
                        Text($"\"{r.Name}\" kampaniyasida {r.Count} ta ruxsat hali ko'rib chiqilmagan. Muddat: {due}.",
                             $"В кампании «{r.Name}» не рассмотрено прав: {r.Count}. Срок: {due}.", link)));
            }
        }

        // 4. O'tish (mover): yangi rahbarga — xodimning qaysi rollari qaror kutayotgani (bir o'tish uchun bitta xabar)
        var movers = await db.IdentityRoles.AsNoTracking()
            .Where(r => r.MoverDueAt != null && (r.ValidTo == null || r.ValidTo > now) && r.Identity.ManagerId != null)
            .GroupBy(r => new { r.IdentityId, ManagerId = r.Identity.ManagerId!.Value, Name = r.Identity.LastName + " " + r.Identity.FirstName })
            .Select(g => new { g.Key.IdentityId, g.Key.ManagerId, g.Key.Name, Count = g.Count(), DueAt = g.Min(r => r.MoverDueAt!.Value) }).ToListAsync(ct);
        foreach (var m in movers)
        {
            var due = TimeZoneInfo.ConvertTime(m.DueAt, Tashkent).ToString("dd.MM.yyyy");
            foreach (var (to, suffix) in WithDelegates(m.ManagerId))
                planned.Add(new($"mover:{m.IdentityId:N}:{m.DueAt.UtcTicks}{suffix}", "mover", to,
                    $"Xodim o'tdi: rollarni ko'rib chiqing / Перевод сотрудника: проверьте роли",
                    Text($"{m.Name} sizning bo'limingizga o'tdi. Avvalgi joyidagi {m.Count} ta rol bo'yicha qaror qiling: kerak bo'lsa qoldiring, " +
                         $"aks holda {due} dan keyin avtomatik olib tashlanadi.",
                         $"{m.Name} переведён(а) в ваше подразделение. Примите решение по ролям с прежнего места ({m.Count}): оставьте нужные, " +
                         $"остальные будут отозваны автоматически после {due}.", $"{portal}/requests?tab=movers")));
        }

        // 5. Ikki kishilik tasdiq: yangi so'rov haqida qolgan administratorlarga (so'rovchining o'ziga emas)
        var changes = await db.ChangeRequests.AsNoTracking().Where(r => r.Status == ChangeRequestStatus.Pending && r.ExpiresAt > now)
            .Select(r => new { r.Id, r.Kind, r.RequestedBy, r.RequestedByName }).ToListAsync(ct);
        if (changes.Count > 0)
        {
            var adminLogins = await db.PortalRoleAssignments.AsNoTracking().Where(p => p.Role == PortalRole.Admin).Select(p => p.Login).ToListAsync(ct);
            var admins = await db.Accounts.AsNoTracking()
                .Where(a => adminLogins.Contains(a.Login) && a.IdentityId != null && a.Status != AccountStatus.Deleted && a.Application.Kind == ApplicationKind.Directory)
                .Select(a => new { a.Login, IdentityId = a.IdentityId!.Value }).ToListAsync(ct);
            foreach (var c in changes)
                foreach (var a in admins.Where(a => !string.Equals(a.Login, c.RequestedBy, StringComparison.OrdinalIgnoreCase)).DistinctBy(a => a.IdentityId))
                    planned.Add(new($"change:{c.Id:N}:{a.IdentityId:N}", "change-request", a.IdentityId,
                        $"Tasdiq kerak / Требуется подтверждение: {c.Kind}",
                        Text($"{c.RequestedByName ?? c.RequestedBy} administrator o'zgarishini ({c.Kind}) yubordi: u ikkinchi administrator tasdig'isiz qo'llanmaydi.",
                             $"{c.RequestedByName ?? c.RequestedBy} отправил(а) административное изменение ({c.Kind}): без подтверждения второго администратора оно не применяется.",
                             $"{portal}/changes")));
        }

        if (planned.Count == 0) return 0;
        var keys = planned.Select(p => p.Key).Distinct().ToList();
        var existing = (await db.Notifications.AsNoTracking().Where(n => keys.Contains(n.DedupKey)).Select(n => n.DedupKey).ToListAsync(ct)).ToHashSet();
        var fresh = planned.Where(p => !existing.Contains(p.Key)).DistinctBy(p => p.Key).ToList();
        if (fresh.Count == 0) return 0;
        var recipientIds = fresh.Select(p => p.RecipientId).Distinct().ToList();
        var emails = await db.Identities.AsNoTracking().Where(i => recipientIds.Contains(i.Id) && i.Email != null && i.Status != IdentityStatus.Terminated)
            .ToDictionaryAsync(i => i.Id, i => i.Email!, ct);
        var added = 0;
        foreach (var p in fresh.Where(p => emails.ContainsKey(p.RecipientId)))
        {
            db.Notifications.Add(new Notification
            {
                DedupKey = p.Key, Kind = p.Kind, RecipientId = p.RecipientId, Email = emails[p.RecipientId], Subject = p.Subject, Body = p.Body,
                CreatedAt = now, NextAttemptAt = now
            });
            added++;
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Boshqa nusxa shu payt yozdi (noyob kalit): keyingi aylanishda qolganlari
            db.ChangeTracker.Clear();
            return 0;
        }
        return added;
    }

    public async Task<DispatchResult> DispatchAsync(NotificationOptions options, INotificationSender sender, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        var due = await db.Notifications.Where(n => n.SentAt == null && n.FailedAt == null && n.NextAttemptAt <= now)
            .OrderBy(n => n.CreatedAt).Take(options.BatchSize).ToListAsync(ct);
        int sent = 0, retrying = 0, failed = 0;
        foreach (var n in due)
        {
            ct.ThrowIfCancellationRequested();
            n.Attempts++;
            try
            {
                await sender.SendAsync(new OutgoingMail(n.Email, n.Subject, n.Body), ct);
                (n.SentAt, n.NextAttemptAt, n.LastError) = (time.GetUtcNow(), null, null);
                sent++;
            }
            catch (Exception e) when (e is SmtpException or IOException or InvalidOperationException or FormatException or System.Net.Sockets.SocketException)
            {
                n.LastError = e.Message.Length <= 1000 ? e.Message : e.Message[..997] + "...";
                if (n.Attempts >= options.MaxAttempts || e is FormatException) { (n.FailedAt, n.NextAttemptAt) = (now, null); failed++; }
                else { n.NextAttemptAt = now + Backoff[Math.Clamp(n.Attempts - 1, 0, Backoff.Length - 1)]; retrying++; }
            }
            await db.SaveChangesAsync(CancellationToken.None);
            // Server javob bermayapti: qolganlari keyingi aylanishda
            if (n.SentAt is null && n.FailedAt is null) break;
        }
        return new DispatchResult(sent, retrying, failed);
    }

    private static string Text(string uz, string ru, string link) =>
        $"{uz}\n\n{ru}\n\n{link}\n\n—\nBank IdM. Bu xabar avtomatik yuborilgan, javob bermang. / Сообщение отправлено автоматически, не отвечайте на него.";
}
