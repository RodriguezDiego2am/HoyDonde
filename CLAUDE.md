# HoyDonde? - Project guidance

## Repository

HoyDonde? is an event-ticketing platform with two applications:

- `HoyDonde.API/`: ASP.NET Core 8 REST API.
- `HoyDonde.API.Tests/`: xUnit unit, controller, and Firestore Emulator integration tests.
- `HoyDonde-frontend/`: Expo 54 / React Native 0.81 client using TypeScript and Expo Router.

The solution builds the backend and its tests. The frontend is an independent npm workspace.

Persistence is Firebase Firestore. Models use Firestore attributes; there is no active `DbContext` or migrations even though obsolete EF Core packages remain referenced. Do not reintroduce SQL/EF persistence without an explicit migration decision.

Real Firebase project: `hoydonde-f5a05` (`Firebase:ProjectId` in `appsettings.json`). Both Firebase Admin (`FirebaseApp`) and `FirestoreDb` load the same credential file from `Firebase:CredentialsPath` (`HoyDonde.API/firebase-service-account.json`, gitignored, never committed) — no `GOOGLE_APPLICATION_CREDENTIALS` environment variable needs to be set manually. Outside the Firestore Emulator (`FIRESTORE_EMULATOR_HOST` unset), `FirestoreDb` resolution fails fast with a clear error if that credential file is missing; under the emulator it never needs one. `dotnet test` (mocked `TestApplicationFactory`) and the emulator-backed integration tests both replace the `FirestoreDb` DI registration before it's ever resolved, so neither depends on this credential file being present.

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
npm run start       # expo start
npm run start:lan   # expo start --lan, for a physical device on the same Wi-Fi
npm run lint        # eslint .
npm run typecheck   # tsc --noEmit
npm test            # jest
```

For a physical device via Expo Go, set `EXPO_PUBLIC_API_URL` in `HoyDonde-frontend/.env` to your machine's LAN IP (e.g. `http://192.168.1.40:5053/api`), not `localhost`.

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

- A Firebase ID token proves identity (UID) only — it carries no permissions. `HoyDonde.API/Authentication/FirebaseAuthenticationHandler.cs` verifies the `Authorization: Bearer` ID token with the Firebase Admin SDK (`FirebaseAuth.DefaultInstance.VerifyIdTokenAsync`, behind `IFirebaseIdTokenVerifier` for testability), not `AddJwtBearer`/`Authority`. It only ever populates `ClaimTypes.NameIdentifier`/`user_id`/`sub` (UID) and, when present, email — never a role claim.
- `AccionAuthorizationHandler` resolves every `[Authorize(Policy = "ACCION_CODIGO")]` exclusively against `IPermissionService`, which walks `Usuario → UsuarioRol → Rol → RolAccion → Accion` in Firestore. Nothing reads a claim to authorize.
- Exactly 24 actions are centralized in `Authorization/Acciones.cs`; a policy exists per action.
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
- `SearchEventsAsync` pagination is fully server-side: the optional `FechaDesde`/`FechaHasta` range filter (`FechaDesde` inclusive `>=`, `FechaHasta` exclusive `<`, both against `Event.FechaInicio`; the caller converts "through day D" into "before day D+1" — the API never does that math), `Categoria`, `Ubicacion` (exact match, server-trimmed), the cursor (`StartAfter`), and `Limit` are all part of the same Firestore query — zero in-memory post-filtering. `FechaDesde > FechaHasta` throws `EventValidationException` → 400 `EVENT_VALIDATION_ERROR` before querying Firestore. The same four explicit composite indexes in `firestore.indexes.json` (`Estado`+`FechaFin`+`FechaInicio`, optionally combined with `Categoria` and/or `Ubicacion`) cover this range query too — an upper bound is a second inequality operator on the already-indexed `FechaInicio` field, not a new field, so no index changes were needed for Frontend 5's Cartelera filters (verified manually against real, already-deployed Firestore indexes — see "Frontend status" below). The Firestore Emulator validates query logic only — it does not prove those indexes are deployed against a real production Firestore project.
- Current verification result: **270 passed, 0 failed, 0 skipped** (full suite, emulator-backed).

## Ticket lifecycle (API-MVP 2, implemented and verified — see docs/api-mvp-plan.md §3)

