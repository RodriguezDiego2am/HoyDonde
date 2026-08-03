# AGENTS.md

This file provides quick orientation to Codex (or any other coding agent) working in this repository. **`CLAUDE.md` is the authoritative, detailed source** — read it before making any change; this file only summarizes the parts most likely to trip up an agent that hasn't read it yet.

## Repository

Two independent projects:

- `HoyDonde.API/` — ASP.NET Core 8 REST API, persisted entirely on Firestore (no `DbContext`, no migrations, despite some obsolete EF Core package references still sitting in the `.csproj`).
- `HoyDonde.API.Tests/` — xUnit unit/controller/integration tests.
- `HoyDonde-frontend/` — Expo SDK 54 / React Native 0.81 client (TypeScript, Expo Router), an independent npm workspace.

## Security model — read this before touching auth/authorization

There is **no** `ApplicationUser`/role-inheritance model, **no** role custom claim, and **no** `users`/`user_audits` collection anywhere in the current code. Do not reintroduce any of these.

```
Firebase UID → IdentidadExterna → Usuario → UsuarioRol → Rol → RolAccion → Accion → ASP.NET Policy
```

- A Firebase ID token (verified via the Firebase Admin SDK in `FirebaseAuthenticationHandler`, not `AddJwtBearer`) proves identity (UID/email) only — it carries no permissions.
- Every `[Authorize(Policy = "ACCION_CODIGO")]` is resolved by `AccionAuthorizationHandler` against `IPermissionService`, which walks `Usuario → UsuarioRol → Rol → RolAccion → Accion` in Firestore. Nothing reads a claim to authorize.
- `Rol`/`Accion` are administrable Firestore entities (`/api/security`), not enums or code constants.
- `Persona` (never a Firebase UID or `UsuarioId`) is the only bridge to domain entities (`Event.OrganizadorPersonaId`, `Ticket.ClientePersonaId`, etc.) — resolved per-request via `IAuthenticatedPersonaResolver`.
- There is no `/api/auth/login` endpoint. Cliente auth is Firebase Client SDK → `POST /api/auth/sync` with the ID token. Admin/Organizador/Control are provisioned server-side via `IIdentityProvider`.

## Essential commands

```bash
dotnet build HoyDonde.sln
dotnet test HoyDonde.sln     # skips Firestore-emulator integration tests without a running emulator
npx --yes firebase-tools@13.35.1 emulators:exec --only firestore --project hoydonde-security-refactor-tests "dotnet test HoyDonde.sln"   # full suite
dotnet run --project HoyDonde.API -- bootstrap-admin <email>   # first Administrator only
```

```bash
cd HoyDonde-frontend
npm install
npm run start:lan   # expo start --lan, for a physical device via Expo Go
npm run lint && npm run typecheck && npm test
```

## Credentials — never touch

`HoyDonde.API/firebase-service-account.json` and `HoyDonde-frontend/.env` are gitignored secrets. **Never read, print, or commit either file**, and never grep for their contents. Test infrastructure (`FakeAuthHandler`, `TestApplicationFactory`) never uses real Firebase credentials or a role/`Test-Role` header — only a UID.

## When in doubt

Read `CLAUDE.md` for the full picture: functional domain, event/ticket lifecycle, reports module, role deactivation vs. physical deletion, DTO/exception conventions, and working rules. Don't duplicate it here — update `CLAUDE.md` itself if behavior changes.
