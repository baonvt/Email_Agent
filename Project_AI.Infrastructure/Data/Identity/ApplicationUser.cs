using Microsoft.AspNetCore.Identity;

namespace Project_AI.Infrastructure.Data.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
}
