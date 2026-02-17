using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Constants;

namespace VendingManager.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = Roles.Admin)]
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditService _auditService;

        public UsersController(ApplicationDbContext context, IAuditService auditService)
        {
            _context = context;
            _auditService = auditService;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserDto>>> GetUsers()
        {
            return await _context.Users
                .Select(u => new UserDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Role = u.Role
                })
                .ToListAsync();
        }

        [HttpPost]
        public async Task<ActionResult<UserDto>> CreateUser(CreateUserDto createUserDto)
        {
            if (await _context.Users.AnyAsync(u => u.Username == createUserDto.Username))
            {
                return BadRequest("El nombre de usuario ya existe.");
            }

            if (createUserDto.Role != Roles.Admin && createUserDto.Role != Roles.User)
            {
                return BadRequest("Rol inválido.");
            }

            var user = new User
            {
                Username = createUserDto.Username,
                Role = createUserDto.Role,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(createUserDto.Password)
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            await _auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Admin", "Crear Usuario", $"Usuario creado: {user.Username} ({user.Role})");

            return CreatedAtAction(nameof(GetUsers), new { id = user.Id }, new UserDto
            {
                Id = user.Id,
                Username = user.Username,
                Role = user.Role
            });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, UpdateUserDto updateUserDto)
        {
            if (updateUserDto.Role != Roles.Admin && updateUserDto.Role != Roles.User)
            {
                return BadRequest("Rol inválido.");
            }

            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var oldRole = user.Role;
            user.Role = updateUserDto.Role;

            var details = $"Rol actualizado: {oldRole} -> {user.Role}.";

            if (!string.IsNullOrEmpty(updateUserDto.Password))
            {
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(updateUserDto.Password);
                details += " Contraseña actualizada.";
            }

            try
            {
                await _context.SaveChangesAsync();
                await _auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Admin", "Actualizar Usuario", $"Usuario: {user.Username}. {details}");
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!UserExists(id)) return NotFound();
                else throw;
            }

            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            if (user.Username == User.Identity?.Name)
            {
                return BadRequest("No puedes eliminar tu propio usuario.");
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();

            await _auditService.RegistrarAccionAsync(User.Identity?.Name ?? "Admin", "Eliminar Usuario", $"Usuario eliminado: {user.Username}");

            return NoContent();
        }

        private bool UserExists(int id)
        {
            return _context.Users.Any(e => e.Id == id);
        }
    }
}
