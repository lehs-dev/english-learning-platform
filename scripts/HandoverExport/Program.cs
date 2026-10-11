using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Globalization;

// Dependency-free export: source Markdown remains editable; all binaries are generated locally.
var root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var handover = Path.Combine(root, "docs", "handover");
var output = Path.Combine(handover, "exports");
Directory.CreateDirectory(output);
var font = new TrueTypeFont(Path.Combine(handover, "assets", "NotoSans-Regular.ttf"));
foreach (var name in new[] { "SP11-user-manual", "SP13-technical-operations", "SP14-test-acceptance", "SP15-requirements-design", "report-three-courses" })
{
    var blocks = Markdown.Read(Path.Combine(handover, name + ".md"));
    Docx.Write(Path.Combine(output, name + ".docx"), blocks);
    Pdf.WriteDocument(Path.Combine(output, name + ".pdf"), blocks, font);
    Html.Write(Path.Combine(output, name + ".html"), blocks);
}
var slides = Markdown.ReadSlides(Path.Combine(handover, "defense-deck.md"));
if (slides.Count is < 25 or > 30) throw new InvalidOperationException("Defense deck must contain 25–30 slides.");
Pptx.Write(Path.Combine(output, "defense-deck.pptx"), slides);
Pdf.WriteSlides(Path.Combine(output, "defense-deck.pdf"), slides, font);
Docx.Write(Path.Combine(output, "defense-speaker-notes.docx"), slides.SelectMany((s, i) => new[] {
    new Block("h1", $"{i + 1:00}. {s.Title}"), new Block("paragraph", string.Join(" · ", s.Bullets)), new Block("paragraph", "Thuyết trình: " + s.Notes) }).ToList());
foreach (var path in Directory.GetFiles(output).Where(p => p.EndsWith(".docx") || p.EndsWith(".pptx")))
{
    using var zip = ZipFile.OpenRead(path);
    foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))
    {
        using var stream = entry.Open();
        XDocument.Load(stream);
    }
}
var manifests = Directory.GetFiles(handover, "*", SearchOption.AllDirectories)
    .Where(p => !p.Contains(Path.DirectorySeparatorChar + "bundle" + Path.DirectorySeparatorChar) && !p.EndsWith("SHA256SUMS.txt") && !p.EndsWith(".zip"))
    .Order(StringComparer.Ordinal).Select(p => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))) + "  " + Path.GetRelativePath(handover, p).Replace('\\', '/'));
File.WriteAllLines(Path.Combine(handover, "SHA256SUMS.txt"), manifests, new UTF8Encoding(false));
Console.WriteLine($"Exported 5 document pairs, 28-slide deck ({slides.Count} actual) and speaker notes. OOXML parsed successfully.");

