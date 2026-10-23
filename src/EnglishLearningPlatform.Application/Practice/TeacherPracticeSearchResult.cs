namespace EnglishLearningPlatform.Application.Practice;

/// <summary>
/// Kết quả tìm kiếm Practice dành cho Teacher.
/// Chỉ chứa dữ liệu trả về, không truy vấn database.
/// </summary>
public sealed class TeacherPracticeSearchResult
{
    // Các Practice thuộc trang đang xem.
    public IReadOnlyList<TeacherPracticeListItem> Items { get; }

    // Tổng số Practice khớp bộ lọc, tính trước khi phân trang.
    public int TotalCount { get; }

    // Số trang hiện tại, bắt đầu từ 1.
    public int PageNumber { get; }

    // Số Practice tối đa trên mỗi trang.
    public int PageSize { get; }

    public TeacherPracticeSearchResult(
        IReadOnlyList<TeacherPracticeListItem> items,
        int totalCount,
        int pageNumber,
        int pageSize)
    {
        // Không nhận null. Không có kết quả thì truyền danh sách rỗng.
        if (items == null)
        {
            throw new ArgumentNullException(nameof(items));
        }

        if (totalCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalCount),
                "Tổng số Practice không được âm.");
        }

        if (pageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageNumber),
                "Số trang phải bắt đầu từ 1.");
        }

        if (pageSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pageSize),
                "Số Practice trên mỗi trang phải lớn hơn 0.");
        }

        // Sao chép danh sách để việc thêm/xóa trên danh sách đầu vào
        // không làm thay đổi danh sách của kết quả đã tạo.
        // Đây không phải sao chép sâu từng TeacherPracticeListItem.
        List<TeacherPracticeListItem> itemsCopy =
            new List<TeacherPracticeListItem>(items);

        Items = itemsCopy.AsReadOnly();

        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }

    // Tính tổng số trang để giao diện tạo nút chuyển trang.
    public int TotalPages
    {
        get
        {
            if (TotalCount == 0)
            {
                return 0;
            }

            int totalPages = TotalCount / PageSize;

            // Nếu còn dư Practice thì cần thêm một trang.
            if (TotalCount % PageSize > 0)
            {
                totalPages = totalPages + 1;
            }

            return totalPages;
        }
    }
}