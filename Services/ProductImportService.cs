using ClosedXML.Excel;

namespace SmartInventory.Services;

/// <summary>
/// Парсинг файлов базы продуктов (CSV / XLSX) в строки для предпросмотра
/// с валидацией. Формат: название;категория;ед.изм;срок хранения ч;закуп. цена.
/// </summary>
public static class ProductImportService
{
    /// <summary>Одна распарсенная строка предпросмотра.</summary>
    public sealed record ImportRow
    {
        public required string Name { get; set; }
        public required string Category { get; set; }
        public required string Unit { get; set; }
        public double ShelfHours { get; set; }
        public decimal PurchasePrice { get; set; }
        /// <summary>Ошибки по строке (пустой список = строка валидна).</summary>
        public List<string> Errors { get; init; } = [];
    }

    /// <summary>Прочитать файл (csv/xlsx) в список строк предпросмотра.
    /// Парсинг только текстовый — валидация отдельным шагом.</summary>
    public static List<ImportRow> Parse(Stream stream, string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".xlsx" or ".xlsm" or ".xltx" or ".xltm" => ParseXlsx(stream),
            _ => ParseCsv(stream),
        };
    }

    // ---------- CSV ----------

    private static List<ImportRow> ParseCsv(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var rows = new List<ImportRow>();
        string? line;
        var first = true;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            // Разделитель авто: ; или , — смотрим по первой строке
            var sep = DetectSeparator(line);
            var cells = line.Split(sep).Select(c => c.Trim().Trim('"')).ToArray();
            // Пропускаем возможный заголовок (первая строка без чисел в сроке/цене)
            if (first && !LooksLikeData(cells)) { first = false; continue; }
            first = false;
            rows.Add(ToRow(cells));
        }
        return rows;
    }

    private static char DetectSeparator(string line)
    {
        var semi = line.Count(c => c == ';');
        var comma = line.Count(c => c == ',');
        return semi >= comma ? ';' : ',';
    }

    // Строка похожа на данные, если срок/цена парсятся числом или поля вообще заполнены
    private static bool LooksLikeData(string[] cells)
    {
        if (cells.Length >= 4 && double.TryParse(cells[3], out _)) return true;
        if (cells.Length >= 5 && decimal.TryParse(cells[4], out _)) return true;
        return cells.Length >= 3 && !string.IsNullOrWhiteSpace(cells[0]);
    }

    // ---------- XLSX ----------

    private static List<ImportRow> ParseXlsx(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var rows = new List<ImportRow>();
        var first = true;
        foreach (var row in ws.RowsUsed().Take(500))
        {
            var cells = Enumerable.Range(1, 5)
                .Select(i => row.Cell(i).GetString().Trim())
                .ToArray();
            if (cells.All(string.IsNullOrWhiteSpace)) continue;
            if (first && !LooksLikeData(cells)) { first = false; continue; }
            first = false;
            rows.Add(ToRow(cells));
        }
        return rows;
    }

    // ---------- валидация ----------

    private static ImportRow ToRow(string[] cells)
    {
        var row = new ImportRow
        {
            Name = cells.ElementAtOrDefault(0) ?? "",
            Category = cells.ElementAtOrDefault(1) ?? "",
            Unit = cells.ElementAtOrDefault(2) ?? "",
        };
        // срок (часы)
        var shelf = cells.ElementAtOrDefault(3);
        if (!double.TryParse(NormNum(shelf), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var sh)
            && !double.TryParse(NormNum(shelf), out sh))
        {
            sh = 0;
        }
        row.ShelfHours = sh;
        // цена
        var price = cells.ElementAtOrDefault(4);
        if (!decimal.TryParse(NormNum(price), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var pp)
            && !decimal.TryParse(NormNum(price), out pp))
        {
            pp = 0;
        }
        row.PurchasePrice = pp;
        return row;
    }

    // «1 234,56» / «1,234.56» → «1234.56»
    private static string NormNum(string? s)
        => string.IsNullOrWhiteSpace(s) ? "" : s.Replace(" ", "").Replace(" ", "").Replace(',', '.');

    /// <summary>Применить валидацию к строкам предпросмотра:
    /// отсутствует единица, срок ≤ 0, дубликат существующего продукта.</summary>
    public static void Validate(List<ImportRow> rows, IEnumerable<string> existingNames)
    {
        var existing = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            r.Errors.Clear();
            if (string.IsNullOrWhiteSpace(r.Name)) r.Errors.Add("нет названия");
            if (string.IsNullOrWhiteSpace(r.Category)) r.Errors.Add("нет категории");
            if (string.IsNullOrWhiteSpace(r.Unit)) r.Errors.Add("нет единицы");
            if (r.ShelfHours <= 0) r.Errors.Add("срок ≤ 0");
            if (r.PurchasePrice < 0) r.Errors.Add("цена < 0");
            var key = (r.Name ?? "").Trim();
            if (key.Length > 0)
            {
                if (existing.Contains(key)) r.Errors.Add("дубликат существующего");
                else if (!seen.Add(key)) r.Errors.Add("дубликат в файле");
            }
        }
    }

    // ---------- сериализация для предпросмотра (TempData → форма) ----------

    /// <summary>Сериализовать строки в скрытое поле формы (JSON-массив).</summary>
    public static string SerializeRows(IEnumerable<ImportRow> rows)
        => System.Text.Json.JsonSerializer.Serialize(
            rows.Select(r => new { r.Name, r.Category, r.Unit, r.ShelfHours, r.PurchasePrice }));

    /// <summary>Десериализовать отредактированные пользователем строки обратно.</summary>
    public static List<ImportRow> DeserializeRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var rows = new List<ImportRow>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                rows.Add(new ImportRow
                {
                    Name = el.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "",
                    Category = el.TryGetProperty("Category", out var c) ? c.GetString() ?? "" : "",
                    Unit = el.TryGetProperty("Unit", out var u) ? u.GetString() ?? "" : "",
                    ShelfHours = el.TryGetProperty("ShelfHours", out var s) && s.TryGetDouble(out var sh) ? sh : 0,
                    PurchasePrice = el.TryGetProperty("PurchasePrice", out var p) && p.TryGetDecimal(out var pp) ? pp : 0,
                });
            }
            return rows;
        }
        catch { return []; }
    }
}