- `BuyTicketsAsync` reads the `Event` inside the same Firestore transaction that decrements stock and creates tickets, and only accepts the purchase when `Estado == Publicado && UtcNow < FechaInicio` (`EventoNoDisponibleParaCompraException` → 409 otherwise); an unknown `TicketTypeId` (`TicketTypeInvalidoException` → 404) and insufficient stock (`StockInsuficienteException` → 409) are checked against that same transactional read, never against client-supplied data. Firestore's transaction retry-on-conflict (`ABORTED`) is what prevents overselling under concurrency — verified with a test of N concurrent purchases against stock 1 (exactly one succeeds, one `Ticket` created, `CantidadDisponible` never negative).
- Every `Ticket` persists an immutable photograph taken from that same transactional read: `EventoNombre`, `TicketTypeNombre`, `PrecioPagado`, `FechaInicio`, `FechaFin`. All five are copied once at purchase time and never recalculated afterward, regardless of what the `Event` does later; `TicketBuyRequest` has no date/price field at all, so none of this can come from the client.
- `ValidateTicketAsync`/`FirestoreTicketValidationStore.TryConsumeAsync` reads the `Ticket` and the current `Event` inside the same transaction before marking `Usado`, rejecting with `EventoCancelado`/`EventoFinalizado` (409) unless `Estado == Publicado && UtcNow <= FechaFin`; a cancelled/finalized event's tickets are never written to (their persisted `Estado` stays `Emitido`). A ticket already `Usado`/`Anulado` is rejected the same way, including under concurrent double-validation of the same ticket. `CancelEventAsync` never batch-updates tickets (API-MVP 1) — cancelling only changes what a *read* reports, not the ticket's historical document.
- `TicketResponseDto.Estado` is the ticket's historical/persisted status (`Emitido`/`Usado`/`Anulado`), never rewritten by an event cancellation. `Utilizable` and `MotivoNoUtilizable` (`"Usado"`/`"Anulado"`/`"EventoCancelado"`/`"EventoFinalizado"`/`null`) are derived at read time from the **current** `Event`, not from the photograph — `EventoNombre`/`TicketTypeNombre`/`PrecioPagado`/`FechaInicio`/`FechaFin` come from the ticket's own photograph and are never resolved live.
- `GetTicketsByClienteIdAsync` groups the client's tickets by distinct `EventoId` and resolves those events with a single batch read (`FirestoreDb.GetAllSnapshotsAsync`) — never one read per ticket.
- Current verification result: **291 passed, 0 failed, 0 skipped** (full suite, emulator-backed).

## Control assignment (API-MVP 3, implemented and verified — see docs/api-mvp-plan.md §4)

