namespace HoyDonde.API.DTOs
{
    // Desglose por categoría del reporte de ventas (docs/api-mvp-plan.md §11). Categoria es
    // "Sin categoría" únicamente para una Compra legacy sin fotografía de Categoria (nunca se le
    // inventa un valor del enum). PorcentajeDelImporteTotal es 0 si el importe total del conjunto
    // filtrado es 0 (nunca una excepción).
    public class VentasCategoriaDto
    {
        public string Categoria { get; set; } = string.Empty;
        public int CantidadCompras { get; set; }
        public int EntradasEmitidas { get; set; }
        public decimal ImporteEmitido { get; set; }
        public double PorcentajeDelImporteTotal { get; set; }
    }
}
