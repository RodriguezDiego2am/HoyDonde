# HoyDonde? - Project guidance

## Repository

HoyDonde? is an event-ticketing platform with two applications:

- `HoyDonde.API/`: ASP.NET Core 8 REST API.
- `HoyDonde.API.Tests/`: xUnit integration/controller tests.
- `HoyDonde-frontend/`: Expo 53 / React Native 0.79 client using TypeScript and Expo Router.

The solution builds the backend and its tests. The frontend is an independent npm workspace.

## Commands

From the repository root:

```bash
dotnet build HoyDonde.sln
dotnet test HoyDonde.sln
dotnet run --project HoyDonde.API
```

From `HoyDonde-frontend/`:

```bash
npm install
npx expo start
npx eslint .
npx tsc --noEmit
npx jest
```

Do not use `npm test`: the current script is a placeholder that always fails.

## Current implementation

- The API uses controllers, services, repositories, models, and DTOs.
- Persistence is Firebase Firestore. Models use Firestore attributes; there is no active `DbContext` or migrations even though obsolete EF Core packages remain referenced.
- Firebase Authentication issues ID tokens. ASP.NET validates them and uses the custom `role` claim for role-based authorization.
- `ApplicationUser` currently has the subclasses `Admin`, `Cliente`, `Organizador`, and `Control`, with one string role per user.
- Firestore collections currently include `users`, `events`, `tickets`, and `user_audits`.
- Ticket stock updates must remain atomic. `TicketService.BuyTicketsAsync` uses a Firestore transaction for this purpose.
- The frontend uses Expo Router. `context/AuthContext.tsx` owns session state and `services/APIService.ts` is the shared Axios client and secure-token storage boundary.
- Read `API_Documentation.md` and the relevant implementation before changing an endpoint. Prefer the code and tests when documentation disagrees with them.

Never read, expose, or commit `HoyDonde.API/firebase-service-account.json` or other credentials. Do not reintroduce SQL/EF persistence without an explicit migration decision.

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

The project specification also describes payments, temporary reservations, signed QR codes, notifications, organizer analytics, audit records, and event state rules. Treat these as requirements or planned capabilities unless the current code and tests demonstrate that they are implemented. Never invent missing behavior.

## Security module refactor - target architecture

The current inheritance-based user model and hard-coded role strings are legacy design. Do not expand that design when working on the planned security refactor.

The agreed target is a reusable security module:

```text
Accion <-> Rol <-> Usuario <-> Persona <-> HoyDonde domain
```

- `Usuario` represents authentication credentials/account state.
- `Persona` represents the individual in the business domain and is the only bridge between security and HoyDonde entities.
- `Rol` and `Accion` are persistent, administrable entities, not enums, constants, or subclasses.
- Users may have multiple roles; roles may have multiple actions.
- Initial roles are `ADMINISTRADOR`, `CLIENTE`, `ORGANIZADOR`, and `CONTROL`.
- Administrator capabilities must include creating/editing roles, assigning/removing actions, and assigning/removing user roles.
- Domain relationships use `Persona`: a person organizes or approves events, performs purchases, and executes access validations. Domain classes must not depend directly on `Usuario`, `Rol`, or `Accion`.
- `Organizador` and `Control` remain distinct roles; one person may hold both.
- Do not introduce an `Organizacion` entity unless business requirements establish a commercial entity separate from a person.

This is a future refactor, not the current persistence model. Before implementing it, inspect all authentication, registration, authorization, Firestore mapping, frontend session, and test impacts; propose a staged migration that preserves working behavior and existing data.

## Known inconsistencies

- The frontend posts credentials to `/api/auth/login`, while the backend currently exposes `/api/auth/sync` for callers that already have a Firebase ID token. The frontend has no Firebase client SDK dependency. Confirm the intended authentication flow before changing either side.
- `AuthService.SyncUserAsync` currently creates missing users as `Organizador`. Do not rely on or propagate this default without confirming the desired behavior.
- Event publication and ticket validation are less strict than the documented business rules. Do not describe planned validations as implemented.

## Working rules

- Inspect the relevant code and tests before editing; keep controllers thin and business rules in services.
- Preserve existing API contracts unless a requested change explicitly includes coordinated frontend updates.
- Use DTOs at HTTP boundaries; do not expose persistence models unnecessarily.
- Use UTC for stored timestamps and explicit state transitions for lifecycle changes.
- Validate ownership as well as role: a role alone must not grant access to another organizer's event or another customer's tickets.
- Add or update tests for changed behavior. Controller tests use `TestApplicationFactory`, mocked services, an `Authorization` header, and `Test-Role`; do not mint real Firebase tokens.
- Run the narrowest relevant checks first, then the full affected suite. Report checks that could not run because Firebase credentials or external services are unavailable.
- Do not claim a requirement is complete unless code and tests support that claim.
