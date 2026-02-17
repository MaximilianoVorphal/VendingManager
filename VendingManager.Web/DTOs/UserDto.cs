using System.ComponentModel.DataAnnotations;

namespace VendingManager.Web.DTOs;

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
}

public class CreateUserDto
{
    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MinLength(4, ErrorMessage = "La contraseña debe tener al menos 4 caracteres")]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "User";
}

public class UpdateUserDto
{
    [Required]
    public string Role { get; set; } = "User";

    [MinLength(4, ErrorMessage = "La contraseña debe tener al menos 4 caracteres")]
    public string? Password { get; set; } // Optional: only if changing
}
