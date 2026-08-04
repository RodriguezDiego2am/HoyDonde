namespace HoyDonde.API.DTOs
{
    // Resumen calculado del reporte de ventas simuladas (docs/api-mvp-plan.md §11). "Importe
    // emitido" nunca se llama recaudación/cobrado/facturación/ganancia: el MVP no procesa pagos
    // reales. EventoConMayorImporte/EventoConMasEntradas son null únicamente cuando no hay
    // ninguna Compra en el conjunto filtrado.
    public class VentasResumenDto
    {
        public int CantidadCompras { get; set; }
        public int EntradasEmitidas { get; set; }
        public decimal ImporteEmitido { get; set; }

        // División por cero -> 0 (nunca una excepción).
        public decimal ImportePromedioPorCompra { get; set; }
        public decimal PrecioPromedioEntrada { get; set; }

        // Distinct(Compra.ClientePersonaId) del conjunto filtrado. Nunca se exponen los
        // ClientePersonaId usados para el cálculo, solo el conteo.
        public int ClientesUnicos { get; set; }

        public VentasEventoDestacadoDto? EventoConMayorImporte { get; set; }
        public VentasEventoDestacadoDto? EventoConMasEntradas { get; set; }
    }
}
