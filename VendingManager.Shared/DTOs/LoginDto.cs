using System.ComponentModel.DataAnnotations;

namespace VendingManager.Shared.DTOs;

// Replicated in Client to avoid complex sharing setup for this task.
// Namespace kept as VendingManager.Core.DTOs to match code in Login.razor if possible, 
// OR we can change namespace to VendingManager.Web.DTOs and update Login.razor.
// Let's use VendingManager.Core.DTOs to minimize changes in Login.razor if it was already trying to use that.
// But wait, Login.razor has `@using VendingManager.Core.DTOs`.
// If I define this class in the Client project under that namespace, it will work.

public class LoginDto
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
