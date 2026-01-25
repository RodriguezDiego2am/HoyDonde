using HoyDonde.API.Data;
using HoyDonde.API.Models;
using Microsoft.EntityFrameworkCore;

namespace HoyDonde.API.Services
{
    public class PricingService : IPricingService
    {
        private readonly ApplicationDbContext _context;

        public PricingService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<PricingResult> CalculatePricingAsync(List<OrderItemRequest> items, string? couponCode = null)
        {
            var result = new PricingResult();

            // 1. Calcular subtotal
            decimal subtotal = 0;
            foreach (var item in items)
            {
                var ticketType = await _context.TicketTypes.FindAsync(item.TicketTypeId);
                if (ticketType == null)
                {
                    throw new ArgumentException($"TicketType con ID {item.TicketTypeId} no encontrado");
                }

                var itemSubtotal = ticketType.Precio * item.Cantidad;
                subtotal += itemSubtotal;

                result.Breakdown.Add(new PricingBreakdown
                {
                    Concepto = $"{ticketType.Nombre} x{item.Cantidad}",
                    Monto = itemSubtotal,
                    Tipo = "subtotal"
                });
            }

            result.SubTotal = subtotal;

            // 2. Aplicar descuento si hay cupón
            decimal discountAmount = 0;
            if (!string.IsNullOrEmpty(couponCode))
            {
                // TODO: Implementar lógica de cupones
                // Por ahora solo ponemos la estructura
                discountAmount = 0;
            }
            result.DiscountAmount = discountAmount;

            // 3. Calcular base imponible (subtotal - descuentos)
            decimal baseImponible = subtotal - discountAmount;

            // 4. Obtener fees activos y calcular
            var fees = await _context.Fees
                .Where(f => f.EsActivo && f.AplicaATodos)
                .OrderBy(f => f.Orden)
                .ToListAsync();

            decimal totalFees = 0;
            decimal totalTaxes = 0;

            foreach (var fee in fees)
            {
                decimal feeAmount = 0;

                // Calcular fee (porcentaje + monto fijo)
                if (fee.Porcentaje > 0)
                {
                    feeAmount += baseImponible * (fee.Porcentaje / 100m);
                }
                if (fee.MontoFijo > 0)
                {
                    feeAmount += fee.MontoFijo;
                }

                // Clasificar entre fee o tax
                if (fee.Tipo == FeeType.Tax)
                {
                    totalTaxes += feeAmount;
                }
                else
                {
                    totalFees += feeAmount;
                }

                result.Breakdown.Add(new PricingBreakdown
                {
                    Concepto = fee.Nombre,
                    Monto = feeAmount,
                    Tipo = fee.Tipo == FeeType.Tax ? "tax" : "fee"
                });
            }

            result.Fees = totalFees;
            result.Taxes = totalTaxes;

            // 5. Calcular total final
            result.Total = baseImponible + totalFees + totalTaxes;

            return result;
        }

        public async Task<Fee?> GetFeeByIdAsync(int id)
        {
            return await _context.Fees.FindAsync(id);
        }

        public async Task<IEnumerable<Fee>> GetActiveFeesAsync()
        {
            return await _context.Fees
                .Where(f => f.EsActivo)
                .OrderBy(f => f.Orden)
                .ToListAsync();
        }
    }
}
