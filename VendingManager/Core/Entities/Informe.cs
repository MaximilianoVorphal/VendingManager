using System;
using System.ComponentModel.DataAnnotations;

namespace VendingManager.Core.Entities
{
    public class Informe
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(255)]
        public string Nombre { get; set; } = string.Empty;

        [Required]
        [MaxLength(10)]
        public string Extension { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string TipoContenido { get; set; } = string.Empty; // MIME type

        public DateTime FechaSubida { get; set; } = DateTime.Now;

        [Required]
        public byte[] Contenido { get; set; } = Array.Empty<byte>();
    }
}
