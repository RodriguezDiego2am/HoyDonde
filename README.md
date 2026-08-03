# HoyDonde?

Plataforma de venta de entradas para eventos: organizadores publican eventos y tipos de entrada, clientes descubren eventos y compran entradas con QR, y personal de control valida el acceso en la puerta. Incluye administración de seguridad (roles/acciones administrables) y reportes con exportación a PDF.

> Para el detalle técnico completo (arquitectura de seguridad, contratos HTTP, estado exacto de cada módulo) ver [`CLAUDE.md`](./CLAUDE.md) y [`API_Documentation.md`](./API_Documentation.md). Este README es el punto de entrada para un evaluador o para alguien que clona el repo por primera vez.

## Perfiles y flujo

- **Cliente**: se registra con Firebase, ve el catálogo público de eventos publicados (con filtros de fecha/categoría/ubicación), compra entradas (pago simulado, sin pasarela real) y las consulta con su código QR en "Mis entradas".
- **Organizador**: crea eventos y tipos de entrada, publica/cancela eventos, crea y asigna personal de Control a sus eventos, y consulta reportes de sus propios eventos (con exportación a PDF).
- **Control**: inicia sesión solo con nombre de usuario (sin `@`), escanea el QR de una entrada con la cámara o la valida por ingreso manual; cada validación es contra el servidor (nunca se decide en el cliente) y una entrada usada no puede reutilizarse.
- **Administrador**: da de alta Administradores/Organizadores, administra roles y las acciones que otorga cada rol, activa/desactiva usuarios, da de baja lógica (reversible) o física (irreversible, solo roles personalizados e inactivos) un rol, y consulta los reportes globales y la auditoría de seguridad.

Todos los botones y pantallas del frontend se habilitan por **acción efectiva del usuario** (`hasAccion(ACCIONES.X)`), nunca por el nombre de su rol — el backend vuelve a validar cada policy en cada request sin importar lo que muestre la UI.

## Stack

- **Backend**: ASP.NET Core 8 (`HoyDonde.API/`), persistencia en Firebase Firestore (no SQL/EF, aunque queden paquetes NuGet obsoletos referenciados históricamente).
- **Tests backend**: xUnit (`HoyDonde.API.Tests/`) — unitarios/controller con mocks, e integración contra el Firestore Emulator.
- **Frontend**: Expo SDK 54 / React Native 0.81, TypeScript, Expo Router (`HoyDonde-frontend/`), workspace npm independiente.
- **Autenticación**: Firebase Authentication (Firebase Client SDK en el frontend, Firebase Admin SDK en el backend). No hay JWT propio ni pasarela de pago real.

## Arquitectura de seguridad (resumen)

```
UID de Firebase → IdentidadExterna → Usuario → UsuarioRol → Rol → RolAccion → Accion → Policy de ASP.NET
```

El token de Firebase solo prueba identidad (UID/email); todos los permisos se resuelven leyendo Firestore en cada request (`Usuario → UsuarioRol → Rol → RolAccion → Accion`), nunca desde un claim del token. `Persona` es el único puente hacia las entidades de dominio (`Event`, `Ticket`, `ControlAsignacion`) — nunca se guarda un UID de Firebase en el dominio. Detalle completo en `CLAUDE.md`.

## Requisitos

- .NET SDK 8
- Node.js 18+ y npm
- Java 17 (Temurin recomendado) — solo para correr el Firestore Emulator vía `npx firebase-tools`
- Una cuenta/proyecto de Firebase propio si vas a probar contra Firebase real (no es necesario para `dotnet test`/emulador)
- Expo Go (app móvil) si vas a probar en un dispositivo físico

## Configuración local (sin valores reales)

Ningún valor real se commitea a este repositorio.

**Backend** — `HoyDonde.API/appsettings.json` ya define `Firebase:ProjectId` y `Firebase:CredentialsPath` (este último apunta a `HoyDonde.API/firebase-service-account.json`, que **no existe en el repo** y debés generar vos desde la consola de Firebase de tu propio proyecto: IAM y administración de cuentas de servicio → cuenta de servicio del SDK de administrador → generar clave nueva). Sin ese archivo, fuera del emulador la resolución de `FirestoreDb` falla rápido con un error claro; `dotnet test` y las pruebas de integración contra el emulador nunca lo necesitan.

