using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    public interface IPurchaseCheckoutService
    {
        Task<PurchaseStatusResponse> StartAsync(string customerId, PurchaseStartRequest request, string idempotencyKey, CancellationToken cancellationToken);
        Task<PurchaseStatusResponse?> GetOwnedAsync(string customerId, string purchaseId, CancellationToken cancellationToken);
    }

    public sealed class PurchaseCheckoutService : IPurchaseCheckoutService
    {
        private readonly PurchaseService _purchases;
        private readonly IMercadoPagoGateway _gateway;
        private readonly FirestoreDb _firestore;
        public PurchaseCheckoutService(PurchaseService purchases, IMercadoPagoGateway gateway, FirestoreDb firestore)
        { _purchases = purchases; _gateway = gateway; _firestore = firestore; }

        public async Task<PurchaseStatusResponse> StartAsync(string customerId, PurchaseStartRequest request, string idempotencyKey, CancellationToken cancellationToken)
        {
            var purchase = await _purchases.ReserveAsync(new PurchaseReservationRequest
            {
                ClientePersonaId = customerId, EventoId = request.EventoId, TicketTypeId = request.TicketTypeId,
                Cantidad = request.Cantidad, IdempotencyKey = idempotencyKey,
            }, DateTime.UtcNow);

            // Zero-price ticket types keep the existing server-authoritative direct confirmation path.
            // Mercado Pago is never called for them, and a client cannot choose this branch because
            // ImporteTotal was calculated from the event inside ReserveAsync.
            if (purchase.ImporteTotal == 0m)
            {
                await _purchases.ConfirmFreePurchaseAsync(purchase.Id, DateTime.UtcNow);
                return ToResponse(await GetPurchaseAsync(purchase.Id, cancellationToken));
            }

            // The transaction leases the single allowed provider creation. A concurrent caller gets
            // the durable current outcome instead of issuing an unprovable duplicate preference.
            var claimed = await ClaimPreferenceCreationAsync(purchase.Id, cancellationToken);
            if (claimed is null)
            {
                var current = await GetPurchaseAsync(purchase.Id, cancellationToken);
                return ToResponse(current);
            }

            try
            {
                var preference = await _gateway.CreatePreferenceAsync(new MercadoPagoPreferenceRequest(claimed.Id, claimed.TicketTypeNombre, claimed.CantidadEntradas, claimed.ImporteTotal / claimed.CantidadEntradas), cancellationToken);
                await SetCheckoutOutcomeAsync(claimed.Id, Compra.CheckoutStatus.Ready, preference.PreferenceId, preference.CheckoutUrl, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                // The provider may have accepted the request; preserve uncertainty and never retry blindly.
                await SetCheckoutOutcomeAsync(claimed.Id, Compra.CheckoutStatus.CreationUnknown, null, null, cancellationToken);
            }
            catch
            {
                await SetCheckoutOutcomeAsync(claimed.Id, Compra.CheckoutStatus.Failed, null, null, cancellationToken);
            }

            return ToResponse(await GetPurchaseAsync(purchase.Id, cancellationToken));
        }

        public async Task<PurchaseStatusResponse?> GetOwnedAsync(string customerId, string purchaseId, CancellationToken cancellationToken)
        {
            var snapshot = await _firestore.Collection("compras").Document(purchaseId).GetSnapshotAsync(cancellationToken);
            if (!snapshot.Exists) return null;
            var purchase = snapshot.ConvertTo<Compra>();
            return purchase.ClientePersonaId == customerId ? ToResponse(purchase) : null;
        }

        private async Task<Compra?> ClaimPreferenceCreationAsync(string purchaseId, CancellationToken cancellationToken)
        {
            var reference = _firestore.Collection("compras").Document(purchaseId);
            return await _firestore.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(reference);
                if (!snapshot.Exists) throw new InvalidOperationException($"La compra '{purchaseId}' no existe.");
                var purchase = snapshot.ConvertTo<Compra>();
                if (purchase.EstadoCheckout != Compra.CheckoutStatus.NotStarted) return null;
                purchase.EstadoCheckout = Compra.CheckoutStatus.Creating;
                transaction.Set(reference, purchase, SetOptions.MergeAll);
                return purchase;
            }, cancellationToken: cancellationToken);
        }

        private Task SetCheckoutOutcomeAsync(string purchaseId, Compra.CheckoutStatus status, string? preferenceId, string? checkoutUrl, CancellationToken cancellationToken)
        {
            var reference = _firestore.Collection("compras").Document(purchaseId);
            return _firestore.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(reference);
                if (!snapshot.Exists) return;
                var purchase = snapshot.ConvertTo<Compra>();
                if (purchase.EstadoCheckout != Compra.CheckoutStatus.Creating) return;
                transaction.Set(reference, new
                {
                    EstadoCheckout = status,
                    CheckoutPreferenceId = preferenceId,
                    CheckoutUrl = checkoutUrl,
                }, SetOptions.MergeAll);
            }, cancellationToken: cancellationToken);
        }

        private async Task<Compra> GetPurchaseAsync(string purchaseId, CancellationToken cancellationToken)
        {
            var snapshot = await _firestore.Collection("compras").Document(purchaseId).GetSnapshotAsync(cancellationToken);
            if (!snapshot.Exists) throw new InvalidOperationException($"La compra '{purchaseId}' no existe.");
            return snapshot.ConvertTo<Compra>();
        }

        private static PurchaseStatusResponse ToResponse(Compra purchase) => new()
        {
            Id = purchase.Id, EstadoCompra = purchase.EstadoCompra.ToString(), EstadoReserva = purchase.EstadoReserva.ToString(),
            CheckoutState = purchase.EstadoCheckout.ToString(), CheckoutUrl = purchase.CheckoutUrl, RefundStatus = purchase.RefundStatus,
        };
    }
}
