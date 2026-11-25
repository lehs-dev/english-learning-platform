using System.Text;
using EnglishLearningPlatform.Application.Flashcards;
using EnglishLearningPlatform.Domain.Entities;
using Microsoft.VisualBasic.FileIO;

namespace EnglishLearningPlatform.Web.Infrastructure;

internal static class FlashcardCsv
{
    public const int MaxFileBytes = 1024 * 1024;
    public const int MaxRequestBytes = MaxFileBytes + 64 * 1024;
    private static readonly string[] Columns = ["FrontText", "BackText", "Example", "Pronunciation", "AudioResourceUrl"];

    public static string Example =>
        $"\uFEFF{string.Join(',', Columns)}\r\nlearn,học,I learn English.,/lɜːn/,\r\n";

    public static async Task<(IReadOnlyList<SaveFlashcardRequest>? Cards, string? Error)> ReadAsync(
        IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length is <= 0 or > MaxFileBytes)
            return (null, "Chọn tệp CSV UTF-8 có dữ liệu, tối đa 1 MiB.");

        try
        {
            using var reader = new StreamReader(file.OpenReadStream(), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
            var text = await reader.ReadToEndAsync(ct);
            using var parser = new TextFieldParser(new StringReader(text.TrimStart('\uFEFF'))) { TrimWhiteSpace = false };
            parser.SetDelimiters(",");

            var header = parser.ReadFields();
            if (header is null || header.Length is < 2 or > 5 ||
                !header.Select(field => field.Trim()).SequenceEqual(Columns.Take(header.Length), StringComparer.OrdinalIgnoreCase))
                return (null, "Dòng đầu cần có ít nhất hai cột FrontText, BackText. Dùng tệp mẫu để thêm các cột tùy chọn.");

            var cards = new List<SaveFlashcardRequest>();
            while (!parser.EndOfData)
            {
                ct.ThrowIfCancellationRequested();
                var line = parser.LineNumber;
                var fields = parser.ReadFields();
                if (fields is null) break;
                if (fields.Length != header.Length)
                    return (null, $"Dòng {line} cần có {header.Length} cột. Đặt nội dung chứa dấu phẩy trong dấu ngoặc kép.");
                if (cards.Count >= FlashcardDeck.MaxCards) return (null, "Tệp CSV chứa nhiều hơn 1.000 thẻ.");
                cards.Add(new(fields[0], fields[1], fields.ElementAtOrDefault(2),
                    fields.ElementAtOrDefault(3), fields.ElementAtOrDefault(4)));
            }

            return cards.Count > 0 ? (cards, null) : (null, "Tệp CSV chưa có thẻ nào.");
        }
        catch (DecoderFallbackException)
        {
            return (null, "Hãy lưu tệp CSV với bảng mã UTF-8 rồi tải lại.");
        }
        catch (MalformedLineException exception)
        {
            return (null, $"Sai định dạng CSV ở dòng {exception.LineNumber}. Kiểm tra dấu phẩy và dấu ngoặc kép.");
        }
    }
}
