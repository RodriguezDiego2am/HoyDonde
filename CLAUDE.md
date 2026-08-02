# HoyDonde? - Project guidance

## Repository

HoyDonde? is an event-ticketing platform with two applications:

- `HoyDonde.API/`: ASP.NET Core 8 REST API.
- `HoyDonde.API.Tests/`: xUnit unit, controller, and Firestore Emulator integration tests.
- `HoyDonde-frontend/`: Expo 53 / React Native 0.79 client using TypeScript and Expo Router.

The solution builds the backend and its tests. The frontend is an independent npm workspace.

Persistence is Firebase Firestore. Models use Firestore attributes; there is no active `DbContext` or migrations even though obsolete EF Core packages remain referenced. Do not reintroduce SQL/EF persistence without an explicit migration decision.

## Commands

From the repository root:

```bash
dotnet build HoyDonde.sln
dotnet test HoyDonde.sln
dotnet run --project HoyDonde.API
```

`dotnet test` alone runs only the tests that don't need Firestore; Firestore Emulator integration tests are skipped without a running emulator. To run the full suite (unit + controller + integration) reproducibly:

```powershell
npx --yes firebase-tools@13.35.1 emulators:exec --only firestore --project hoydonde-security-refactor-tests "dotnet test HoyDonde.sln"
```

Verified environment: Java 17 (Temurin) + Firebase CLI 13.35.1 via `npx` (no global install, no Java upgrade needed). The Firebase CLI starts the Firestore Emulator, exports `FIRESTORE_EMULATOR_HOST` to the child process, and shuts it down afterward. No `firebase login` or real credentials are required — `hoydonde-security-refactor-tests` is an arbitrary project ID that never resolves against a real Firebase project.

From `HoyDonde-frontend/`:

```bash
npm install
npx expo start
npx eslint .
npx tsc --noEmit
npx jest
```

Do not use `npm test`: the current script is a placeholder that always fails.

Never read, expose, or commit `HoyDonde.API/firebase-service-account.json` or other credentials.

## Security architecture (current, implemented)

The security module is complete (docs/security-refactor-plan.md, Etapas 0–6). There is no legacy `ApplicationUser`/role-inheritance model and no role custom claim anywhere in the code.

```text
Firebase UID
→ IdentidadExterna
→ Usuario
→ UsuarioRol
→ Rol
→ RolAccion
→ Accion
→ ASP.NET Policy
```

- A Firebase ID token proves identity (UID) only — it carries no permissions.
- `AccionAuthorizationHandler` resolves every `[Authorize(Policy = "ACCION_CODIGO")]` exclusively against `IPermissionService`, which walks `Usuario → UsuarioRol → Rol → RolAccion → Accion` in Firestore. Nothing reads a claim to authorize.
- Exactly 20 actions are centralized in `Authorization/Acciones.cs`; a policy exists per action.
- `Rol` and `Accion` are persistent, administrable Firestore entities, not enums or subclasses. A `Usuario` may hold multiple roles; a `Rol` may grant multiple actions.
- Roles/actions/user-role administration lives under `/api/security` (`SecurityAdminController`/`SecurityAdminService`): create/edit/activate roles, list roles/actions/users, assign/remove actions on a role, assign/remove roles on a user, effective-permissions lookup, activate/deactivate a user. Every mutation is guarded transactionally against leaving zero effective Administrators.
- The first Administrator is created via an explicit command, not an HTTP endpoint: `dotnet run --project HoyDonde.API -- bootstrap-admin <email>`. Disabled unless `Bootstrap:AllowAdminBootstrap=true`; refuses if an effective Administrator already exists; password comes from an environment variable or interactive prompt, never from a CLI argument or committed config.

## Bridge to the domain

`Persona` is the only bridge between the security module and HoyDonde domain entities. Domain classes never depend on `Usuario`, `Rol`, or `Accion`, and never store a Firebase UID or `UsuarioId`.

- `Event.OrganizadorPersonaId`, `Ticket.ClientePersonaId`, `Ticket.ValidadoPorPersonaId` — all `PersonaId`, never a UID.
- `control_asignaciones/{ControlPersonaId}_{EventId}` replaces the old scalar `Control.EventId`/`Control.OrganizadorId`; one Control person can be assigned to multiple events.
- `IAuthenticatedPersonaResolver` is the single place that resolves the authenticated UID to a `PersonaId` (`identidades_externas/FIREBASE#{uid}` → `Usuario` → `Usuario.PersonaId`); it throws `IdentityNotProvisionedException` (mapped to a generic 403, UID never leaked in the response) if the token is valid but the identity isn't provisioned in the new model.
- A policy alone never grants access to another organizer's event, another customer's tickets, or an unassigned Control's event: ownership/assignment is re-read from Firestore and compared against the resolved `PersonaId` on every request, in addition to the policy check.
- Ticket stock updates and issuance are transactional (`TicketService.BuyTicketsAsync` uses a Firestore transaction) to avoid overselling and partial writes. A ticket cannot be validated/consumed twice.

## Provisioning flows