record Block(string Kind, string Text, List<string[]>? Rows = null);
record Slide(string Title, List<string> Bullets, string Notes);
static class Markdown
{
    public static string Clean(string s) => Regex.Replace(Regex.Replace(s, @"\[([^\]]+)\]\([^)]*\)", "$1"), @"\*\*|`", "").Trim();
    public static List<Block> Read(string path)
    {
        var lines = File.ReadAllLines(path); var result = new List<Block>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim(); if (line.Length == 0) continue;
            if (line.StartsWith("```"))
            {
                var language = line[3..]; var list = new List<string>();
                while (++i < lines.Length && !lines[i].Trim().StartsWith("```")) list.Add(lines[i]);
                result.Add(new Block(language == "mermaid" ? "diagram" : "code", string.Join("\n", list))); continue;
            }
            if (line.StartsWith('|'))
            {
                var rows = new List<string[]>();
                do
                {
                    var cells = lines[i].Trim().Trim('|').Split('|').Select(Clean).ToArray();
                    if (!cells.All(c => Regex.IsMatch(c, @"^:?-+:?$"))) rows.Add(cells);
                    i++;
                } while (i < lines.Length && lines[i].Trim().StartsWith('|'));
                i--; result.Add(new Block("table", "", rows)); continue;
            }
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            if (heading.Success) result.Add(new Block("h" + heading.Groups[1].Value.Length, Clean(heading.Groups[2].Value)));
            else if (Regex.IsMatch(line, @"^(- |\d+\. )")) result.Add(new Block("bullet", Clean(Regex.Replace(line, @"^(- |\d+\. )", ""))));
            else result.Add(new Block("paragraph", Clean(line)));
        }
        return result;
    }
    public static List<Slide> ReadSlides(string path) => Regex.Split(File.ReadAllText(path), @"(?m)^---\s*$")
        .Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => {
            var lines = t.Split('\n').Select(x => x.Trim()).ToArray();
            return new Slide(Clean(lines.First(x => x.StartsWith("# "))[2..]), lines.Where(x => x.StartsWith("- ")).Select(x => Clean(x[2..])).ToList(), string.Join(" ", lines.Where(x => x.StartsWith('>')).Select(x => Clean(x[1..]))));
        }).ToList();
    public static string DiagramSummary(string source)
    {
        if (source.Contains("erDiagram")) return "Course → Module → Lesson → Resource\nStudent → Enrollment → LessonProgress\nOrder → Payment → Enrollment\nAssessment → Questions → Options\nAssessment → Attempt → Answer / SkillResult\nCourse → Final Assessment";
        return "Trình duyệt → Web (MVC) → Application (DTO / Use case)\nApplication → Domain (Entity / Invariant)\nWeb → Infrastructure (Identity / EF / Transaction)\nInfrastructure → SQL Server 2022";
    }
}
static class Package
{
    public static void Put(ZipArchive zip, string name, string contents)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)); writer.Write(contents);
    }
    public static string Escape(string s) => System.Security.SecurityElement.Escape(s) ?? "";
    public const string Header = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>";
}
static class Docx
{
    static string P(string text, string style = "Normal") => $"<w:p><w:pPr><w:pStyle w:val=\"{style}\"/></w:pPr><w:r><w:t xml:space=\"preserve\">{Package.Escape(text)}</w:t></w:r></w:p>";
    public static void Write(string path, List<Block> blocks)
    {
        File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Package.Put(zip, "[Content_Types].xml", Package.Header + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/><Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/><Override PartName=\"/word/footer1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml\"/></Types>");
        Package.Put(zip, "_rels/.rels", Package.Header + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
        Package.Put(zip, "word/_rels/document.xml.rels", Package.Header + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer\" Target=\"footer1.xml\"/></Relationships>");
        var body = new StringBuilder();
        foreach (var block in blocks)
        {
            if (block.Kind == "table")
            {
                body.Append("<w:tbl><w:tblPr><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:color=\"CCD7DF\"/><w:left w:val=\"single\" w:sz=\"4\" w:color=\"CCD7DF\"/><w:bottom w:val=\"single\" w:sz=\"4\" w:color=\"CCD7DF\"/><w:right w:val=\"single\" w:sz=\"4\" w:color=\"CCD7DF\"/><w:insideH w:val=\"single\" w:sz=\"4\" w:color=\"CCD7DF\"/><w:insideV w:val=\"single\" w:sz=\"4\" w:color=\"CCD7DF\"/></w:tblBorders><w:tblCellMar><w:top w:w=\"90\" w:type=\"dxa\"/><w:left w:w=\"100\" w:type=\"dxa\"/><w:bottom w:w=\"90\" w:type=\"dxa\"/><w:right w:w=\"100\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr>");
                var rows = block.Rows!;
                for (var index = 0; index < rows.Count; index++)
                {
                    body.Append("<w:tr><w:trPr><w:cantSplit/>" + (index == 0 ? "<w:tblHeader/>" : "") + "</w:trPr>");
                    foreach (var cell in rows[index]) body.Append("<w:tc><w:tcPr><w:tcW w:w=\"" + (9360 / rows[index].Length) + "\" w:type=\"dxa\"/>" + (index == 0 ? "<w:shd w:fill=\"E8F3F2\"/>" : "") + "</w:tcPr>" + P(cell, "TableText") + "</w:tc>");
                    body.Append("</w:tr>");
                }
                body.Append("</w:tbl>" + P(""));
            }
            else if (block.Kind is "code" or "diagram")
                foreach (var line in (block.Kind == "diagram" ? Markdown.DiagramSummary(block.Text) : block.Text).Split('\n')) body.Append(P(line, "Code"));
            else body.Append(P((block.Kind == "bullet" ? "• " : "") + block.Text, block.Kind.StartsWith('h') ? "Heading" + block.Kind[1..] : block.Kind == "bullet" ? "ListBullet" : "Normal"));
        }
        body.Append("<w:sectPr><w:footerReference w:type=\"default\" r:id=\"rId2\"/><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1134\" w:right=\"1273\" w:bottom=\"1134\" w:left=\"1273\" w:header=\"600\" w:footer=\"600\"/></w:sectPr>");
        Package.Put(zip, "word/document.xml", Package.Header + "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body>" + body + "</w:body></w:document>");
        Package.Put(zip, "word/styles.xml", Package.Header + """
<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Noto Sans" w:hAnsi="Noto Sans" w:eastAsia="Noto Sans" w:cs="Noto Sans"/><w:sz w:val="21"/><w:color w:val="20313F"/><w:lang w:val="vi-VN"/></w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after="140" w:line="290" w:lineRule="auto"/></w:pPr></w:pPrDefault></w:docDefaults>
<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="220" w:after="220"/><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:color w:val="087E83"/><w:sz w:val="42"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading2"><w:name w:val="heading 2"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="240" w:after="160"/><w:outlineLvl w:val="1"/></w:pPr><w:rPr><w:b/><w:color w:val="087E83"/><w:sz w:val="28"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading3"><w:name w:val="heading 3"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="180" w:after="120"/><w:outlineLvl w:val="2"/></w:pPr><w:rPr><w:b/><w:sz w:val="24"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="ListBullet"><w:name w:val="List Bullet"/><w:basedOn w:val="Normal"/><w:pPr><w:ind w:left="240"/><w:spacing w:after="100"/></w:pPr></w:style>
<w:style w:type="paragraph" w:styleId="Code"><w:name w:val="Code"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:after="40"/><w:shd w:fill="F1F5F8"/></w:pPr><w:rPr><w:sz w:val="17"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="TableText"><w:name w:val="Table Text"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:after="60" w:line="250"/></w:pPr><w:rPr><w:sz w:val="19"/></w:rPr></w:style>
</w:styles>
""");
        Package.Put(zip, "word/footer1.xml", Package.Header + "<w:ftr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:pPr><w:jc w:val=\"right\"/></w:pPr><w:r><w:rPr><w:sz w:val=\"16\"/><w:color w:val=\"667788\"/></w:rPr><w:t>English Learning Platform · Candidate · Trang </w:t></w:r><w:fldSimple w:instr=\"PAGE\"/></w:p></w:ftr>");
    }
}
static class Html
{
    public static void Write(string path, List<Block> blocks)
    {
        var body = new StringBuilder();
        foreach (var block in blocks)
        {
            if (block.Kind == "table") body.Append("<table>" + string.Join("", block.Rows!.Select((r, i) => "<tr>" + string.Join("", r.Select(c => "<" + (i == 0 ? "th" : "td") + ">" + Package.Escape(c) + "</" + (i == 0 ? "th" : "td") + ">")) + "</tr>")) + "</table>");
            else if (block.Kind is "diagram" or "code") body.Append("<pre>" + Package.Escape(block.Kind == "diagram" ? Markdown.DiagramSummary(block.Text) : block.Text) + "</pre>");
            else { var tag = block.Kind.StartsWith('h') ? block.Kind : "p"; body.Append("<" + tag + ">" + (block.Kind == "bullet" ? "• " : "") + Package.Escape(block.Text) + "</" + tag + ">"); }
        }
        File.WriteAllText(path, "<!doctype html><html lang=\"vi\"><meta charset=\"utf-8\"><title>English Learning Platform · Bàn giao</title><style>@font-face{font-family:Noto;src:url(../assets/NotoSans-Regular.ttf)}@page{size:A4;margin:20mm}body{font-family:Noto,Arial,sans-serif;color:#20313f;line-height:1.55;font-size:10.5pt;max-width:900px;margin:auto;padding:30px}h1,h2{color:#087e83}h1{font-size:24pt}h2{font-size:16pt;margin-top:25px}h3{font-size:13pt}h1,h2,h3{break-after:avoid}table{border-collapse:collapse;width:100%;font-size:9.5pt;margin:15px 0}th,td{border:1px solid #cbd7df;padding:8px;text-align:left;vertical-align:top}th{background:#e8f3f2}pre{white-space:pre-wrap;overflow-wrap:anywhere;background:#f1f5f8;padding:12px;font-family:Noto;font-size:9pt}tr{break-inside:avoid}</style><body>" + body + "</body></html>", new UTF8Encoding(false));
    }
}
sealed class TrueTypeFont
{
    public byte[] Bytes { get; }
    readonly Dictionary<string, (int Offset, int Length)> tables = [];
    readonly Dictionary<int, int> map = [];
    readonly int units, metricCount; readonly int[] advances;
    public TrueTypeFont(string path)
    {
        Bytes = File.ReadAllBytes(path);
        for (var i = 0; i < U16(4); i++) { var at = 12 + 16 * i; tables[Encoding.ASCII.GetString(Bytes, at, 4)] = ((int)U32(at + 8), (int)U32(at + 12)); }
        units = U16(tables["head"].Offset + 18); metricCount = U16(tables["hhea"].Offset + 34);
        advances = Enumerable.Range(0, metricCount).Select(i => (int)U16(tables["hmtx"].Offset + 4 * i)).ToArray();
        var cmap = tables["cmap"].Offset;
        for (var i = 0; i < U16(cmap + 2); i++)
        {
            var at = cmap + (int)U32(cmap + 4 + 8 * i + 4); var format = U16(at);
            if (format == 4)
            {
                var count = U16(at + 6) / 2; var ends = at + 14; var starts = ends + 2 * count + 2; var deltas = starts + 2 * count; var offsets = deltas + 2 * count;
                for (var s = 0; s < count; s++) for (var c = U16(starts + 2 * s); c <= U16(ends + 2 * s) && c < 65535; c++)
                {
                    var offset = U16(offsets + 2 * s); var delta = S16(deltas + 2 * s);
                    var glyph = offset == 0 ? (c + delta) & 65535 : U16(offsets + 2 * s + offset + 2 * (c - U16(starts + 2 * s)));
                    if (offset != 0 && glyph != 0) glyph = (glyph + delta) & 65535;
                    if (glyph != 0) map[c] = glyph;
                }
            }
            if (format == 12) for (var g = 0; g < U32(at + 12); g++) { var start = U32(at + 16 + 12 * (int)g); var end = U32(at + 20 + 12 * (int)g); var gid = U32(at + 24 + 12 * (int)g); for (var c = start; c <= end && c < 65536; c++) map[(int)c] = (int)(gid + c - start); }
        }
    }
    ushort U16(int at) => (ushort)((Bytes[at] << 8) | Bytes[at + 1]); short S16(int at) => unchecked((short)U16(at));
    uint U32(int at) => ((uint)Bytes[at] << 24) | ((uint)Bytes[at + 1] << 16) | ((uint)Bytes[at + 2] << 8) | Bytes[at + 3];
    public int Glyph(char c) => map.GetValueOrDefault(c, 0);
    public int Width(char c) => (int)Math.Round(advances[Math.Min(Glyph(c), metricCount - 1)] * 1000d / units);
    public double Measure(string s, double size) => s.Sum(c => Width(c)) * size / 1000d;
    public List<string> Wrap(string s, double width, double size)
    {
        var result = new List<string>(); var current = "";
        foreach (var word in s.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Measure(current.Length == 0 ? word : current + " " + word, size) > width && current.Length > 0) { result.Add(current); current = ""; }
            if (Measure(word, size) > width) foreach (var c in word) { if (Measure(current + c, size) > width && current.Length > 0) { result.Add(current); current = ""; } current += c; }
            else current += (current.Length > 0 ? " " : "") + word;
        }
        if (current.Length > 0 || result.Count == 0) result.Add(current); return result;
    }
}
sealed class PdfFile(TrueTypeFont font, double pageWidth, double pageHeight)
{
    readonly List<byte[]> objects = []; readonly List<int> pageIds = []; readonly HashSet<char> chars = []; readonly List<(StringBuilder Content, int Id)> pages = [];
    public readonly double Width = pageWidth, Height = pageHeight;
    int Add(string s) => Add(Encoding.ASCII.GetBytes(s)); int Add(byte[] bytes) { objects.Add(bytes); return objects.Count; }
    int Stream(byte[] bytes, string extra = "") => Add(Join(Encoding.ASCII.GetBytes($"<< /Length {bytes.Length} {extra} >>\nstream\n"), bytes, Encoding.ASCII.GetBytes("\nendstream")));
    static byte[] Join(params byte[][] arrays) { var result = new byte[arrays.Sum(a => a.Length)]; var at = 0; foreach (var array in arrays) { array.CopyTo(result, at); at += array.Length; } return result; }
    public StringBuilder NewPage() { var page = new StringBuilder(); pages.Add((page, pages.Count + 1)); return page; }
    public void Text(StringBuilder page, string text, double x, double y, double size, string rgb = "0.125 0.192 0.247")
    {
        text = text.Replace("→", " > ").Replace("≥", ">=").Replace("≤", "<=");
        foreach (var c in text) { chars.Add(c); if (font.Glyph(c) == 0 && !char.IsWhiteSpace(c)) throw new InvalidOperationException($"Font missing U+{(int)c:X4}"); }
        var hex = Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text));
        page.Append(CultureInfo.InvariantCulture, $"BT /F1 {size:0.##} Tf {rgb} rg 1 0 0 1 {x:0.##} {Height-y:0.##} Tm <{hex}> Tj ET\n");
    }
    public void Rect(StringBuilder page, double x, double y, double width, double height, string rgb) => page.Append(CultureInfo.InvariantCulture, $"{rgb} rg {x:0.##} {Height-y-height:0.##} {width:0.##} {height:0.##} re f\n");
    public void Save(string path)
    {
        var catalog = Add(""); var pagesId = Add("");
        var fontStream = Stream(font.Bytes, $"/Length1 {font.Bytes.Length}");
        var descriptor = Add($"<< /Type /FontDescriptor /FontName /NotoSans /Flags 32 /FontBBox [-621 -389 2800 1067] /ItalicAngle 0 /Ascent 1069 /Descent -293 /CapHeight 714 /StemV 80 /FontFile2 {fontStream} 0 R >>");
        var max = chars.Max(c => (int)c); var cidMap = new byte[(max + 1) * 2]; foreach (var c in chars) { var glyph = font.Glyph(c); cidMap[2*c] = (byte)(glyph >> 8); cidMap[2*c+1] = (byte)glyph; }
        var mapStream = Stream(cidMap); var widths = string.Join(" ", chars.Order().Select(c => $"{(int)c} [{font.Width(c)}]"));
        var cid = Add($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /NotoSans /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /FontDescriptor {descriptor} 0 R /CIDToGIDMap {mapStream} 0 R /DW 600 /W [{widths}] >>");
        var unicode = new StringBuilder("/CIDInit /ProcSet findresource begin 12 dict begin begincmap /CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def /CMapName /Adobe-Identity-UCS def /CMapType 2 def 1 begincodespacerange <0000> <FFFF> endcodespacerange\n");
        foreach (var chunk in chars.Order().Chunk(100)) { unicode.Append(chunk.Length + " beginbfchar\n"); foreach (var c in chunk) unicode.Append($"<{(int)c:X4}> <{(int)c:X4}>\n"); unicode.Append("endbfchar\n"); }
        unicode.Append("endcmap CMapName currentdict /CMap defineresource pop end end"); var uniStream = Stream(Encoding.ASCII.GetBytes(unicode.ToString()));
        var type0 = Add($"<< /Type /Font /Subtype /Type0 /BaseFont /NotoSans /Encoding /Identity-H /DescendantFonts [{cid} 0 R] /ToUnicode {uniStream} 0 R >>");
        foreach (var (page, _) in pages) { var content = Stream(Encoding.ASCII.GetBytes(page.ToString())); pageIds.Add(Add(FormattableString.Invariant($"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 {Width} {Height}] /Resources << /Font << /F1 {type0} 0 R >> >> /Contents {content} 0 R >>"))); }
        objects[catalog-1] = Encoding.ASCII.GetBytes($"<< /Type /Catalog /Pages {pagesId} 0 R >>"); objects[pagesId-1] = Encoding.ASCII.GetBytes($"<< /Type /Pages /Kids [{string.Join(" ", pageIds.Select(i => i + " 0 R"))}] /Count {pageIds.Count} >>");
        using var output = File.Create(path); var offsets = new List<long> { 0 }; Write("%PDF-1.7\n% generated locally; Unicode font embedded\n");
        for (var i = 0; i < objects.Count; i++) { offsets.Add(output.Position); Write($"{i+1} 0 obj\n"); output.Write(objects[i]); Write("\nendobj\n"); }
        var xref = output.Position; Write($"xref\n0 {objects.Count+1}\n0000000000 65535 f \n"); foreach (var offset in offsets.Skip(1)) Write($"{offset:0000000000} 00000 n \n"); Write($"trailer\n<< /Size {objects.Count+1} /Root {catalog} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        void Write(string s) => output.Write(Encoding.ASCII.GetBytes(s));
    }
}
static class Pdf
{
    public static void WriteDocument(string path, List<Block> blocks, TrueTypeFont font)
    {
        var pdf = new PdfFile(font, 595.28, 841.89); StringBuilder page = null!; var y = 0d; var count = 0;
        var documentTitle = blocks.First(b => b.Kind == "h1").Text;
        void NewPage() { page = pdf.NewPage(); count++; y = 74; pdf.Rect(page, 0, 0, pdf.Width, 7, "0.031 0.494 0.514"); pdf.Text(page, "ENGLISH LEARNING PLATFORM  /  HỒ SƠ BÀN GIAO", 48, 34, 8, "0.4 0.46 0.51"); pdf.Text(page, $"Candidate · 11/10/2026 · Asia/Saigon     |     {count}", 48, 807, 8, "0.4 0.46 0.51"); }
        void Ensure(double height) { if (y + height > 778) NewPage(); }
        void Lines(string text, double size, double spacing, string color, double indent = 0, bool background = false)
        {
            foreach (var original in text.Split('\n')) foreach (var line in font.Wrap(original, 499 - indent, size)) { Ensure(spacing); if (background) pdf.Rect(page, 46 + indent, y - size - 2, 503-indent, spacing, "0.945 0.961 0.973"); pdf.Text(page, line, 48 + indent, y, size, color); y += spacing; }
        }
        NewPage();
        foreach (var block in blocks)
        {
            if (block.Kind.StartsWith('h')) { var size = block.Kind == "h1" ? 22d : block.Kind == "h2" ? 14d : 12d; Ensure(68); if (block.Kind != "h1") y += 12; Lines(block.Text, size, size+7, "0.031 0.494 0.514"); y += 3; }
            else if (block.Kind == "table")
            {
                var rows = block.Rows!; var columns = rows.Max(r => r.Length); var col = 499d / columns;
                for (var index = 0; index < rows.Count; index++)
                {
                    var wrapped = rows[index].Select(c => font.Wrap(c, col-14, 8.8)).ToArray(); var height = wrapped.Max(w => w.Count) * 13 + 14; Ensure(height+8);
                    for (var j = 0; j < columns; j++) { pdf.Rect(page, 48+j*col, y-9, col-1, height, index == 0 ? "0.91 0.953 0.949" : "0.969 0.976 0.98"); if (j < wrapped.Length) for (var k = 0; k < wrapped[j].Count; k++) pdf.Text(page, wrapped[j][k], 55+j*col, y+3+k*13, 8.8); }
                    y += height + 1;
                }
                y += 11;
            }
            else if (block.Kind is "code" or "diagram") { Ensure(40); Lines(block.Kind == "diagram" ? Markdown.DiagramSummary(block.Text) : block.Text, 8.6, 13, "0.125 0.192 0.247", 8, true); y += 12; }
            else { Lines((block.Kind == "bullet" ? "• " : "") + block.Text, 10.3, 16.2, "0.125 0.192 0.247", block.Kind == "bullet" ? 8 : 0); y += 7; }
        }
        pdf.Save(path);
    }
    public static void WriteSlides(string path, List<Slide> slides, TrueTypeFont font)
    {
        var pdf = new PdfFile(font, 960, 540);
        for (var i = 0; i < slides.Count; i++)
        {
            var page = pdf.NewPage(); pdf.Rect(page, 0, 0, 960, 540, "0.043 0.086 0.129"); pdf.Rect(page, 60, 55, 42, 5, "0.224 0.831 0.765"); pdf.Text(page, "ENGLISH LEARNING PLATFORM", 60, 90, 10, "0.51 0.62 0.69");
            var title = font.Wrap(slides[i].Title, 830, 32); var ty = 158d; foreach (var line in title) { pdf.Text(page, line, 60, ty, 32, "1 1 1"); ty += 42; }
            var by = Math.Max(235, ty+35); foreach (var bullet in slides[i].Bullets) { pdf.Rect(page, 62, by-13, 6, 6, "0.224 0.831 0.765"); foreach (var line in font.Wrap(bullet, 800, 19)) { pdf.Text(page, line, 86, by, 19, "0.85 0.89 0.92"); by += 28; } by += 16; }
            pdf.Text(page, ".NET  /  LẬP TRÌNH HIỆN ĐẠI  /  QUẢN LÝ DỰ ÁN", 60, 505, 9, "0.51 0.62 0.69"); pdf.Text(page, $"{i+1:00} / {slides.Count}", 842, 505, 10, "0.224 0.831 0.765");
        }
        pdf.Save(path);
    }
}
static class Pptx
{
    static string Shape(int id, string name, long x, long y, long width, long height, string fill, string text = "", int size = 2400, string color = "FFFFFF", bool bold = false)
    {
        var paragraphs = string.Join("", text.Split('\n').Select(line => $"<a:p><a:pPr/><a:r><a:rPr lang=\"vi-VN\" sz=\"{size}\" b=\"{(bold ? 1 : 0)}\"><a:solidFill><a:srgbClr val=\"{color}\"/></a:solidFill><a:latin typeface=\"Noto Sans\"/><a:ea typeface=\"Noto Sans\"/><a:cs typeface=\"Noto Sans\"/></a:rPr><a:t>{Package.Escape(line)}</a:t></a:r><a:endParaRPr lang=\"vi-VN\"/></a:p>"));
        return $"<p:sp><p:nvSpPr><p:cNvPr id=\"{id}\" name=\"{name}\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x=\"{x}\" y=\"{y}\"/><a:ext cx=\"{width}\" cy=\"{height}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>{(fill == "none" ? "<a:noFill/>" : "<a:solidFill><a:srgbClr val=\"" + fill + "\"/></a:solidFill>")}<a:ln><a:noFill/></a:ln></p:spPr><p:txBody><a:bodyPr wrap=\"square\" lIns=\"0\" tIns=\"0\" rIns=\"0\" bIns=\"0\"/><a:lstStyle/>{paragraphs}</p:txBody></p:sp>";
    }
    const string Ns = "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\"";
    const string Group = "<p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>";
    static string Rels(string body) => Package.Header + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + body + "</Relationships>";
    static string Rel(string id, string type, string target) => $"<Relationship Id=\"{id}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/{type}\" Target=\"{target}\"/>";
    public static void Write(string path, List<Slide> slides)
    {
        File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var overrides = "<Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/><Override PartName=\"/ppt/slideMasters/slideMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml\"/><Override PartName=\"/ppt/slideLayouts/slideLayout1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml\"/><Override PartName=\"/ppt/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/><Override PartName=\"/ppt/notesMasters/notesMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.notesMaster+xml\"/>";
        for (var i = 1; i <= slides.Count; i++) overrides += $"<Override PartName=\"/ppt/slides/slide{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/><Override PartName=\"/ppt/notesSlides/notesSlide{i}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.notesSlide+xml\"/>";
        Package.Put(zip, "[Content_Types].xml", Package.Header + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" + overrides + "</Types>");
        Package.Put(zip, "_rels/.rels", Rels(Rel("rId1", "officeDocument", "ppt/presentation.xml")));
        Package.Put(zip, "ppt/presentation.xml", Package.Header + $"<p:presentation {Ns}><p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst><p:notesMasterIdLst><p:notesMasterId r:id=\"rId2\"/></p:notesMasterIdLst><p:sldIdLst>" + string.Join("", Enumerable.Range(1, slides.Count).Select(i => $"<p:sldId id=\"{255+i}\" r:id=\"rId{i+2}\"/>")) + "</p:sldIdLst><p:sldSz cx=\"12192000\" cy=\"6858000\" type=\"screen16x9\"/><p:notesSz cx=\"6858000\" cy=\"9144000\"/></p:presentation>");
        Package.Put(zip, "ppt/_rels/presentation.xml.rels", Rels(Rel("rId1", "slideMaster", "slideMasters/slideMaster1.xml") + Rel("rId2", "notesMaster", "notesMasters/notesMaster1.xml") + string.Join("", Enumerable.Range(1, slides.Count).Select(i => Rel("rId" + (i+2), "slide", $"slides/slide{i}.xml")))));
        Package.Put(zip, "ppt/slideMasters/slideMaster1.xml", Package.Header + $"<p:sldMaster {Ns}><p:cSld><p:spTree>{Group}</p:spTree></p:cSld><p:clrMap accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" bg1=\"lt1\" bg2=\"lt2\" folHlink=\"folHlink\" hlink=\"hlink\" tx1=\"dk1\" tx2=\"dk2\"/><p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst><p:txStyles><p:titleStyle/><p:bodyStyle/><p:otherStyle/></p:txStyles></p:sldMaster>");
        Package.Put(zip, "ppt/slideMasters/_rels/slideMaster1.xml.rels", Rels(Rel("rId1", "slideLayout", "../slideLayouts/slideLayout1.xml") + Rel("rId2", "theme", "../theme/theme1.xml")));
        Package.Put(zip, "ppt/slideLayouts/slideLayout1.xml", Package.Header + $"<p:sldLayout {Ns} type=\"blank\" preserve=\"1\"><p:cSld name=\"Blank\"><p:spTree>{Group}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>");
        Package.Put(zip, "ppt/slideLayouts/_rels/slideLayout1.xml.rels", Rels(Rel("rId1", "slideMaster", "../slideMasters/slideMaster1.xml")));
        Package.Put(zip, "ppt/notesMasters/notesMaster1.xml", Package.Header + $"<p:notesMaster {Ns}><p:cSld><p:spTree>{Group}</p:spTree></p:cSld><p:clrMap accent1=\"accent1\" accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" accent6=\"accent6\" bg1=\"lt1\" bg2=\"lt2\" folHlink=\"folHlink\" hlink=\"hlink\" tx1=\"dk1\" tx2=\"dk2\"/><p:notesStyle/></p:notesMaster>");
        Package.Put(zip, "ppt/notesMasters/_rels/notesMaster1.xml.rels", Rels(Rel("rId1", "theme", "../theme/theme1.xml")));
        var colors = new[] { "0B1621", "FFFFFF", "20313F", "E8F3F2", "39D4C3", "087E83", "A6C3D3", "F6BE52", "C9A6EB", "ED978F", "39D4C3", "A6C3D3" }; var names = new[] { "dk1", "lt1", "dk2", "lt2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink" };
        Package.Put(zip, "ppt/theme/theme1.xml", Package.Header + "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"English Learning\"><a:themeElements><a:clrScheme name=\"Learning\">" + string.Join("", names.Select((n, i) => $"<a:{n}><a:srgbClr val=\"{colors[i]}\"/></a:{n}>")) + "</a:clrScheme><a:fontScheme name=\"Noto\"><a:majorFont><a:latin typeface=\"Noto Sans\"/><a:ea typeface=\"Noto Sans\"/><a:cs typeface=\"Noto Sans\"/></a:majorFont><a:minorFont><a:latin typeface=\"Noto Sans\"/><a:ea typeface=\"Noto Sans\"/><a:cs typeface=\"Noto Sans\"/></a:minorFont></a:fontScheme><a:fmtScheme name=\"Minimal\"><a:fillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:fillStyleLst><a:lnStyleLst>" + string.Concat(Enumerable.Repeat("<a:ln w=\"9525\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/></a:ln>", 3)) + "</a:lnStyleLst><a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst><a:bgFillStyleLst>" + string.Concat(Enumerable.Repeat("<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>", 3)) + "</a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>");
        for (var i = 0; i < slides.Count; i++)
        {
            var s = slides[i]; var shapes = new StringBuilder(Group);
            shapes.Append(Shape(2, "Accent", 762000, 698500, 533400, 63500, "39D4C3"));
            shapes.Append(Shape(3, "Brand", 762000, 1016000, 10541000, 254000, "none", "ENGLISH LEARNING PLATFORM", 1000, "829FAF"));
            shapes.Append(Shape(4, "Title", 762000, 1651000, 10668000, 1016000, "none", s.Title, 3200, "FFFFFF", true));
            for (var b = 0; b < s.Bullets.Count; b++) { shapes.Append(Shape(5+2*b, "BulletAccent", 787400, 3060700+b*660400, 76200, 76200, "39D4C3")); shapes.Append(Shape(6+2*b, "Bullet", 1092200, 2959100+b*660400, 10287000, 584200, "none", s.Bullets[b], 2050, "D9E3EB")); }
            shapes.Append(Shape(20, "Footer", 762000, 6350000, 9855200, 254000, "none", ".NET  /  LẬP TRÌNH HIỆN ĐẠI  /  QUẢN LÝ DỰ ÁN", 900, "829FAF")); shapes.Append(Shape(21, "SlideNumber", 10617200, 6337300, 812800, 304800, "none", $"{i+1:00} / {slides.Count}", 1000, "39D4C3"));
            Package.Put(zip, $"ppt/slides/slide{i+1}.xml", Package.Header + $"<p:sld {Ns}><p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val=\"0B1621\"/></a:solidFill><a:effectLst/></p:bgPr></p:bg><p:spTree>{shapes}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>");
            Package.Put(zip, $"ppt/slides/_rels/slide{i+1}.xml.rels", Rels(Rel("rId1", "slideLayout", "../slideLayouts/slideLayout1.xml") + Rel("rId2", "notesSlide", $"../notesSlides/notesSlide{i+1}.xml")));
            var note = Shape(2, "Speaker Notes", 685800, 2743200, 5486400, 5486400, "none", s.Notes, 1400, "20313F").Replace("<p:nvPr/>", "<p:nvPr><p:ph type=\"body\" idx=\"1\"/></p:nvPr>");
            Package.Put(zip, $"ppt/notesSlides/notesSlide{i+1}.xml", Package.Header + $"<p:notes {Ns}><p:cSld><p:spTree>{Group}{note}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:notes>");
            Package.Put(zip, $"ppt/notesSlides/_rels/notesSlide{i+1}.xml.rels", Rels(Rel("rId1", "notesMaster", "../notesMasters/notesMaster1.xml") + Rel("rId2", "slide", $"../slides/slide{i+1}.xml")));
        }
    }
}
