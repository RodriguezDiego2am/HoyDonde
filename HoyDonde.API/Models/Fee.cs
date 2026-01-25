using System.ComponentModel.DataAnnotations.Schema;

namespace HoyDonde.API.Models
{
    public class Fee
    {
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty; // ej: "Comisión de servicio", "IVA"
        public FeeType Tipo { get; set; }

        [Column(TypeName = "decimal(18,4)")]
        public decimal Porcentaje { get; set; } = 0; // Porcentaje (ej: 21 para 21%)

        [Column(TypeName = "decimal(18,2)")]
        public decimal MontoFijo { get; set; } = 0; // Monto fijo (ej: $50)

        public bool EsActivo { get; set; } = true;
        public bool AplicaATodos { get; set; } = true; // Si aplica a todos los eventos o solo a algunos

        // Orden de aplicación (para calcular fees compuestos correctamente)
        public int Orden { get; set; } = 0;
    }

    public enum FeeType
    {
        ServiceFee,      // Comisión de servicio
        Tax,             // Impuesto
        ProcessingFee,   // Comisión de procesamiento de pago
        Other            // Otro
    }
}
