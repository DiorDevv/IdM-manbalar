using System.Globalization;
using System.Text;

namespace Bank.Idm.Connectors.Hr;

/// <summary>
/// HR eksport faylini o'qiydi. Qat'iy: sarlavhada majburiy ustun yo'q yoki tabel raqami takrorlansa, butun import rad etiladi
/// (haqiqat manbaidagi xato xodimlarni noto'g'ri "ishdan bo'shatib" yubormasligi kerak). Alohida noto'g'ri qatorlar o'tkazib
/// yuboriladi va ularning tabel raqamlari "o'tkazib yuborilgan" ro'yxatiga tushadi (ular yo'qolgan deb hisoblanmaydi).
/// </summary>
public sealed class CsvHrSource(CsvHrSourceOptions options) : IHrSource
{
    public async Task<HrSnapshot> ReadAsync(CancellationToken ct = default)
    {
        var file = new FileInfo(options.Path);
        if (!file.Exists) throw new ConnectorException($"HR fayli topilmadi: {options.Path}");
        if (file.Length > options.MaxFileBytes) throw new ConnectorException($"HR fayli juda katta: {file.Length} bayt (chegara {options.MaxFileBytes})");
        string text;
        try
        {
            // UTF-8 qat'iy: noto'g'ri kodlash (masalan Windows-1251) jimgina buzilgan ismlarga aylanmasin
            text = await File.ReadAllTextAsync(file.FullName, new UTF8Encoding(false, throwOnInvalidBytes: true), ct);
        }
        catch (DecoderFallbackException)
        {
            throw new ConnectorException("HR fayli UTF-8 kodlashda emas. Eksportni UTF-8 (CSV UTF-8) formatida saqlang.");
        }
        catch (IOException e)
        {
            throw new ConnectorException($"HR faylini o'qib bo'lmadi: {e.Message}", e);
        }
        return Parse(text, options);
    }

    public static HrSnapshot Parse(string text, CsvHrSourceOptions o)
    {
        var rows = CsvReader.Read(text.TrimStart('﻿'));
        if (rows.Count == 0) throw new ConnectorException("HR fayli bo'sh");
        var header = rows[0].Select(h => h.Trim()).ToList();
        int? Column(string? name, bool required = false)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var index = header.FindIndex(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) return index;
            if (required) throw new ConnectorException($"HR faylida majburiy ustun yo'q: \"{name}\" (sarlavha: {string.Join(", ", header)})");
            return null;
        }
        var c = o.Columns;
        var number = Column(c.EmployeeNumber, true)!.Value;
        var first = Column(c.FirstName, true)!.Value;
        var last = Column(c.LastName, true)!.Value;
        var middle = Column(c.MiddleName); var email = Column(c.Email); var department = Column(c.Department);
        var title = Column(c.JobTitle); var manager = Column(c.ManagerEmployeeNumber); var hire = Column(c.HireDate);
        var termination = Column(c.TerminationDate); var status = Column(c.Status);

        var records = new List<HrRecord>();
        var warnings = new List<string>();
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 1; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = i + 1;
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            string? Get(int? index) => index is { } x && x < row.Count && !string.IsNullOrWhiteSpace(row[x]) ? row[x].Trim() : null;

            var id = Get(number);
            if (id is null) { warnings.Add($"{line}-qator: tabel raqami yo'q"); continue; }
            if (seen.TryGetValue(id, out var firstLine))
                throw new ConnectorException($"Tabel raqami {id} takrorlangan ({firstLine} va {line}-qatorlar). HR eksportini tekshiring: import qo'llanmadi.");
            seen[id] = line;

            var problems = new List<string>();
            string? Text(int? index, string field, int max, bool required = false)
            {
                var v = Get(index);
                if (v is null) { if (required) problems.Add($"{field} yo'q"); return null; }
                if (v.Length > max) { problems.Add($"{field} {max} belgidan uzun"); return null; }
                return v;
            }
            DateOnly? Date(int? index, string field)
            {
                var v = Get(index);
                if (v is null) return null;
                if (DateOnly.TryParseExact(v, o.DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
                problems.Add($"{field} noto'g'ri sana: \"{v}\"");
                return null;
            }
            var record = new HrRecord(
                Text(number, "tabel raqami", 64)!,
                Text(first, "ism", 128, required: true)!,
                Text(last, "familiya", 128, required: true)!,
                Text(middle, "otasining ismi", 128),
                Text(email, "email", 256),
                Text(department, "bo'lim", 256),
                Text(title, "lavozim", 256),
                Text(manager, "rahbar tabel raqami", 64),
                Date(hire, "ishga kirgan sana"),
                Date(termination, "ishdan ketgan sana"),
                ParseStatus(Get(status), o, problems));
            if (record.Email is { } e && (!e.Contains('@') || e.Contains(' '))) problems.Add($"email noto'g'ri: \"{e}\"");
            if (record.ManagerEmployeeNumber == record.EmployeeNumber) problems.Add("xodim o'ziga o'zi rahbar");
            if (problems.Count > 0)
            {
                // Faqat qator va tabel raqami: ism va boshqa shaxsiy ma'lumot ogohlantirishga yozilmaydi
                warnings.Add($"{line}-qator ({id}): {string.Join("; ", problems)}");
                skipped.Add(id);
                continue;
            }
            records.Add(record);
        }
        return new HrSnapshot(records) { Warnings = warnings, SkippedEmployeeNumbers = skipped };
    }

    private static HrStatus? ParseStatus(string? value, CsvHrSourceOptions o, List<string> problems)
    {
        if (value is null) return null;
        bool In(string[] values) => values.Any(v => v.Equals(value, StringComparison.OrdinalIgnoreCase));
        if (In(o.ActiveValues)) return HrStatus.Active;
        if (In(o.SuspendedValues)) return HrStatus.Suspended;
        if (In(o.TerminatedValues)) return HrStatus.Terminated;
        problems.Add($"holat noma'lum: \"{value}\"");
        return null;
    }
}

/// <summary>RFC 4180 CSV: qo'shtirnoq ichida ajratuvchi, qator o'tkazish va "" (qo'shtirnoq) bo'lishi mumkin. Ajratuvchi ";" yoki ",".</summary>
public static class CsvReader
{
    public static List<List<string>> Read(string text)
    {
        var delimiter = DetectDelimiter(text);
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else quoted = false;
                }
                else field.Append(ch);
            }
            else if (ch == '"' && field.Length == 0) quoted = true;
            else if (ch == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                rows.Add(row); row = [];
            }
            else field.Append(ch);
        }
        if (quoted) throw new ConnectorException("HR fayli buzilgan: yopilmagan qo'shtirnoq");
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    private static char DetectDelimiter(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var header = end < 0 ? text : text[..end];
        return header.Count(ch => ch == ';') >= header.Count(ch => ch == ',') && header.Contains(';') ? ';' : ',';
    }
}
