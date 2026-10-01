using Microsoft.AspNetCore.Authorization;

namespace EnglishLearningPlatform.Application.Authorization;

/// <summary>
/// Yêu cầu người dùng hiện tại là chủ sở hữu của tài nguyên
/// (<see cref="Domain.Authorization.IOwnedResource"/>).
/// Dùng qua <c>IAuthorizationService.AuthorizeAsync(User, resource, new ResourceOwnerRequirement())</c>
/// trong controller/service, vì đây là authorization theo tài nguyên, không gắn bằng attribute.
/// </summary>
public sealed class ResourceOwnerRequirement : IAuthorizationRequirement;
