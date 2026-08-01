# AGENTS.md

This file provides guidance to Codex (Codex.ai/code) when working with code in this repository.

## Repository layout

This repo contains two independent projects that are not built or deployed together:

- `HoyDonde.API/` — ASP.NET Core 8 Web API (backend)
- `HoyDonde.API.Tests/` — xUnit test project for the API
- `HoyDonde-frontend/` — Expo / React Native app (mobile + web client)

There is no root-level build script tying the two together; treat them as separate workspaces.

## Backend (HoyDonde.API)

### Commands

Run from the repo root or `HoyDonde.API/`:

```bash
dotnet build HoyDonde.sln          # build API + tests
dotnet test HoyDonde.sln           # run all backend tests
dotnet test --filter "FullyQualifiedName~EventsControllerTests"   # run one test class
dotnet run --project HoyDonde.API  # run the API locally (default: http://localhost:5053)
```

The API requires a Firebase service account key at `HoyDonde.API/firebase-service-account.json` (path configurable via `Firebase:CredentialsPath` in `appsettings.json`). Without it, Firebase initialization is skipped with a warning and auth/Firestore calls will fail. This file is a secret and must never be read into context or committed.

### Architecture

Classic layered architecture, but **backed entirely by Firestore, not SQL/EF Core** — despite `Microsoft.EntityFrameworkCore*` still being referenced in `HoyDonde.API.csproj`, there is no `DbContext` or migrations in the project anymore (they were removed in favor of Firestore). Do not reintroduce EF-based repositories/migrations without confirming with the user first.

- **Controllers** (`Controllers/`) — thin HTTP layer; pull the caller's UID from `ClaimTypes.NameIdentifier` / `user_id` / `sub` claims (Firebase tokens vary which one is populated, so controllers check all three).
- **Services** (`Services/`) — business logic (`AuthService`, `EventService`, `TicketService`, `UserService`).
- **Repositories** (`Repositories/`) — data access. Currently only `FirestoreUserRepository` (implements `IUserRepository`) talks to Firestore's `users` collection directly.
- **Models** (`Models/`) — Firestore-mapped domain entities using `[FirestoreData]` / `[FirestoreProperty]` attributes (not EF attributes). `ApplicationUser` is an abstract base with `Admin`, `Organizador`, `Cliente`, `Control` subclasses, discriminated by a `Role` string field read back via `FirestoreUserRepository.MapDocumentToUser`.
- **DTOs** (`DTOs/`) — request/response shapes for the HTTP boundary.

**Auth model**: Firebase Auth issues the JWTs; ASP.NET's JWT bearer handler validates them against `https://securetoken.google.com/{ProjectId}` and maps the Firebase custom claim `role` to `ClaimsIdentity.RoleClaimType`, so `[Authorize(Roles = Roles.X)]` works directly against Firebase custom claims (see `Models/Roles.cs` for the four role constants: `Admin`, `Organizador`, `Cliente`, `Control`). User registration (`UserController`) creates the Firebase Auth user via FirebaseAdmin SDK, sets the `role` custom claim with `SetCustomUserClaimsAsync`, then writes the profile to Firestore. `POST /api/auth/sync` is what reconciles a Firebase-authenticated caller with their Firestore user document.

Known quirk (see `API_Documentation.md`): if `AuthService.SyncUserAsync` doesn't find an existing Firestore user, it defaults the new record to role `Organizador` — verify this is still intended behavior before relying on it.

**Ticket purchase** (`TicketService.BuyTicketsAsync`) uses `_firestore.RunTransactionAsync` to atomically decrement ticket-type inventory — preserve this pattern for any similar write to shared counters.

Full endpoint reference and Firestore collection layout (`users`, `events`, `tickets`, `user_audits`) is documented in `API_Documentation.md` (Spanish) — read it before adding/changing endpoints.

### Tests

`HoyDonde.API.Tests` uses `WebApplicationFactory<Program>` (`TestApplicationFactory.cs`) with all real services/repositories replaced by Moq mocks, and a `FakeAuthHandler` that authenticates any request carrying an `Authorization` header, using the `Test-Role` header to set the mocked role (defaults to `Organizador`). Use this pattern — set `Test-Role` and `Authorization` headers rather than trying to mint real Firebase tokens — when writing new controller tests.

## Frontend (HoyDonde-frontend)

### Commands

Run from `HoyDonde-frontend/`:

```bash
npm install
npx expo start           # dev server (Metro) — choose Android/iOS/web from the CLI output
npm test                 # jest (jest-expo preset)
npx eslint .              # lint (flat config, eslint-config-expo)
```

There is no configured build/typecheck script beyond `tsc` via the editor; `tsconfig.json` sets strict mode.

### Architecture

- **File-based routing** via `expo-router`; screens live under `app/`, with `app/(tabs)/` as the tab group and `app/_layout.tsx` as the root `Stack` (wraps everything in `AuthProvider`).
- Some screens live outside `app/` in `screens/` (e.g. `LoginScreen.tsx`, `RegisterScreen.tsx`) and are wired in via routes under `app/`.
- **`context/AuthContext.tsx`** exposes `useAuth()` (`isAuthenticated`, `user`, `login`, `logout`, `loading`), backed by `services/APIService.ts`, which persists the token/user via `expo-secure-store`.
- **`services/APIService.ts`** is the single Axios client (`apiClient`) for all backend calls; it auto-attaches the stored bearer token via a request interceptor. `getAPIUrl()` special-cases Android emulator (`10.0.2.2`) vs iOS/web (`localhost`) for local dev, pointed at port `5053`.

### ⚠️ Frontend/backend auth mismatch

The frontend's `authService.login()` posts credentials to `/auth/login`, but the current backend has **no such endpoint** — `AuthController` only exposes `POST /api/auth/sync`, which expects an already-issued Firebase ID token, not an email/password exchange. The frontend also has no Firebase client SDK integration visible in `package.json`. Before building on the login flow, confirm with the user whether the frontend auth is mid-migration to Firebase client auth or the backend removed a route the frontend still depends on — don't assume either side is the source of truth.
