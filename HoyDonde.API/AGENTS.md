# Backend review guidance

## REJECT

- Secrets, credentials, Firebase service-account files, or log output that exposes them.
- SQL/EF/`DbContext` persistence; `ApplicationUser`, role claims, `users`, or `user_audits`.
- Claim-based or hard-coded-role authorization, UID/`UsuarioId` domain references, or policy-only access without ownership/assignment checks.
- Persistence models at HTTP boundaries, controller exception handling, N+1 reads, or non-transactional writes that can violate an invariant.
- Backup export/import changes that weaken managed-operation safety or bypass documented safeguards.

## REQUIRE

- ASP.NET Core 8 with Firestore-only persistence and dynamic action policies: Firebase UID → IdentidadExterna → Usuario → UsuarioRol → Rol → RolAccion → Accion → Policy.
- Resolve actors through `IAuthenticatedPersonaResolver`; use `PersonaId` for domain ownership and re-read ownership/Control assignment from Firestore.
- Keep controllers thin, business rules in services, persistence in repositories, and HTTP input/output bounded DTOs. Preserve the centralized error-middleware contract.
- Use Firestore transactions for concurrent invariants and audit only effective mutations. Keep managed backup export/import operations safe and documented.
- Add focused xUnit/Moq coverage; run `dotnet test HoyDonde.sln`, and use `npx --yes firebase-tools@13.35.1 emulators:exec --only firestore --project hoydonde-security-refactor-tests "dotnet test HoyDonde.sln"` for emulator coverage when relevant.

## PREFER

- Batch Firestore reads instead of per-row queries; use UTC and explicit lifecycle transitions.
- Treat `CLAUDE.md` and `API_Documentation.md` as the detailed authority; follow the root `AGENTS.md` contract.