- **Cliente**: the frontend (Firebase Client SDK, not yet integrated — see "Frontend status" below) authenticates directly with Firebase and calls `POST /api/auth/sync` with the resulting ID token. The API reads `uid`/`email` only from the token, never the body, and idempotently provisions `Persona+Usuario+UsuarioRol(CLIENTE)` the first time that UID is seen. An existing `Usuario` (any role) is returned as-is, never converted to Cliente, never duplicated.
- **Admin/Organizador/Control**: privileged sign-up via `IIdentityProvider` (`UserService`). Only an Administrator can create an Admin or Organizador; only an Organizador can create a Control, and only for their own event (`Event.OrganizadorPersonaId` is compared against the actor's resolved `PersonaId` before anything is created). If Firestore provisioning fails after the identity was created, the identity is compensated via `DeleteIdentityAsync`; if compensation itself fails, an `IdentidadHuerfana` record is written with both errors.
- Firebase's role in all of this is limited to authenticating and issuing/managing identities (`IIdentityProvider`: create, delete, activate/deactivate, update attributes, generate password-reset link). It has no custom-claim-based permission responsibility.

## Persistence (Firestore collections)

Current and real:

- `personas`, `usuarios`, `usuarios/{id}/roles`, `identidades_externas`
- `roles`, `roles/{codigo}/acciones`, `acciones`
- `events`, `tickets`
- `control_asignaciones`
- `security_audits` (administration mutations)
- `identidades_huerfanas` (orphaned-identity compensation failures)

`users` and `user_audits` are not written or read anywhere in the code; treat them as gone, not as a legacy fallback.

## Functional domain

HoyDonde? connects event organizers with customers and provides event discovery, ticket sales, and access control.

Core concepts and rules:

- Organizers create events and ticket types with price and independent stock.
- Only valid, published events should appear in the public catalog or accept purchases.
- Customers search and filter events, purchase tickets, and access their own tickets.
- Every issued ticket must be unique and belong to one customer, ticket type, and event.
- Access-control personnel validate a ticket for the correct event and must prevent reuse.
- Inventory changes and ticket issuance must avoid overselling and partial writes.
- Merchandise sales and ticket resale are outside the current scope.

The project specification also describes payments, temporary reservations, signed QR codes, notifications, organizer analytics, `ValidacionAcceso` audit records, and event state rules. Treat these as requirements or planned capabilities unless the current code and tests demonstrate that they are implemented. Event publication and ticket validation strictness should not be described as more complete than the code/tests show. Never invent missing behavior.

## Event lifecycle (API-MVP 1, implemented and verified — see docs/api-mvp-plan.md §2)

- `Event` persists `FechaInicio`/`FechaFin`, both UTC (replaces the old single `Fecha` field).
- Persisted `EventStatus`: `Borrador`, `Publicado`, `Cancelado`. `Finalizado` is not persisted — it's a derived effective status (`Event.GetEstadoEfectivo`, exposed as `EventResponse.Estado`) computed as `Publicado && UtcNow > FechaFin`.
- Valid transitions: `Borrador → Publicado` (requires ≥1 ticket type) and `Borrador`/`Publicado` (not yet `Finalizado`) `→ Cancelado`. Any other transition (double publish/cancel, touching a `Cancelado` or effectively-`Finalizado` event) throws `EventInvalidTransitionException` → HTTP 409; `CancelEventAsync` evaluates the effective status, not just the persisted one, before allowing cancellation.
- `UpdateEventAsync` only succeeds while `Estado == Borrador` (`EventNotEditableException` → 409 otherwise) and replaces the entire `TicketGroups` collection — no incremental per-ticket-type edit.
- `GET /api/events/{id}` (`[AllowAnonymous]`) and `SearchEventsAsync` (catalog) only ever show `Publicado && UtcNow <= FechaFin`; anything else is a 404 (never 403, to avoid confirming existence to an anonymous caller). `GET /api/events/organizer/{id}` (`EVENTO_VER_PROPIOS` + ownership check) returns an owned event in any state; `GET /api/events/organizer/me` lists all of the caller's own events, any state.
- All Event HTTP reads return `EventResponse`/`PagedResponse<EventResponse>`, never the `Event` model.
- `SearchEventsAsync` pagination is fully server-side: the optional `FechaInicio` filter, the cursor (`StartAfter`), and `Limit` are all part of the same Firestore query — no in-memory post-filtering. Four explicit composite indexes in `firestore.indexes.json` cover the real filter shapes (`Estado`+`FechaFin`+`FechaInicio`, optionally combined with `Categoria` and/or `Ubicacion`). The Firestore Emulator validates query logic only — it does not prove those indexes are deployed against a real production Firestore project.
- Current verification result: **270 passed, 0 failed, 0 skipped** (full suite, emulator-backed).

## Ticket lifecycle (API-MVP 2, implemented and verified — see docs/api-mvp-plan.md §3)

- `BuyTicketsAsync` reads the `Event` inside the same Firestore transaction that decrements stock and creates tickets, and only accepts the purchase when `Estado == Publicado && UtcNow < FechaInicio` (`EventoNoDisponibleParaCompraException` → 409 otherwise); an unknown `TicketTypeId` (`TicketTypeInvalidoException` → 404) and insufficient stock (`StockInsuficienteException` → 409) are checked against that same transactional read, never against client-supplied data. Firestore's transaction retry-on-conflict (`ABORTED`) is what prevents overselling under concurrency — verified with a test of N concurrent purchases against stock 1 (exactly one succeeds, one `Ticket` created, `CantidadDisponible` never negative).
- Every `Ticket` persists an immutable photograph taken from that same transactional read: `EventoNombre`, `TicketTypeNombre`, `PrecioPagado`, `FechaInicio`, `FechaFin`. All five are copied once at purchase time and never recalculated afterward, regardless of what the `Event` does later; `TicketBuyRequest` has no date/price field at all, so none of this can come from the client.
- `ValidateTicketAsync`/`FirestoreTicketValidationStore.TryConsumeAsync` reads the `Ticket` and the current `Event` inside the same transaction before marking `Usado`, rejecting with `EventoCancelado`/`EventoFinalizado` (409) unless `Estado == Publicado && UtcNow <= FechaFin`; a cancelled/finalized event's tickets are never written to (their persisted `Estado` stays `Emitido`). A ticket already `Usado`/`Anulado` is rejected the same way, including under concurrent double-validation of the same ticket. `CancelEventAsync` never batch-updates tickets (API-MVP 1) — cancelling only changes what a *read* reports, not the ticket's historical document.
- `TicketResponseDto.Estado` is the ticket's historical/persisted status (`Emitido`/`Usado`/`Anulado`), never rewritten by an event cancellation. `Utilizable` and `MotivoNoUtilizable` (`"Usado"`/`"Anulado"`/`"EventoCancelado"`/`"EventoFinalizado"`/`null`) are derived at read time from the **current** `Event`, not from the photograph — `EventoNombre`/`TicketTypeNombre`/`PrecioPagado`/`FechaInicio`/`FechaFin` come from the ticket's own photograph and are never resolved live.
- `GetTicketsByClienteIdAsync` groups the client's tickets by distinct `EventoId` and resolves those events with a single batch read (`FirestoreDb.GetAllSnapshotsAsync`) — never one read per ticket.
- Current verification result: **291 passed, 0 failed, 0 skipped** (full suite, emulator-backed).

## Tests

- Controller/HTTP tests use `TestApplicationFactory` and `FakeAuthHandler`: the fake authentication identity supplies only UID (`ClaimTypes.NameIdentifier`, overridable per-request via the `Test-Uid` header) and email — nothing role-related. There is no `Test-Role` header and no `ClaimTypes.Role`/`"role"` claim anywhere in test infrastructure.
- Test authorization is granted via `TestApplicationFactory.GrantAccion(uid, usuarioId, personaId, ...accionCodigos)`, which wires the mocked `IUsuarioRepository`/`IRolRepository`/`IAccionRepository` so the real `AccionAuthorizationHandler`/`IPermissionService` resolve authorization the same way production does.
- Integration tests run against a real Firestore Emulator (see Commands above) and are marked with `[FirestoreEmulatorFact]`; they skip (not fail) when no emulator is reachable.
- Run the narrowest relevant checks first, then the full affected suite (unit/controller, then the emulator-backed integration suite). Report checks that could not run because Firebase credentials or external services are unavailable.
- Do not claim a requirement is complete unless code and tests support that claim.

## Frontend status (known inconsistency)

The frontend is not yet integrated with the final Firebase Auth + `/api/auth/sync` flow described above — do not assume login/registration already work end-to-end in the app. Confirm the intended flow before changing either side, and do not change API contracts without separately coordinating the corresponding frontend update.

## Working rules

- Inspect the relevant code and tests before editing; keep controllers thin, business rules in services, persistence in repositories.
- Preserve existing API contracts unless a requested change explicitly includes coordinated frontend updates.
- Use DTOs at HTTP boundaries; do not expose persistence models unnecessarily.
- Use UTC for stored timestamps and explicit state transitions for lifecycle changes.
- Do not reintroduce `ApplicationUser`-style inheritance, hard-coded role strings/enums, or claim-based authorization. Roles and actions are Firestore-administrable data, not code constants.
- Domain entities reference `PersonaId`, never a Firebase UID or `UsuarioId`.
- Validate ownership/assignment in addition to policy: a granted action alone must never expose another organizer's event, another customer's tickets, or an event a Control isn't assigned to.
- Use Firestore transactions for invariants that need atomicity or protection from concurrent writes (stock, ticket issuance/consumption, last-Administrator guard, idempotent provisioning).
- Only write an audit record (`security_audits`) for a mutation that actually happened — no-op writes must not generate one.
- Add or update tests for changed behavior; run the narrowest relevant checks first, then the full suite.
- `HoyDonde-frontend/` and `HoyDonde-frontend/lint.json` may carry unrelated local changes at any point — do not revert, modify, or fold them into backend work unless the task explicitly targets the frontend.
- `docs/security-refactor-plan.md` is the historical design/migration record for the security module (Etapas 0–6); this file describes the current state going forward.
