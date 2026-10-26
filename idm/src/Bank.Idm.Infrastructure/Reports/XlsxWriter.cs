using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Bank.Idm.Infrastructure.Reports;

/// <summary>
/// Minimal Excel (.xlsx, Office Open XML) yozuvchi: bitta varaq, qalin sarlavha, muzlatilgan birinchi qator, avtofiltr.
/// Tashqi kutubxona yo'q (bank tarmog'ida qo'shimcha paket — qo'shimcha xavf). Barcha qiymatlar matn (inlineStr):
/// "=..." bilan boshlangan qiymat formula bo'lib bajarilmaydi (CSV/formula in'eksiyasidan himoya).
/// </summary>
public static class XlsxWriter
{
    public static byte[] Write(ReportTable table)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                </Types>
                """);
            Entry(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Entry(zip, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            Entry(zip, "xl/workbook.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <sheets><sheet name="{Escape(SheetName(table.Title))}" sheetId="1" r:id="rId1"/></sheets>
                <definedNames><definedName name="_xlnm._FilterDatabase" localSheetId="0" hidden="1">'{Escape(SheetName(table.Title).Replace("'", "''"))}'!$A$1:${Column(table.Columns.Count - 1)}${table.Rows.Count + 1}</definedName></definedNames>
                </workbook>
                """);
            // 0 — oddiy, 1 — qalin sarlavha (kulrang fon)
            Entry(zip, "xl/styles.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
                <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FFE8ECF2"/></patternFill></fill></fills>
                <borders count="1"><border/></borders>
                <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                <cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFont="1" applyFill="1"/></cellXfs>
                </styleSheet>
                """);
            Entry(zip, "xl/worksheets/sheet1.xml", Sheet(table));
        }
        return stream.ToArray();
    }

    private static string Sheet(ReportTable table)
    {
        var sb = new StringBuilder();
        sb.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">""");
        sb.Append("""<sheetViews><sheetView workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews>""");
        sb.Append("<cols>");
        for (var c = 0; c < table.Columns.Count; c++)
        {
            var width = Math.Clamp(table.Rows.Select(r => c < r.Count ? r[c]?.Length ?? 0 : 0).Append(table.Columns[c].Length).Max() + 2, 8, 60);
            sb.Append($"""<col min="{c + 1}" max="{c + 1}" width="{width}" customWidth="1"/>""");
        }
        sb.Append("</cols><sheetData>");
        Row(sb, 1, table.Columns, style: 1);
        for (var r = 0; r < table.Rows.Count; r++) Row(sb, r + 2, table.Rows[r], style: 0);
        sb.Append("</sheetData>");
        sb.Append($"""<autoFilter ref="A1:{Column(table.Columns.Count - 1)}{table.Rows.Count + 1}"/>""");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, int index, IReadOnlyList<string?> values, int style)
    {
        sb.Append($"""<row r="{index}">""");
        for (var c = 0; c < values.Count; c++)
        {
            var value = values[c];
            if (string.IsNullOrEmpty(value)) continue;
            sb.Append($"""<c r="{Column(c)}{index}" t="inlineStr"{(style > 0 ? $" s=\"{style}\"" : "")}><is><t xml:space="preserve">{Escape(value)}</t></is></c>""");
        }
        sb.Append("</row>");
    }

    /// <summary>0 → A, 25 → Z, 26 → AA.</summary>
    internal static string Column(int index)
    {
        var name = "";
        for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    /// <summary>XML'ga mos bo'lmagan boshqaruv belgilari tashlanadi, maxsus belgilar ekranlanadi.</summary>
    internal static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (char.IsHighSurrogate(ch) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                sb.Append(ch).Append(value[++i]);
                continue;
            }
            if (!XmlConvert.IsXmlChar(ch)) continue;   // boshqaruv belgilari va juftsiz surrogatlar
            sb.Append(ch switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", '"' => "&quot;", _ => ch.ToString() });
        }
        return sb.ToString();
    }

    /// <summary>Excel varaq nomi: ko'pi bilan 31 belgi, \ / ? * [ ] : taqiqlangan.</summary>
    private static string SheetName(string title)
    {
        var clean = new string(title.Where(c => c is not ('\\' or '/' or '?' or '*' or '[' or ']' or ':')).ToArray()).Trim();
        return clean.Length == 0 ? "Hisobot" : clean.Length <= 31 ? clean : clean[..31];
    }

    private static void Entry(ZipArchive zip, string name, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(name, CompressionLevel.Optimal).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }
}
