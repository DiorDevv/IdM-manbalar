namespace Bank.Idm.Api.Endpoints;

/// <summary>
/// Barcha foydalanuvchilar uchun umumiy, qisqa muddatli natija (masalan bosh sahifa ko'rsatkichlari, butun bank bo'yicha hisob):
/// muddat tugaganda faqat bitta so'rov qayta hisoblaydi, bir vaqtda kelganlar uning natijasini kutadi (bazaga "to'lqin" bo'lmaydi).
/// Har server nusxasida alohida.
/// </summary>
public sealed class SharedSnapshot<T>(TimeProvider time) where T : class
{
    public sealed record Entry(T Value, DateTimeOffset At);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile Entry? _current;

    /// <param name="staleIf">true bo'lsa, muddat tugamagan bo'lsa ham qayta hisoblanadi (masalan, shu orada sinxronlash tugagan).</param>
    public async Task<Entry> GetAsync(TimeSpan maxAge, Func<CancellationToken, Task<T>> factory, Func<Entry, bool>? staleIf, CancellationToken ct)
    {
        bool Fresh(Entry? e) => e is not null && time.GetUtcNow() - e.At < maxAge && staleIf?.Invoke(e) != true;
        if (Fresh(_current)) return _current!;
        await _gate.WaitAsync(ct);
        try
        {
            if (Fresh(_current)) return _current!;
            var started = time.GetUtcNow();
            var value = await factory(ct);
            return _current = new Entry(value, started);
        }
        finally
        {
            _gate.Release();
        }
    }
}
