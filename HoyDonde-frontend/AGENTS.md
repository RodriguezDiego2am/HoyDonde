# Frontend review guidance

## REJECT

- Secrets, `.env` contents, or hard-coded API credentials.
- Role-name checks, a frontend authorization boundary, or client-side decisions for security-sensitive outcomes.
- Direct API calls from screens when an API service applies, unbounded/persistence-shaped contracts, or raw internal IDs as display data.

## REQUIRE

- Expo SDK 54 / React Native 0.81, strict TypeScript, and Expo Router conventions.
- Gate actions exclusively with `AuthContext.hasAccion(ACCIONES.*)`; the API remains the enforcement boundary and re-checks policy/ownership.
- Keep API services separate from screens and use bounded DTO contracts.
- Cover changed behavior with Jest and Testing Library; run `npm run lint`, `npm run typecheck`, and `npm test` from this workspace.
- Use only `EXPO_PUBLIC_API_URL` from the local `.env`; never read, expose, or commit it.

## PREFER

- Accessible, visible labels and `accessibilityLabel`s; do not convey data only through color or chart size.
- Display resolved names/emails rather than raw IDs, keeping IDs internal to requests when needed.
- Follow the root `AGENTS.md`; treat `CLAUDE.md` and `API_Documentation.md` as detailed contract authority.
