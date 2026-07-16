using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using VendingManager.Core.Entities;
using VendingManager.Core.Interfaces;
using VendingManager.Core.Utils;
using VendingManager.Infrastructure.Data;
using VendingManager.Shared.DTOs;
using VendingManager.Shared.Enums;

namespace VendingManager.Infrastructure.Services;

public class TransferenciaService : ITransferenciaService
{
    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _env;

    public TransferenciaService(ApplicationDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    public async Task<IEnumerable<Transferencia>> GetAllAsync()
    {
        return await _context.Transferencias
            .OrderByDescending(t => t.Fecha)
            .ThenByDescending(t => t.Id)
            .ToListAsync();
    }

    public async Task<Transferencia?> GetByIdAsync(int id)
    {
        return await _context.Transferencias
            .Include(t => t.Compras)
            .FirstOrDefaultAsync(t => t.Id == id);
    }

    public async Task<Transferencia> CreateAsync(Transferencia transferencia)
    {
        transferencia.Estado = TransferenciaEstado.Pendiente;
        _context.Transferencias.Add(transferencia);
        await _context.SaveChangesAsync();
        return transferencia;
    }

    public async Task<Transferencia> UpdateAsync(int id, Transferencia transferencia)
    {
        var existing = await _context.Transferencias.FindAsync(id);
        if (existing == null)
            throw new KeyNotFoundException($"Transferencia {id} no encontrada.");

        if (existing.Estado == TransferenciaEstado.Conciliado)
            throw new InvalidOperationException("No se puede modificar una transferencia ya conciliada.");

        existing.Fecha = transferencia.Fecha;
        existing.Monto = transferencia.Monto;
        existing.Descripcion = transferencia.Descripcion;
        existing.Trabajador = transferencia.Trabajador;
        // Estado se maneja vía transiciones automáticas

        _context.Transferencias.Update(existing);
        await _context.SaveChangesAsync();
        return existing;
    }

    public async Task DeleteAsync(int id)
    {
        var existing = await _context.Transferencias.FindAsync(id);
        if (existing == null)
            throw new KeyNotFoundException($"Transferencia {id} no encontrada.");

        _context.Transferencias.Remove(existing);
        await _context.SaveChangesAsync();
    }

    public async Task<IEnumerable<Transferencia>> GetTransferenciasByRendicionAsync(int rendicionId)
    {
        return await _context.Transferencias
            .Where(t => t.RendicionId == rendicionId)
            .OrderBy(t => t.Fecha)
            .ToListAsync();
    }

    public async Task<IEnumerable<Transferencia>> GetTransferenciasPendientesAsync()
    {
        return await _context.Transferencias
            .Where(t => t.Estado == TransferenciaEstado.Pendiente || t.Estado == TransferenciaEstado.EnUso)
            .OrderByDescending(t => t.Fecha)
            .ToListAsync();
    }

    public async Task<IEnumerable<Transferencia>> GetTransferenciasNoVinculadasAsync()
    {
        return await _context.Transferencias
            .Where(t => t.RendicionId == null)
            .OrderByDescending(t => t.Fecha)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task SaveComprobanteImagenAsync(int transferenciaId, IFormFile file)
    {
        if (file == null || file.Length == 0)
            throw new ArgumentException("No se proporcionó ningún archivo.");

        const long maxSize = 5 * 1024 * 1024; // 5MB
        if (file.Length > maxSize)
            throw new ArgumentException("El archivo excede el límite de 5MB.");

        var allowedExt = new[] { ".jpg", ".jpeg", ".png", ".pdf" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExt.Contains(ext))
            throw new ArgumentException("Formato de archivo no permitido. Use JPG, PNG o PDF.");

        // Buffer into memory first so content is signature-validated BEFORE
        // any lookup or storage occurs.
        byte[] bytes;
        await using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        FileSignatureValidator.Validate(bytes, AllowedFormats.Jpeg | AllowedFormats.Png | AllowedFormats.Pdf);

        var transferencia = await _context.Transferencias.FindAsync(transferenciaId);
        if (transferencia == null)
            throw new KeyNotFoundException($"Transferencia {transferenciaId} no encontrada.");

        // Store in DB — replaces any previously stored bytes automatically
        transferencia.ComprobanteImagen = bytes;
        transferencia.ComprobanteImagenContentType = MapContentType(ext);
        transferencia.ComprobanteImagenFileName = file.FileName;

        _context.Transferencias.Update(transferencia);
        await _context.SaveChangesAsync();
    }

    private static string MapContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

    /// <inheritdoc/>
    public async Task<ComprobanteBackfillResult> BackfillComprobantesAsync()
    {
        var result = new ComprobanteBackfillResult();
        var basePath = GetUploadBasePath();

        var pending = await _context.Transferencias
            .Where(t => t.ComprobanteImagen == null &&
                        t.ComprobanteImagenPath != null &&
                        t.ComprobanteImagenPath.StartsWith("/uploads/"))
            .ToListAsync();

        result.Total = pending.Count;

        foreach (var transferencia in pending)
        {
            var physicalPath = Path.Combine(basePath, transferencia.ComprobanteImagenPath!.TrimStart('/'));
            if (!System.IO.File.Exists(physicalPath))
            {
                result.MissingFiles.Add(transferencia.Id);
                continue;
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(physicalPath);
            transferencia.ComprobanteImagen = bytes;
            transferencia.ComprobanteImagenContentType =
                MapContentType(Path.GetExtension(physicalPath).ToLowerInvariant());
            transferencia.ComprobanteImagenFileName =
                Path.GetFileName(transferencia.ComprobanteImagenPath);
            result.Migrated++;
        }

        if (result.Migrated > 0)
            await _context.SaveChangesAsync();

        return result;
    }

    private string GetUploadBasePath()
    {
        if (!string.IsNullOrEmpty(_env.WebRootPath))
            return _env.WebRootPath;

        return Path.Combine(_env.ContentRootPath, "wwwroot");
    }
}