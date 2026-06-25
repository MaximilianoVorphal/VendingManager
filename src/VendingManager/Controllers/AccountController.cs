using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using VendingManager.Shared.DTOs;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;

namespace VendingManager.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AccountController(ApplicationDbContext context, IAuditService auditService) : ControllerBase
    {
        [HttpPost("login")]
        [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("LoginPolicy")]
        public async Task<ActionResult<string>> Login(LoginDto loginDto)
        {
            var user = await context.Users
                .FirstOrDefaultAsync(u => u.Username == loginDto.Username);

            if (user == null || !BCrypt.Net.BCrypt.Verify(loginDto.Password, user.PasswordHash))
            {
                // Timing delay to mitigate user-enumeration attacks — uses CSPRNG (L-3).
                await Task.Delay(System.Security.Cryptography.RandomNumberGenerator.GetInt32(100, 300));
                return Unauthorized("Credenciales inválidas");
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = true,
                RedirectUri = "/"
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            await auditService.RegistrarAccionAsync(user.Username, "Login", "Inicio de sesión exitoso");

            return Ok("Login exitoso");
        }

        [HttpGet("~/logout")] // Ruta absoluta para coincidir con el enlace del frontend
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/");
        }

        [HttpGet("user")]
        public IActionResult GetUser()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return Ok(new 
                { 
                    User.Identity.Name, 
                    Role = User.FindFirst(ClaimTypes.Role)?.Value 
                });
            }
            return Ok(new { Name = (string?)null });
        }
    }
}
