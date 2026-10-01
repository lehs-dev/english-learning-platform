namespace EnglishLearningPlatform.Domain.Authorization;

/// <summary>
/// Tài nguyên có chủ sở hữu là một người dùng (ví dụ: Course thuộc về Teacher tạo ra nó).
/// Dùng cho authorization theo tài nguyên: chỉ chủ sở hữu mới được thao tác.
/// </summary>
public interface IOwnedResource
{
    Guid OwnerUserId { get; }
}
