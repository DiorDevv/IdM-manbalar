namespace Bank.Idm.Infrastructure.Access;

/// <summary>
/// Xodim boshqa filial yoki bo'limga o'tganda (mover) ariza yoki qo'lda berilgan rollar siyosati (Idm:Mover).
/// Avtomatik rollar qoidadan darhol qayta hisoblanadi; boshqa rollar <see cref="GraceDays"/> kun ichida yangi rahbar
/// "qoldirish" deb tasdiqlamasa, tugaydi. Lavozim yoki rahbar o'zgarishi bu siyosatni ishga tushirmaydi.
/// </summary>
public class MoverOptions
{
    /// <summary>O'chirilsa, o'tishda rollarga tegilmaydi (faqat hodisa yoziladi).</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Ish topshirish muddati, kun. 0 — rollar o'tish aniqlanishi bilan darhol tugaydi.</summary>
    public int GraceDays { get; set; } = 7;
    /// <summary>Bitta HR importida shundan ko'p foiz ishlayotgan xodim o'tsa, import qo'llanmaydi (noto'g'ri fayl belgisi).</summary>
    public int MaxMovePercent { get; set; } = 20;
    /// <summary>Muddati o'tgan qarorlarni tekshirish oralig'i, soniya (0 — fon ishi o'chiq).</summary>
    public int IntervalSeconds { get; set; } = 300;
}
