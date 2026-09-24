using EnglishLearningPlatform.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EnglishLearningPlatform.Infrastructure.Persistence.Configurations;

public sealed class IdentityRoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> builder)
    {
        builder.HasData(
            new IdentityRole<Guid>
            {
                Id = Guid.Parse("b1edc10a-02c9-4b16-8f6b-000000000001"),
                Name = AppRoles.Student,
                NormalizedName = "STUDENT",
                ConcurrencyStamp = "f93b8ecb-9c3c-4bbd-9d19-000000000001"
            },
            new IdentityRole<Guid>
            {
                Id = Guid.Parse("b1edc10a-02c9-4b16-8f6b-000000000002"),
                Name = AppRoles.Teacher,
                NormalizedName = "TEACHER",
                ConcurrencyStamp = "f93b8ecb-9c3c-4bbd-9d19-000000000002"
            },
            new IdentityRole<Guid>
            {
                Id = Guid.Parse("b1edc10a-02c9-4b16-8f6b-000000000003"),
                Name = AppRoles.Admin,
                NormalizedName = "ADMIN",
                ConcurrencyStamp = "f93b8ecb-9c3c-4bbd-9d19-000000000003"
            }
        );
    }
}
