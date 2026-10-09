namespace EnglishLearningPlatform.Application.QuestionBank;
public sealed class QuestionSearchResult
{
    // Danh sách các câu hỏi phù hợp với bộ lọc tìm kiếm.
    public IReadOnlyList<QuestionListItem> Items {get;}
    // Tổng số câu hỏi phù hợp với bộ lọc tìm kiếm, không phụ thuộc vào phân trang.
    public int TotalCount {get;}
    // Trang đang xem, bắt đầu từ 1.
    public int PageNumber {get;}
    // Số câu tối đa trên mỗi trang
    public int PageSize {get;}

    public QuestionSearchResult(IReadOnlyList<QuestionListItem> items, int totalCount, int pageNumber, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
        if (items == null){
            throw new ArgumentNullException(nameof(items));
        }
        if (totalCount < 0){
            throw new ArgumentOutOfRangeException(nameof(totalCount), "Total count must be at least 0.");
        }
        if (pageNumber < 1){
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page number must be at least 1.");
        }
        if (pageSize < 1){
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be at least 1.");
        }
        // Sao chép danh sách để việc thêm/xóa trên danh sách gốc
        // không làm thay đổi tập câu hỏi của kết quả đã tạo.
        // Đây không phải sao chép sâu từng QuestionListItem.
        List<QuestionListItem> itemsCopy = new List<QuestionListItem>(items);
        Items = itemsCopy.AsReadOnly();
        TotalCount = totalCount;
        PageNumber = pageNumber;
        PageSize = pageSize;
    }
    // Tính số trang cần thiết từ tổng số câu và kích thước trang.
    // Quy ước của kết quả này: không có câu hỏi thì có 0 trang kết quả.
    public int TotalPages
    {
        get
        {
            if (TotalCount == 0)
            {
                return 0;
            }
            int totalpages = TotalCount / PageSize;
            if (TotalCount % PageSize > 0)
            {
                totalpages += 1;
            }
            return totalpages;
        }
    }


}