**Frontend** — copiá `HoyDonde-frontend/.env.example` a `HoyDonde-frontend/.env` y completá los valores públicos del SDK cliente de Firebase de tu propio proyecto (panel de Firebase → Configuración del proyecto). Esos valores son públicos por diseño; la cuenta de servicio del backend nunca va ahí.

## Ejecutar el backend

```bash
dotnet build HoyDonde.sln
dotnet run --project HoyDonde.API
```

Para probar desde un dispositivo físico con Expo Go en la misma red Wi-Fi, exponé la API en todas las interfaces:

```bash
dotnet run --project HoyDonde.API --urls "http://0.0.0.0:5053"
```

## Ejecutar el frontend (Expo Go)

```bash
cd HoyDonde-frontend
npm install
npm run start:lan   # expo start --lan
```

En `HoyDonde-frontend/.env`, `EXPO_PUBLIC_API_URL` debe apuntar a la IP LAN de tu máquina (por ejemplo `http://192.168.1.40:5053/api`), no a `localhost`, para que un dispositivo físico pueda alcanzar la API.

## Tests

```bash
dotnet test HoyDonde.sln          # solo unitarios/controller; salta los de integración sin emulador corriendo
```

Suite completa (unitarios + controller + integración contra el Firestore Emulator real, sin necesitar login ni credenciales reales de Firebase):

```bash
npx --yes firebase-tools@13.35.1 emulators:exec --only firestore --project hoydonde-security-refactor-tests "dotnet test HoyDonde.sln"
```

Frontend:

```bash
cd HoyDonde-frontend
npm run typecheck
npm run lint
npm test
```

## Bootstrap y comandos de seed (proyectos Firebase reales)

```bash
# Primer Administrador (deshabilitado salvo Bootstrap:AllowAdminBootstrap=true; falla si ya existe un Administrador efectivo)
dotnet run --project HoyDonde.API -- bootstrap-admin <email>

# Acciones del módulo de reportes, para un proyecto Firestore real ya existente (idempotente)
dotnet run --project HoyDonde.API -- seed-report-actions

# Acción de baja física de roles, para un proyecto Firestore real ya existente (idempotente; no la asigna a ningún rol)
dotnet run --project HoyDonde.API -- seed-role-deletion-action
```

Ninguno de los tres comandos necesita correrse contra el Firestore Emulator ni contra `dotnet test`.

## Pagos, reportes y bajas de rol

- **Pagos simulados**: la compra de entradas no usa ninguna pasarela de pago real; es una demostración del flujo de stock/emisión de entradas.
- **Reportes**: reporte propio del Organizador y reportes globales (eventos + auditoría de seguridad) del Administrador, con filtros de fecha/estado/categoría (y organizador/actor en los reportes de Admin) y exportación a PDF (`expo-print`/`expo-sharing`) desde el frontend.
- **Baja de roles**: un rol personalizado puede darse de **baja lógica** (reversible, conserva historial y asignaciones) o, ya inactivo y sin usuarios asignados, **baja física** (irreversible). Los 4 roles esenciales (`ADMINISTRADOR`/`ORGANIZADOR`/`CLIENTE`/`CONTROL`) nunca pueden eliminarse físicamente.

## Estructura del repositorio

```
HoyDonde.API/            API REST (ASP.NET Core 8)
HoyDonde.API.Tests/       Tests xUnit (unitarios, controller, integración con emulador)
HoyDonde-frontend/        App Expo / React Native (workspace npm independiente)
docs/                     Historial de diseño (refactor de seguridad, plan del MVP)
API_Documentation.md      Referencia de la API HTTP (rutas, policies, DTOs, contrato de error)
CLAUDE.md                 Guía técnica detallada del estado actual del proyecto
firebase.json / firestore.indexes.json   Configuración del Firestore Emulator e índices
```

## Seguridad — nunca commitear

- `HoyDonde.API/firebase-service-account.json` (cuenta de servicio real) — ya está en `.gitignore`.
- `HoyDonde-frontend/.env` (valores reales) — ya está en `.gitignore`; commiteá solo `.env.example` con valores de ejemplo.
- Cualquier otro archivo con tokens, contraseñas o connection strings reales.
