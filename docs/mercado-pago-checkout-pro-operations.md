# Mercado Pago Checkout Pro operations gate

## Status

**New Checkout Pro production traffic is disabled.** This Slice 1 change only adds a local
reservation domain model and has no Mercado Pago HTTP client, webhook endpoint, worker, or UI.
`rolloutAllowed` in the sanitized fixture is deliberately `false`; an operator must not enable
new payment starts until every item in the contract checklist is evidenced and approved.

## Intended provider boundary

| Item | Required value or behaviour | Gate status |
|---|---|---|
| Product and market | Checkout Pro for Argentina, canonical currency `ARS` | Not production-validated |
| Payment decision | Server-side `GET /v1/payments/{id}` is the only payment source of truth | Not production-validated |
| Correlation | Server-created `external_reference` identifies one local purchase | Not production-validated |
| Approval boundary | A verified `approvedAt` must be strictly before local `expiresAt` | Enforced locally; provider field semantics unvalidated |
| Webhook | `x-signature` HMAC-SHA256 validation before durable inbox handling | No endpoint in this slice; canonicalization unvalidated |
| Refund | Full refund path has no amount field and must be observable | No client or worker in this slice |

The sanitized fixture at `HoyDonde.API.Tests/Fixtures/mercado-pago-contracts.json` is a
contract checklist, not sandbox proof and not a copy of a payment response. It contains no token,
secret, payer information, request identifier, payment ID, or signature.

## Version and sandbox evidence required before enablement

No Mercado Pago SDK or API version is selected by Slice 1. Before a provider implementation is
merged or enabled, record all of the following here with date, official documentation URL, API/SDK
version, sandbox account evidence, and a sanitized test result:

1. Preference endpoint or SDK call, required fields, Checkout Pro URL, ARS rules and rounding.
2. Correlation field format, length, persistence and uniqueness in a payment GET response.
3. Payment GET schema, status map, authoritative approval timestamp, timezone and precision.
4. Webhook event types, payment-ID location, HMAC manifest/canonicalization, timestamp tolerance,
   ACK budget and retry semantics.
5. Configure `MercadoPago:CheckoutSuccessUrl`, `MercadoPago:CheckoutFailureUrl`, and
   `MercadoPago:CheckoutPendingUrl` as approved HTTPS or app-return routes before enabling traffic;
   the backend sends them as Checkout Pro `back_urls` and the return remains navigation only.
6. Confirm the configured timestamp tolerance accepts an in-window provider retry but rejects stale
   or future-dated signed notifications before inbox persistence.
7. Total-refund endpoint, eligibility, deterministic idempotency header, status verification and
   retry/ambiguity policy.
8. Approved, pending, rejected, cancelled, refunded, disputed and unknown status semantics.

Missing evidence is a rollout blocker, not permission to infer an API contract from fixtures.

## Preference-creation ambiguity

Provider idempotency for preference creation is **not verified**. Never blind-retry a preference
creation after a timeout or ambiguous transport failure. A future provider slice must persist a
local `CreationUnknown` result and return the same local idempotency outcome until a documented,
validated recovery lookup or an operator decision exists. Letting the local 15-minute reservation
expire is safer than risking duplicate charges.

## Local reservation safety rules

- `reservedAt` is server time and `expiresAt` is exactly fifteen minutes later.
- Only Firestore's transaction may change an `Active` reservation.
- An approval strictly before expiry consumes the reservation and emits the purchased ticket count.
- Approval at or after expiry releases inventory and becomes refund-required with zero tickets.
- Once local state is `Released`, any later processed approval is refund-only even if its provider
  timestamp is earlier than expiry; stock is never reacquired and tickets are never emitted.
- Existing `PagoSimulado=true` purchases remain legacy-readable and are neither migrated nor
  reclassified by this rollout.

## Deployment and rollback for this slice

Deploy additive Firestore fields and indexes before code. Keep new purchase starts disabled because
there is no controller or provider integration in this slice. Roll back by removing the code path
from deployment; do not delete pending purchases, audit records, or future refund evidence. No
backfill is permitted.

## Slice 3 rollout and rollback runbook

1. Deploy the backend schema and Firestore indexes before enabling new checkout starts; confirm the `compras` indexes are `READY` in the Firebase console or `firebase deploy --only firestore:indexes` output.
2. Keep `MercadoPago:Enabled=false` through sandbox contract verification, signed webhook observation, and a controlled ARS Checkout Pro test; enable progressively only after the pending-status screen and server `GET /api/purchases/{id}` agree.
3. Store and rotate Mercado Pago access tokens and webhook secrets exclusively in the approved secret manager. Rotate one credential at a time, verify signed webhook delivery with the replacement, then revoke the predecessor; never place credentials in Expo configuration, source control, logs, PDFs, or support tickets.
4. Monitor durable inbox persistence, webhook signature rejection, payment GET failures, purchases older than 15 minutes, `ReservationReleasedBeforeVerification`, refund pending/failed counts, and confirmed-ticket emission counts. Investigate with sanitized audit references only.
5. Rollback disables **new** starts with `MercadoPago:Enabled=false`; it does not disable authorized status reads, webhook inbox processing, reservation expiration, or refund workers. Do not delete purchases, reopen a released reservation, or revoke confirmed tickets during rollback.
6. No backfill is permitted. Historical `PagoSimulado=true` purchases remain readable and visibly labelled as simulated; real pending/refund records retain their explicit state and are excluded from emitted metrics until confirmed.
