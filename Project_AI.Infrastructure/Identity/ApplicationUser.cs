using Microsoft.AspNetCore.Identity;

namespace Project_AI.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = "";
}