- `POST /api/events/{eventId}/controls/{controlPersonaId}` (`EventsController.AssignControl`) assigns an already-provisioned Control to another event owned by the authenticated organizer. Protected by the **existing** `CONTROL_CREAR` policy — no new action was added to the 20-action catalog.
- Ownership: `Event.OrganizadorPersonaId` (re-read from Firestore) must equal the actor's resolved `PersonaId` (`EventOwnershipException` → 403); an unknown event is `EventNotFoundException` → 404.
- Allowed event states: `Borrador`, `Publicado` not yet started, and `Publicado` in progress. Rejected with `EventoNoDisponibleParaAsignacionControlException` → 409 when the event is `Cancelado`, or `Publicado` with effective status `Finalizado` (`UtcNow > FechaFin`), same effective-status evaluation as `CancelEventAsync`.
- Control eligibility (`UserService.AsignarControlExistenteAsync`): the `controlPersonaId` must resolve (`IUsuarioRepository.GetByPersonaIdAsync`) to a `Usuario` that is `IsActive == true` and holds an active `CONTROL` role. All three rejection reasons (Persona doesn't exist, Usuario inactive, no active CONTROL role) collapse into one public `ControlInvalidoException` → HTTP 404, whose message deliberately never distinguishes which of the three applies, so the response can't be used to probe whether a given `PersonaId` exists.
- Scope check: the Control must already have at least one `ControlAsignacion` (to any event) created by this same organizer (`IControlAsignacionRepository.ExisteAsignacionPorAsignadorAsync`, two equality filters + `Limit(1)`, no composite index needed) before it can be assigned to a new one. Otherwise `ControlAjenoException` → 403 — an organizer can never appropriate a Control administered exclusively by another organizer.
- Idempotency: reuses `IControlAsignacionRepository.AsignarAsync` (already transactional at the repository level). Calling the endpoint twice for the same `(controlPersonaId, eventId)` is a no-op success; `GetAsignacionAsync` always returns `AssignedByPersonaId`/`CreatedAt` from the **first** assignment, verified under 8 concurrent calls for the same pair both at the repository level and at the `UserService` level against the real emulator.
- No new Firebase identity, `Persona`, `Usuario`, or `UsuarioRol` is ever created by this endpoint — it only links an already-provisioned Control to an additional event.
- Response is the bounded `ControlAsignacionResponseDto` (`ControlPersonaId`, `EventId`, `AssignedByPersonaId`, `CreatedAt`) — never the Firebase UID, `UsuarioId`, or any identity-provider data.
- Current verification result: **327 passed, 0 failed, 0 skipped** (full suite, emulator-backed).

## Error contract and backend closure (API-MVP 4, implemented and verified — see docs/api-mvp-plan.md §5)

- Every error response (typed domain exception, invalid `ModelState`, or an unanticipated exception) has the same public shape: `{ code, message, traceId, errors?, detail? }` — `errors` only present for `code: "VALIDATION_ERROR"`; `detail` only on a 500 outside `Production`, never in `Production`.
- `HoyDonde.API/Middleware/ExceptionMiddleware.cs` is the **single** place that maps an exception to an HTTP status/code; no controller (`EventsController`, `TicketsController`, `UserController`, `SecurityAdminController`) contains a `try/catch` of its own — every domain exception propagates to the middleware. `Program.cs` wires `ApiBehaviorOptions.InvalidModelStateResponseFactory` so automatic `ModelState` errors follow the exact same contract.
- `RegisterAdminDto`/`RegisterOrganizadorDto` require a valid `Email` and a `Password` of at least 6 characters (Firebase Authentication's own minimum); `RegisterControlDto` requires `UserName`/`Password`(≥6)/`EventId`.
- `EventResponse.TicketGroups` uses the output-only `TicketTypeResponseDto` (`Id`/`Nombre`/`Precio`/`CantidadDisponible`), not `TicketGroupDto` — `TicketGroupDto` stays input-only (`EventCreateRequest`/`EventUpdateRequest`), with no `Id` ever accepted from the client. All six Event read/write endpoints (create, update, public detail, public search, organizer list, organizer detail) return the real server-generated `TicketTypeId`, so a real client can resolve what to buy (`POST /api/tickets/buy`) from the HTTP response alone.
- `HoyDonde.API.Tests/Integration/FullJourneyEmulatorTests.cs` chains, over a real HTTP pipeline and real controllers/services/repositories against the Firestore Emulator (only the auth scheme is substituted): Organizador creates event → publishes → Cliente buys (resolving `eventId`/`ticketTypeId` solely from the HTTP response) → assigned Control validates → second validation rejected, then verifies stock/photograph/`ClientePersonaId`/`ValidadoPorPersonaId`/`Estado == Usado` in Firestore. The same file also verifies a deactivated `Usuario` gets a real 403 on `POST /api/events`.
- `API_Documentation.md` is the current, rewritten-from-scratch API reference (routes, policies, DTOs, error contract, examples) — prefer it over inferring the contract from code when documenting or discussing the HTTP surface.
- Current verification result: **347 passed, 0 failed, 0 skipped** (full suite, emulator-backed). **API-MVP 1–4 are implemented and verified — the backend functional MVP is closed.**

## Operational control queries (API-MVP 5, implemented and verified — see docs/api-mvp-plan.md §4)

- Three read-only endpoints reuse the existing `CONTROL_CREAR`/`TICKET_VALIDAR` policies (no 21st action): `GET /api/events/organizer/controls` (organizer's own Controls, deduplicated), `GET /api/events/{eventId}/controls` (Controls assigned to one owned event), `GET /api/events/control/me` (events assigned to the authenticated Control, any state).
- Minimal dedicated DTOs (`ControlResumenResponseDto`, `ControlAsignadoResponseDto`, `EventoAsignadoResponseDto`) — never the Firebase UID, `ExternalSubjectId`, `UsuarioId`, DNI, phone, full roles, or ticket types/price/stock. See `API_Documentation.md` §8.1 for shapes.
- Ownership/scope resolved the same way as API-MVP 3 (actor → `PersonaId` via `IAuthenticatedPersonaResolver`, re-read from Firestore); related Usuarios/Events resolved in batch (`WhereIn`/`GetAllSnapshotsAsync`), never per-row.
- **With this closed, the backend functional MVP (API-MVP 1–5) is fully closed.** Verification result at closure: **391 passed, 0 failed, 0 skipped** (full suite, emulator-backed). Frontend 0 (docs/api-mvp-plan.md §7) is closed — see "Frontend status" below; Frontend 1–5 remain pending.

## Reports module (closed — see docs/api-mvp-plan.md §11)

All three reports (Organizer's own events, Admin's global events, Admin's security-audit) are implemented backend + frontend + PDF export. `REPORTE_VER_GLOBAL`/`REPORTE_VER_PROPIO` were assigned manually to `ADMINISTRADOR`/`ORGANIZADOR` on the real Firestore project before this closing pass; this pass did not touch that assignment or run `seed-report-actions` again.

- `Authorization/Acciones.cs` has `REPORTE_VER_GLOBAL` (Admin's two reports) and `REPORTE_VER_PROPIO` (Organizer's report) among its 23 actions. `SecurityCatalogSeeder` assigns them to `ADMINISTRADOR`/`ORGANIZADOR` **only for new installations** (dev/test/emulator); against real Firestore, `SecurityCatalogSeeder.SeedAsync()` never runs again once an effective Administrator exists — the dedicated `seed-report-actions` command (idempotent, creates only the two `Accion` documents, never touches an assignment) remains the real-Firestore path.
- `GET /api/reports/organizer/events` (Policy `REPORTE_VER_PROPIO`): `fechaDesde`/`fechaHasta` required (UTC explicit, `fechaDesde` inclusive, `fechaHasta` exclusive, max 366 days — `REPORT_RANGE_INVALID` → 400), plus optional `estado`/`categoria`/`eventId`/`ticketTypeId` (`ticketTypeId` requires `eventId` — `REPORT_FILTER_INVALID` → 400). Never accepts `organizadorPersonaId`: the organizer always comes from `IAuthenticatedPersonaResolver`. Ownership is always part of the Firestore query itself (`WhereEqualTo(OrganizadorPersonaId, actorPersonaId)` combined with the `FechaInicio` range), never only an in-memory filter.
- `GET /api/reports/admin/events` (Policy `REPORTE_VER_GLOBAL`): same `fechaDesde`/`fechaHasta`/`estado`/`categoria` filters, plus optional `organizadorPersonaId` (arbitrary, accepted from the caller — this endpoint is Admin-only) — deliberately **no** `eventId`/`ticketTypeId` (this report is aggregate activity, not a single-event drill-down). Without `organizadorPersonaId`, the Firestore query is range-only (`FechaInicio`, automatic single-field index); with it, `WhereEqualTo(OrganizadorPersonaId, ...)` reuses the same composite index as the Organizer's report. `ReporteAdminEventoDetalleDto` extends the Organizer's per-event DTO with `OrganizadorPersonaId` (never the Firebase UID/`UsuarioId`/`ExternalSubjectId`).
- `GET /api/reports/admin/security-audits` (Policy `REPORTE_VER_GLOBAL`): `fechaDesde`/`fechaHasta` optional (default: last 30 days when both omitted; max 366 days when informed — same `REPORT_RANGE_INVALID` exception, `ReporteFiltroValidator.ValidateRangoConDefault`), plus optional `operacion`, `actorUsuarioId`, `targetTipo`, `targetId` (exact match, never substring). Only the `Timestamp` range is a Firestore query (`ISecurityAuditRepository.GetByRangoAsync`, descending order); the other four filters are applied in memory by `SecurityAuditReportService`. `ActorEmail` is resolved in batch (`IUsuarioRepository.GetByIdsAsync`, direct document refs, never `WhereIn`) — `null` if the actor `Usuario` no longer exists. **Deviation from the original design:** `targetTipo` accepts a fourth real value, `UsuarioRol` (in addition to the originally planned `Rol`/`Usuario`/`RolAccion`), because `SecurityAdminService.AsignarRolAUsuarioAsync`/`QuitarRolDeUsuarioAsync` already persist that exact `TargetTipo` for the most frequent `/admin/usuarios` operation (assign/remove a role from a user) — restricting the filter to the original three values would have made that operation unfilterable by objetivo type.
- Both events reports reuse `ReporteFiltroValidator`/`ReporteMetricasCalculator` unchanged; tickets are always read via `WhereIn(EventoId, chunk)` in batches of **at most 30**, never one read per event. The monetary figure is always **"importe emitido"**, never "recaudación"/"cobrado"/"ganancia".
- New composite index `events: OrganizadorPersonaId ASC, FechaInicio ASC` (added during the Organizer-report cut) is **deployed and READY** on the real Firebase project (`hoydonde-f5a05`) and covers both the Organizer's and the Admin's `organizadorPersonaId`-filtered query — no further index changes were needed for the Admin report or the security-audit report.
- Frontend: `/organizer/reports` (Organizer, gated by `REPORTE_VER_PROPIO`), `/admin/reports` → `/admin/reports/events` / `/admin/reports/security-audits` (Admin, gated by `REPORTE_VER_GLOBAL`, both entries added to `AdminHubScreen`). Every filter select shows names/emails, never a raw id, in the UI; PersonaId/UsuarioId only travel internally in the request. The Admin screens' organizer/actor pickers reuse the existing `GET /api/security/usuarios` (`USUARIO_VER_PERMISOS_EFECTIVOS`) to resolve names — if a session lacks that action, the picker is simply omitted (rest of the report still works by id). PDF export (`expo-print`/`expo-sharing`, `utils/reportPdf.ts`/`utils/reportPdfBuilders.ts`) builds HTML client-side from the already-loaded report, escapes all dynamic text, always includes the simulated-payments disclaimer, and handles a device without sharing available (file stays in local cache, user is told via `Alert`).
- **Status: 505 passed, 0 failed, 0 skipped (backend, full suite, emulator-backed — 2 unrelated concurrency tests are known-flaky under full-suite contention, verified green in isolation); frontend 408 passed, 0 failed (`npm test`), `npm run typecheck`/`npm run lint` clean, `npx expo-doctor` 18/18, `npx expo export --platform android` succeeds; manually verified in Expo Go against the real API/Firestore** (Organizer's own report, Admin's global report, both reports' filters, coherent metrics, the security audit and its filters, all three PDFs generating/opening/sharing correctly, and Cliente/Control accounts never seeing any reports access) — no issues found. **The reports module (backend, frontend, PDF) is fully closed.**

## Role deletion: logical vs. physical (closed — see docs/api-mvp-plan.md §12)

A custom role can be deactivated (logical, reversible, preserves history) or, once inactive, permanently deleted (physical, irreversible) — the 4 essential seeded roles (`ADMINISTRADOR`/`ORGANIZADOR`/`CLIENTE`/`CONTROL`) can never be physically deleted. `SecurityCatalogSeeder` assigns `ROL_ELIMINAR` to `ADMINISTRADOR` automatically only for new (dev/test/emulator) installations. Against the real Firestore project (`hoydonde-f5a05`), the dedicated `seed-role-deletion-action` command has been run, `ROL_ELIMINAR` has been assigned manually to `ADMINISTRADOR` there, and a physical deletion of a custom role has been manually validated successfully against that real project. Any other pre-existing installation still needs to run the same command and assignment before this feature is usable there.

- `Authorization/Acciones.cs` has `ROL_ELIMINAR` among its 23 actions, assigned to `ADMINISTRADOR` only for new installations.
- **Logical deactivation** reuses the existing `POST /api/security/roles/{codigo}/desactivar` (Policy `ROL_ACTIVAR`, `SetRolActivoAsync`) — no second mechanism was added. `Rol.Activo = false` preserves the role and every `RolAccion`/`UsuarioRol` association; reactivating (`.../activar`) restores effective permissions; idempotent; same last-Administrator guard as the rest of `/api/security`.
- **Physical deletion**: `DELETE /api/security/roles/{codigo}` (Policy `ROL_ELIMINAR`, independent from `ROL_ACTIVAR`). `FirestoreRolRepository.EliminarAsync` only succeeds, inside a single Firestore transaction, when the role exists, is not one of the 4 essential roles, is already inactive, and has zero `UsuarioRol` assignments — active **or** inactive — for that role code. It deletes the `Rol` document and every doc in its `roles/{codigo}/acciones` subcollection, writes one `ROL_ELIMINAR` audit, and never touches the `Accion` catalog, other roles, or any `Usuario`. Earlier `security_audits` entries for that role are never deleted.
- The root `roles` catalog collection and the `usuarios/{usuarioId}/roles` subcollection (`UsuarioRol`) share the same Firestore collection name (pre-existing collision, documented since the security refactor). The assignment check is a collection-group query over `roles` — **unfiltered by `Activo`**, unlike `GetUsuarioIdsConRolActivoAsync`/`UltimoAdministradorGuard` which only care about active ones — that explicitly discards documents without a real `Usuario` parent.
- **Race closed, not weakened:** `FirestoreUsuarioRepository.AsignarRolAsync` now also reads the `Rol` document *inside its own transaction* (previously only `SecurityAdminService.AsignarRolAUsuarioAsync` checked existence, non-transactionally, before calling the repository). That extra read is what lets Firestore's optimistic concurrency correctly serialize an assignment against a concurrent physical deletion of the same role — verified with a real concurrency test against the Firestore Emulator; an orphaned `UsuarioRol` pointing at a deleted `Rol` is not possible.
- Typed exceptions (409): `RolProtegidoException` (`ROL_PROTEGIDO`), `RolDebeEstarInactivoException` (`ROL_DEBE_ESTAR_INACTIVO`), `RolTieneUsuariosAsignadosException` (`ROL_TIENE_USUARIOS_ASIGNADOS`); 404 reuses `RolNoEncontradoException`/`ROLE_NOT_FOUND`.
- Command for an already-existing real Firestore project (same pattern as `seed-report-actions`): `dotnet run --project HoyDonde.API -- seed-role-deletion-action` — creates only the `ROL_ELIMINAR` `Accion`, idempotent, never assigns it to any role, never touches existing roles/users. **Already run against `hoydonde-f5a05`**; `ROL_ELIMINAR` was assigned to `ADMINISTRADOR` there afterward as a separate manual step (the command itself never assigns). Any other pre-existing installation must still run this command itself.
- Frontend (`RolDetailScreen`): buttons relabeled "Dar de baja lógica"/"Reactivar rol" (previously "Desactivar rol"/"Activar rol"), with a short explanation that logical deactivation preserves history/assignments. A "Zona de peligro" section is gated exclusively by `hasAccion(ACCIONES.ROL_ELIMINAR)` (never by the actor's role name): a discreet protected note for the 4 essential roles, a "deactivate it first" note for an active custom role, and an "Eliminar definitivamente" button (explicit confirmation, irreversibility warning, double-submit guarded by component state) only for an inactive custom role. On success it navigates back to `/admin/roles`, which refreshes automatically via its existing focus-based reload.
- **Status: 534 passed, 0 failed, 0 skipped (backend, full suite, emulator-backed, includes the assign/delete concurrency test); frontend 416 passed (`npm test`), `npm run typecheck`/`npm run lint` clean, `npx expo-doctor` 18/18, `npx expo export --platform android` succeeds.** `seed-role-deletion-action` has been run against the real Firestore project (`hoydonde-f5a05`), `ROL_ELIMINAR` was assigned to `ADMINISTRADOR` there, and a physical deletion of a custom role was manually verified successfully against that real project (see docs/api-mvp-plan.md §12 for the full closure note).

## Password recovery and authenticated change (closed — see docs/api-mvp-plan.md §13)

Three distinct flows, all exclusively via Firebase Authentication — the Administrator never sees or sets another user's current or new password. No temporary password, no "force change at next login", no SMTP.

- **Public recovery** ("Olvidé mi contraseña" in Login): `components/auth/ForgotPasswordModal.tsx` (frontend), Firebase Client SDK `sendPasswordResetEmail` exclusively — never touches the API. Always shows the same prudent message ("Si existe una cuenta asociada, recibirás instrucciones para restablecerla"), including on `auth/user-not-found`/`auth/invalid-email` (never reveals whether an email is registered); a real network/provider error shows a distinct, retryable message. **Not a real recovery path for a Control account**: its synthetic email (`{userName}@control.hoydonde.com`) is not a real inbox, so that instructive email never actually arrives even though Firebase accepts the request without error — the modal always shows a fixed notice pointing Control to the Administrator instead (the assisted-link flow below).
- **Authenticated change** (`/account/security`, `screens/account/ChangePasswordScreen.tsx`, universal route outside tabs): Firebase Client SDK exclusively — `EmailAuthProvider.credential` + `reauthenticateWithCredential` + `updatePassword` on `auth.currentUser` — never calls the API. Validates non-empty current password, new password ≥6 chars, new ≠ current, matching confirmation; `auth/wrong-password` gives a clear message, `auth/requires-recent-login` asks to sign in again, no `auth.currentUser` shows a "session expired" state instead of the form. Fields are cleared from state right before the success screen; nothing password-related is ever persisted to AsyncStorage/logs/navigation. `components/PasswordFormInput.tsx` adds a show/hide toggle without modifying the shared `FormInput`. Reached from Perfil (`app/(tabs)/explore.tsx`) for normal accounts and from `ControlHubScreen` (discreet link) for the Control-exclusive experience, without reintroducing Cartelera/Mis entradas/Perfil there. Whether the current Firebase session survives `updatePassword` or requires a fresh login is Firebase's own behavior — the screen surfaces whichever happens, never assumes one. **This is the real path for a Control account that knows its current password** — works exactly like any other account, never depends on receiving an email.
- **Administrator-assisted recovery link**: new action `USUARIO_RESTABLECER_PASSWORD` (`Authorization/Acciones.cs`, 23 → 24 actions), dynamic policy like the rest of the catalog. `SecurityCatalogSeeder` assigns it to `ADMINISTRADOR` only, only for new installations. `POST /api/security/usuarios/{usuarioId}/password-reset-link` (`SecurityAdminController.GenerarPasswordResetLink` → `SecurityAdminService.GenerarPasswordResetLinkAsync`) resolves the `Usuario` by internal id, requires `IdentityProvider == "FIREBASE"` and a non-empty `ExternalSubjectId` (otherwise `UsuarioSinIdentidadRecuperableException` → 409 `USER_IDENTITY_NOT_RECOVERABLE`), and calls `IIdentityProvider.GeneratePasswordResetLinkAsync(externalSubjectId)` — this method already existed on `IIdentityProvider`/`FirebaseIdentityProvider` since the security refactor, unused until this closure. The email is always resolved by Firebase from the `ExternalSubjectId`, never accepted from the client. Response is the minimal `{ resetLink }` — never a UID, `ExternalSubjectId`, or `PersonaId`. Works the same for a Control account (synthetic email) — **this is the only real recovery path for a Control account that forgot its password**, since public recovery above never reaches it.
- **Audit, non-atomic by design**: only on success, `security_audits` gets one `USUARIO_GENERAR_RESET_PASSWORD` entry (`TargetTipo = "Usuario"`, `TargetId = usuarioId`, `Detalle` always empty — never the email or the link). `ISecurityAuditRepository.RegistrarAsync` is a standalone, non-transactional write used only here (unlike `SecurityAuditWriter`, always paired with a real Firestore mutation) — generating the link (Firebase Auth) and writing the audit (Firestore) are two independent writes, **not** a distributed transaction: if the process dies between them, the link was already issued by Firebase but the audit entry may be missing, never the reverse. A failed `GeneratePasswordResetLinkAsync` never reaches the audit write.
- **Command for an already-existing real Firestore project** (same pattern as `seed-report-actions`/`seed-role-deletion-action`): `dotnet run --project HoyDonde.API -- seed-password-reset-action` — creates only the `USUARIO_RESTABLECER_PASSWORD` `Accion`, idempotent, never assigns it to any role, never touches users. **Not run against `hoydonde-f5a05` in this closure** — any existing installation (including that one) still needs to run it and assign the action itself.
- **Frontend admin UI**: `UsuarioDetailScreen` — "Generar enlace de recuperación" gated exclusively by `hasAccion(ACCIONES.USUARIO_RESTABLECER_PASSWORD)` (never by role), explicit confirmation, double-submit guarded by component state, the link shown once and never logged, "Compartir" via React Native's `Share.share` (no clipboard dependency was added — none was already available in the project), "Descartar", and the link is cleared from state on unmount.
- **Status: 549 passed, 0 failed, 0 skipped (backend, full suite, emulator-backed); frontend 440 passed (`npm test`), `npm run typecheck`/`npm run lint` clean, `npx expo-doctor` 18/18, `npx expo export --platform android` succeeds.** Nothing was run against real Firebase/Firestore in this closure — `seed-password-reset-action` and the `USUARIO_RESTABLECER_PASSWORD` role assignment remain pending for any existing installation (including `hoydonde-f5a05`), and this closure was not manually verified in Expo Go against the real API/Firestore.

## Tests

- Controller/HTTP tests use `TestApplicationFactory` and `FakeAuthHandler`: the fake authentication identity supplies only UID (`ClaimTypes.NameIdentifier`, overridable per-request via the `Test-Uid` header) and email — nothing role-related. There is no `Test-Role` header and no `ClaimTypes.Role`/`"role"` claim anywhere in test infrastructure.
- Test authorization is granted via `TestApplicationFactory.GrantAccion(uid, usuarioId, personaId, ...accionCodigos)`, which wires the mocked `IUsuarioRepository`/`IRolRepository`/`IAccionRepository` so the real `AccionAuthorizationHandler`/`IPermissionService` resolve authorization the same way production does.
- Integration tests run against a real Firestore Emulator (see Commands above) and are marked with `[FirestoreEmulatorFact]`; they skip (not fail) when no emulator is reachable.
- Run the narrowest relevant checks first, then the full affected suite (unit/controller, then the emulator-backed integration suite). Report checks that could not run because Firebase credentials or external services are unavailable.
- Do not claim a requirement is complete unless code and tests support that claim.

## Frontend status

Frontend 0 (docs/api-mvp-plan.md §7, "Fundación") is closed. Expo 54 / React Native 0.81, verified manually on a physical device via Expo Go against the real Firebase project (`hoydonde-f5a05`) and the real API (not the emulator/mocked tests): public catalog reads the real API and renders its empty state, bootstrap-created Administrator logs in and shows email/role, Cliente self-registration works, session persistence/logout/re-login work, and `/api/auth/sync` round-trips against real Firestore with the deployed indexes (including the `roles` collection-group index on `Activo` used by `GetRolCodigosActivosAsync`-style queries — see `firestore.indexes.json`).

Frontend 1 (catálogo, compra de demostración, Mis entradas con QR) is closed, verified manually the same way. A minimal cross-cutting operational circuit is also implemented and manually verified: Administrador creates an Organizador (`app/admin`); Organizador runs the full event lifecycle (create/edit/publish/cancel) and creates/assigns Control (`app/organizer`). Control accounts log in with just their username (no `@`); `HoyDonde-frontend/utils/controlLoginEmail.ts` resolves it to the same synthetic email the backend builds at Control creation (`{userName}@control.hoydonde.com`, `UserService.cs`) — never re-derive that rule elsewhere.

Frontend 3 (ticket scanning/validation) is closed. A Control account whose only effective action is `TICKET_VALIDAR` is routed to an exclusive `app/control` experience (no Cartelera/Mis entradas/Perfil/tab bar) — decided by `HoyDonde-frontend/utils/controlExperience.ts` (`isControlExclusivo`, action-based, never role-based); a multi-role account keeps its normal navigation and reaches `/control` from its Panel. Scanning uses `expo-camera` (`CameraView`/`useCameraPermissions`) and reuses the Frontend 1 QR payload shape (`{ticketId, eventId}`, `utils/ticketQr.ts`); manual entry is the always-available fallback, sharing the same service/lock (`services/ticketValidationService.ts`, `hooks/useTicketValidation.ts`). Validation is always online — every outcome (valid/already used/anulado/event cancelled/finalized/not authorized/not found/network/unexpected) comes from `POST /api/tickets/validate`, never decided client-side. Verified manually with a physical camera against the real API/Firestore: a Control-exclusive account scanned a real ticket QR, the first read validated and consumed it, the second read of the same QR was rejected as already used; manual entry validated a different ticket and its repeat was likewise rejected as already used.

Frontend 4 (security administration, docs/api-mvp-plan.md §7) is closed. Routes: `/admin` (hub), `/admin/altas` (Admin/Organizador provisioning), `/admin/roles` → `/admin/roles/[codigo]` (roles + their assigned actions), `/admin/usuarios` → `/admin/usuarios/[usuarioId]` (roles, effective permissions, activate/deactivate). Every section, button, and form field is gated exclusively by `AuthContext.hasAccion(ACCIONES.*)` against the actual codes in `Authorization/Acciones.cs` — never by role name, and the frontend never hardcodes a role→action map; the backend re-checks every policy on every request regardless of what the UI shows. `AuthContext.refreshSessionPermissions()` re-runs `/api/auth/sync` on demand (button/pull-to-refresh in Perfil, plus a throttled foreground re-sync) to pick up a permission change without logging out. Verified manually against the real API/Firestore: an Administrator removed `EVENTO_CREAR` from the `ORGANIZADOR` role, the Organizador refreshed permissions and lost "Crear evento", the Administrator reassigned `EVENTO_CREAR`, and after refreshing again the Organizador saw it reappear — `EVENTO_CREAR` was left assigned to `ORGANIZADOR` at the end of that verification, matching its state before the demo.

Cartelera advanced filters (docs/api-mvp-plan.md §7, Frontend 5) are closed. `app/(tabs)/index.tsx` opens a filter panel (`components/cartelera/EventFilterPanel.tsx`) for a Desde/Hasta date range (`components/forms/SegmentedDateField.tsx`, day-only variant of `SegmentedDateTimeField.tsx` sharing `components/forms/segmentedDigits.ts`/`DigitBox.tsx`), category, and exact location; `utils/datetime.ts` (`parseLocalDate`, `startOfLocalDay`, `nextLocalDayExclusive`, `isValidLocalDateRange`) converts local dates to UTC before calling `GET /api/events` (see `API_Documentation.md` §7 for the query contract) — Hasta is always sent as the start of the **next** local day, never `23:59:59.999`. Applying/clearing filters resets pagination; refresh and "load more" preserve the applied filters; an invalid range is rejected client-side before any request. Verified manually in Expo Go against the real API/Firestore: full range, Desde-only, Hasta-only, category, exact location, combined range+category+location, invalid range rejected, filtered empty state, clear filters, active-filter indicator, refresh preserving filters, and no Firestore index errors — the already-deployed indexes covered every combination, so nothing new was deployed.

Reports module screens (docs/api-mvp-plan.md §11, Frontend 5) are closed: `/organizer/reports` (Organizer, gated by `REPORTE_VER_PROPIO`) and `/admin/reports` → `/admin/reports/events` / `/admin/reports/security-audits` (Admin, gated by `REPORTE_VER_GLOBAL`), each with filters, a resumen/desglose preview, and PDF export via `expo-print`/`expo-sharing`. Verified via `npm test` (mocked API/PDF), `npm run typecheck`, `npm run lint`, `npx expo-doctor`/`npx expo export --platform android`, and manually in Expo Go against the real API/Firestore: both events reports and their filters, coherent metrics, the security audit and its filters, all three PDFs generating/opening/sharing correctly, and Cliente/Control accounts never seeing any reports access — no issues found.

Password recovery/change (docs/api-mvp-plan.md §13) is closed at the automated-verification level only (see "Password recovery and authenticated change" above): `npm test`/`typecheck`/`lint`/`expo-doctor`/`expo export` all pass, but this pass was **not** manually verified in Expo Go against the real API/Firestore/Firebase project, and `seed-password-reset-action` was not run against real Firestore — both remain pending before relying on this in production.

For a physical device via Expo Go on the same Wi-Fi as the backend, run the API bound to all interfaces and start Expo in LAN mode:

```powershell
dotnet run --project .\HoyDonde.API --urls "http://0.0.0.0:5053"
```

```bash
npm run start:lan   # from HoyDonde-frontend/
```

`EXPO_PUBLIC_API_URL` in `HoyDonde-frontend/.env` must point at the machine's LAN IP (e.g. `http://192.168.1.40:5053/api`), not `localhost`.

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
