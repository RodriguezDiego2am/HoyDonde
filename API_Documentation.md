# Documentación de la API HoyDonde?

Esta documentación describe **exclusivamente** el estado real del código en `HoyDonde.API` al cierre de API-MVP 4 (`docs/api-mvp-plan.md` §5). No describe capacidades planificadas ni comportamiento legacy eliminado (Custom Claims de Firebase, colección `users`, roles hardcodeados): eso fue reemplazado por el modelo de seguridad descrito en `CLAUDE.md` y `docs/security-refactor-plan.md`.

## 1. Arquitectura y URL base

- **Backend**: ASP.NET Core 8 Web API (`HoyDonde.API/`).
- **Persistencia**: Google Cloud Firestore (NoSQL de documentos). No hay `DbContext` activo ni migraciones EF Core, aunque los paquetes obsoletos siguen referenciados en el `.csproj`.
- **Identidad**: Firebase Authentication. Un token de Firebase prueba **quién sos** (UID), nunca **qué podés hacer**: los permisos viven exclusivamente en Firestore (§4).
- **Capas**: `Controllers/` (HTTP, delgados) → `Services/` (reglas de negocio) → `Repositories/` (acceso a Firestore) → `Models/`/`DTOs/` (entidades de dominio y contratos HTTP).

URL base en desarrollo local (`dotnet run --project HoyDonde.API`, perfil `http`/`https` de `launchSettings.json`):

```
http://localhost:5053
https://localhost:7131
```

Swagger/OpenAPI está disponible solo en `Development`, en `/swagger`.

No hay una URL base de producción documentada en el repositorio: el proyecto de Firebase real (`Firebase:ProjectId` en `appsettings.json`, hoy `hoydonde-app`) y el hosting del backend son responsabilidad de despliegue, fuera del alcance de este documento.

---

## 2. Autenticación: Firebase Client SDK + Bearer token

El backend **nunca** recibe contraseñas de Cliente ni emite sus propios tokens de sesión para ese rol. El flujo es:

1. El cliente (app Expo SDK 54, Frontend 0 cerrado — ver `CLAUDE.md` "Frontend status") se autentica directamente contra **Firebase Authentication** usando el Firebase Client SDK (login/registro de email+contraseña, o el proveedor que corresponda).
2. Firebase devuelve un **ID token** (JWT) al cliente.
3. El cliente llama a cualquier endpoint autenticado de esta API con:

   ```
   Authorization: Bearer <id-token-de-firebase>
   ```

4. `Program.cs` registra un esquema de autenticación propio (`FirebaseAuthenticationHandler`, `HoyDonde.API/Authentication/`) que verifica ese ID token con el **Firebase Admin SDK** (`FirebaseAuth.DefaultInstance.VerifyIdTokenAsync`, detrás de `IFirebaseIdTokenVerifier` para poder testearlo sin el SDK real) — no con `Microsoft.AspNetCore.Authentication.JwtBearer`/`Authority` contra `securetoken.google.com`, que no resuelve las claves públicas de firma en este entorno. La validación de firma/expiración es responsabilidad exclusiva del Admin SDK; el resto del pipeline nunca vuelve a tocar el token crudo.
5. De ese token, la API solo lee dos cosas: el **UID** (`ClaimTypes.NameIdentifier`, o los claims alternativos `user_id`/`sub` según cómo Firebase los emita) y, cuando aplica, el **email**. Ningún claim de rol se usa nunca para autorizar (§4).

### Altas privilegiadas no se autoregistran

Un token de Firebase válido por sí solo **no** provisiona nada en Firestore. Hay dos caminos distintos:

- **Cliente**: se autoprovisiona la primera vez que llama a `POST /api/auth/sync` (§3).
- **Admin / Organizador / Control**: los crea explícitamente otro actor ya autorizado, vía `POST /api/users/*` (§6) — nunca por autoregistro.

---

## 3. `POST /api/auth/sync` — provisioning idempotente de Cliente

```
POST /api/auth/sync
Authorization: Bearer <id-token-de-firebase>   (requerido; [Authorize] sin excepción anónima)
```

`uid` y `email` se leen **exclusivamente del token**, nunca del body. El body es opcional y solo trae datos de `Persona` — nunca una contraseña:

```json
{
  "fullName": "Ada Lovelace",
  "dni": "30123456",
  "phoneNumber": "+54 9 11 5555-5555"
}
```

Comportamiento:

- La **primera vez** que se ve ese UID, se provisiona `Persona` + `Usuario` + `UsuarioRol(CLIENTE)` + `IdentidadExterna` en una única transacción de Firestore (`IUsuarioRepository.ProvisionarAsync`).
- Si ese UID **ya tiene** un `Usuario` (con cualquier rol — Cliente, Organizador, Admin o Control), se devuelve el existente **sin modificarlo ni convertirlo** a Cliente. Nunca se duplica.

Respuesta `200 OK` (`SyncUserResponseDto`):

```json
{
  "usuarioId": "usuario-...",
  "personaId": "persona-...",
  "roles": ["CLIENTE"],
  "acciones": ["TICKET_COMPRAR", "TICKET_VER_PROPIO"]
}
```

- **`acciones`**: códigos de `Accion` efectivos del `Usuario` sincronizado, resueltos en el momento de la llamada vía `IPermissionService` (`Usuario` → `UsuarioRol` → `Rol` → `RolAccion` → `Accion`) — la misma autoridad que resuelve cada policy `[Authorize]`, nunca una tabla hardcodeada de roles ni un claim del token. Únicos y en orden determinístico (ordinal ascendente). Si el `Usuario` está inactivo o no tiene roles/acciones activas, `acciones` es `[]`. El frontend usa este campo para decidir qué opciones mostrar, pero **no reemplaza** la autorización real: cada endpoint vuelve a evaluar su policy contra Firestore en cada request.

---

## 4. Autorización: Usuario → Rol → Acción → Policy (sin custom claims)

```
Firebase UID
  → IdentidadExterna (identidades_externas/FIREBASE#{uid})
  → Usuario (IsActive)
  → UsuarioRol (activo)
  → Rol (activo)
  → RolAccion
  → Accion (activo)
  → ASP.NET Policy
```

- Cada `[Authorize(Policy = "ACCION_CODIGO")]` se resuelve exclusivamente vía `AccionAuthorizationHandler` → `IPermissionService.TieneAccionAsync`, que recorre esa cadena completa en Firestore. **Nada lee un claim para autorizar.**
- Un `Usuario` puede tener varios roles; un `Rol` puede otorgar varias acciones.
- Si el `Usuario` está inactivo (`IsActive == false`), o la asignación de rol está inactiva, o el `Rol` está inactivo, o la `Accion` está inactiva, `TieneAccionAsync` devuelve `false` y el handler simplemente no llama a `context.Succeed(...)` — ASP.NET deniega por default, sin excepción ni 500. Esto es lo que hace que **un Usuario desactivado reciba 403 en cualquier endpoint protegido**, sin lógica adicional en el controller.
- El catálogo tiene exactamente **23 acciones** (`Authorization/Acciones.cs`), sembradas por `SecurityCatalogSeeder` en 4 roles iniciales: `ADMINISTRADOR`, `ORGANIZADOR`, `CLIENTE`, `CONTROL`. Roles y acciones son entidades Firestore administrables (`/api/security`, §9), no enums ni constantes de código. `REPORTE_VER_GLOBAL` (`ADMINISTRADOR`) y `REPORTE_VER_PROPIO` (`ORGANIZADOR`) son del módulo de reportes (§15); `ROL_ELIMINAR` (`ADMINISTRADOR`) es de la baja física de roles (§10.1).
- Una policy concedida **nunca es suficiente por sí sola** para tocar el recurso de otro actor: cada operación vuelve a leer el dueño/asignación real desde Firestore y lo compara contra el `PersonaId` del actor autenticado (`IAuthenticatedPersonaResolver`, §5). Ejemplos: `EVENTO_PUBLICAR_PROPIO` no alcanza para publicar el evento de otro organizador; `TICKET_VALIDAR` no alcanza para validar tickets de un evento al que ese Control no está asignado.

---

## 5. Bootstrap del primer Administrador

No es un endpoint HTTP — es un comando de línea de comandos, deliberadamente fuera del pipeline web:

```bash
dotnet run --project HoyDonde.API -- bootstrap-admin admin@hoydonde.com
```

- Deshabilitado salvo que `Bootstrap:AllowAdminBootstrap=true` (config o variable de entorno equivalente). En `appsettings.json` viene en `false` por defecto.
- Se rechaza (sin crear ni modificar nada) si **ya existe** un Administrador efectivo (un `Usuario` activo con una asignación `UsuarioRol(ADMINISTRADOR)` activa sobre un `Rol` `ADMINISTRADOR` también activo).
- La contraseña **nunca** se pasa como argumento de CLI ni se guarda en config: sale de la variable de entorno `HOYDONDE_BOOTSTRAP_ADMIN_PASSWORD`, o se pide interactivamente (entrada oculta) si no hay TTY redirigido.
- Siembra el catálogo de roles/acciones (`SecurityCatalogSeeder.SeedAsync`, idempotente) antes de verificar/crear, y reutiliza exactamente el mismo `UserService.RegisterAdminAsync` que usa el alta HTTP.

---

## 6. Provisioning de Admin/Organizador/Control (`/api/users`)

Solo un actor ya autorizado puede dar de alta a estos roles — nunca autoregistro.

### `POST /api/users/admin` — Policy: `USUARIO_CREAR_ADMIN`

Solo un Administrador autenticado puede crear otro Administrador.

```json
{ "email": "nuevo-admin@hoydonde.com", "password": "unaContraseñaSegura" }
```

- `email`: requerido, formato de email válido.
- `password`: requerido, mínimo 6 caracteres (el mínimo que exige Firebase Authentication).

`200 OK` → `UsuarioProvisioningResponseDto`:

```json
{ "message": "Administrador creado exitosamente.", "usuarioId": "usuario-...", "personaId": "persona-..." }
```

### `POST /api/users/organizador` — Policy: `USUARIO_CREAR_ORGANIZADOR`

Solo un Administrador autenticado puede crear un Organizador (los organizadores no se autoregistran). Mismo body/respuesta que arriba.

### `POST /api/users/control` — Policy: `CONTROL_CREAR`

Solo un Organizador autenticado puede crear personal de Control, y únicamente **para un evento propio**:

```json
{ "userName": "control_puerta_norte", "password": "unaContraseñaSegura", "eventId": "evento-..." }
```

- `userName`: requerido. El email de la identidad de Firebase se construye como `{userName}@control.hoydonde.com` — el Control no necesita (ni provee) un email real.
- `password`: requerido, mínimo 6 caracteres.
- `eventId`: requerido. Se compara `Event.OrganizadorPersonaId` (releído de Firestore) contra el `PersonaId` resuelto del actor autenticado **antes** de tocar Firebase o Firestore.

`200 OK` → mismo `UsuarioProvisioningResponseDto` (`Message = "Control creado exitosamente."`).

### Compensación ante fallo parcial

Las tres altas anteriores crean primero la identidad en el proveedor externo (Firebase) y luego provisionan `Persona`+`Usuario`+`UsuarioRol`+`IdentidadExterna` en Firestore. Si el paso de Firestore falla **después** de crear la identidad de Firebase, esa identidad se borra (`IIdentityProvider.DeleteIdentityAsync`). Si el borrado de compensación también falla, se registra un `IdentidadHuerfana` (colección `identidades_huerfanas`) con ambos errores — nunca se pierde el rastro en silencio.

---

## 7. Eventos (`/api/events`)

### Estados y vigencia

Estados **persistidos** (`Event.EventStatus`): `Borrador`, `Publicado`, `Cancelado`. `Finalizado` **no se persiste**: es un estado efectivo derivado, `Estado == Publicado && UtcNow > FechaFin`, calculado en cada lectura/operación (`Event.GetEstadoEfectivo`) y expuesto como `EventResponse.Estado`.

Transiciones válidas:

| Desde | Hacia | Condición |
|---|---|---|
| `Borrador` | `Publicado` | ≥1 tipo de ticket |
| `Borrador` | `Cancelado` | sin condición |
| `Publicado` (no finalizado) | `Cancelado` | `UtcNow <= FechaFin` |
| `Publicado` (finalizado) | `Cancelado` | **inválida** — 409 |
| cualquier otra combinación (doble publicación, doble cancelación, reactivar `Cancelado`) | — | **inválida** — 409 |

Tres vigencias distintas, cada una con su propia condición exacta:

| Operación | Condición |
|---|---|
| Catálogo público / detalle público | `Estado == Publicado && UtcNow <= FechaFin` |
| Compra de tickets | `Estado == Publicado && UtcNow < FechaInicio` |
| Validación de tickets | `Estado == Publicado && UtcNow <= FechaFin` |

Un evento publicado es **inmutable**: no hay "despublicar"; para corregirlo hay que cancelarlo. Solo un evento en `Borrador` es editable (`PUT`), y esa edición reemplaza la colección completa de tipos de ticket (no hay edición incremental por id).

### Endpoints

| Método y ruta | Policy | Notas |
|---|---|---|
| `POST /api/events` | `EVENTO_CREAR` | Crea en `Borrador`. |
| `PUT /api/events/{eventId}` | `EVENTO_EDITAR_PROPIO` | Solo mientras `Borrador`; reemplaza `TicketGroups` completo. |
| `POST /api/events/{eventId}/publish` | `EVENTO_PUBLICAR_PROPIO` | `Borrador → Publicado`. |
| `POST /api/events/{eventId}/cancel` | `EVENTO_CANCELAR_PROPIO` | Ver tabla de transiciones. |
| `GET /api/events/{eventId}` | *(anónimo)* | Solo `Publicado` y no finalizado; cualquier otro caso → 404 (nunca 403, para no confirmar existencia). |
| `GET /api/events` | *(anónimo)* | Catálogo/búsqueda paginada, misma vigencia que el detalle público. |
| `GET /api/events/organizer/me` | `EVENTO_VER_PROPIOS` | Todos los eventos propios, cualquier estado. |
| `GET /api/events/organizer/{id}` | `EVENTO_VER_PROPIOS` | Detalle de un evento propio en cualquier estado; 403 si no es propio, 404 si no existe. |
| `POST /api/events/{eventId}/controls/{controlPersonaId}` | `CONTROL_CREAR` | Asigna un Control **ya existente** a otro evento propio (§8). |

Ningún endpoint de lectura devuelve el modelo `Event` crudo: todos devuelven `EventResponse` o `PagedResponse<EventResponse>`.

### Filtros de `GET /api/events` (Frontend 5 — Cartelera)

Todos opcionales y combinables, se aplican en la propia consulta de Firestore (`EventService.SearchEventsAsync`) antes del cursor y del `limit` — nunca hay filtrado en memoria:

| Query param | Tipo | Semántica |
|---|---|---|
| `fechaDesde` | ISO 8601 UTC | Filtra `Event.FechaInicio >= fechaDesde` (**inclusiva**). |
| `fechaHasta` | ISO 8601 UTC | Filtra `Event.FechaInicio < fechaHasta` (**exclusiva**). Para incluir el día completo "Hasta", el llamador debe enviar el inicio del día **siguiente**, nunca `23:59:59.999`. |
| `categoria` | `Event.EventCategory` | Igualdad exacta; un valor que no exista en el enum responde `VALIDATION_ERROR` (400) vía el propio model binding, sin lógica adicional en el servicio. |
| `ubicacion` | texto | Igualdad **exacta** tras `Trim()` del lado del servidor — no es búsqueda parcial ni difusa. |
| `lastEventId` | string | Cursor de paginación: id del último `Event` devuelto en la página anterior. |
| `limit` | int | Tamaño de página (default 20). |

`fechaDesde`/`fechaHasta` reemplazan al antiguo filtro de un solo lado (`fechaInicio`, solo cota inferior, sin cota superior): no tenía consumidores reales (ni frontend ni tests), así que se migró directamente en vez de mantener dos parámetros con significados ambiguos.

Visibilidad pública: siempre `Estado == Publicado && UtcNow <= FechaFin`, igual que el detalle público — los filtros de arriba se combinan con esa condición, nunca la reemplazan. `fechaDesde > fechaHasta` responde `EVENT_VALIDATION_ERROR` (400, §11) antes de tocar Firestore.

Paginación: la respuesta es `PagedResponse<EventResponse>` (`data`, `lastDocumentId`, `hasNextPage`); pedir la página siguiente repite exactamente los mismos filtros de la primera llamada junto con `lastEventId` — cambiar cualquier filtro entre llamadas invalida el cursor previo (hay que volver a paginar desde el principio).

Ejemplo: `GET /api/events?fechaDesde=2026-08-05T00:00:00Z&fechaHasta=2026-08-11T00:00:00Z&categoria=Musica&ubicacion=Parque%20Central&limit=10`.

### `EventCreateRequest` / `EventUpdateRequest`

```json
{
  "nombre": "Festival de Verano",
  "descripcion": "Un evento de ejemplo",
  "fechaInicio": "2026-12-01T22:00:00Z",
  "fechaFin": "2026-12-02T04:00:00Z",
  "ubicacion": "Parque Central",
  "categoria": "Musica",
  "ticketGroups": [
    { "nombre": "General", "precio": 5000, "cantidadDisponible": 200 },
    { "nombre": "VIP", "precio": 12000, "cantidadDisponible": 50 }
  ]
}
```

Reglas: `nombre`/`ubicacion` no vacíos; `fechaInicio` estrictamente futura; `fechaFin` estrictamente posterior a `fechaInicio`; al crear, `ticketGroups` requiere ≥1 elemento (al editar, no — `publish` es quien lo exige); cada tipo de ticket con `nombre` no vacío, `precio >= 0` (entradas gratuitas permitidas), `cantidadDisponible >= 1`.

### `EventResponse`

`ticketGroups` usa el DTO de **salida** `TicketTypeResponseDto` — no `TicketGroupDto` (ese es exclusivamente de entrada, §7.1 abajo) y nunca el modelo de persistencia `TicketType`. Cada elemento incluye el `id` real generado por el servidor al crear el evento:

```json
{
  "id": "evento-...",
  "nombre": "Festival de Verano",
  "descripcion": "Un evento de ejemplo",
  "fechaInicio": "2026-12-01T22:00:00Z",
  "fechaFin": "2026-12-02T04:00:00Z",
  "ubicacion": "Parque Central",
  "categoria": "Musica",
  "estado": "Publicado",
  "ticketGroups": [
    { "id": "tipo-...", "nombre": "General", "precio": 5000, "cantidadDisponible": 187 }
  ]
}
```

`estado` es el **efectivo/derivado**: `"Borrador"`, `"Publicado"`, `"Cancelado"` o `"Finalizado"`.

**Serialización de enums**: `estado` y `categoria` viajan siempre como el **nombre** del valor (`"Publicado"`, `"Musica"`, etc.), nunca como el entero subyacente — configurado globalmente en `Program.cs` vía `JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false)`. Esto aplica tanto a las respuestas (`EventResponse.Estado`/`Categoria`) como a los requests que aceptan `categoria` (`EventCreateRequest`/`EventUpdateRequest`, §7 abajo): un `categoria` numérico o un nombre que no exista en el enum se rechaza en la deserialización, lo que ASP.NET Core traduce automáticamente en el mismo contrato uniforme de error (`code: "VALIDATION_ERROR"`, §11) — ningún controller necesita código adicional para esto. `TicketResponseDto.Estado` (`"Emitido"`/`"Usado"`/`"Anulado"`) ya era un `string` calculado en el servicio antes de este ajuste, así que no depende de esta configuración global.

Los seis endpoints de lectura de eventos devuelven este mismo `id` real por tipo de ticket — **creación**, **actualización**, **detalle público** (`GET /api/events/{eventId}`), **búsqueda pública** (`GET /api/events`), **lista de eventos propios** (`GET /api/events/organizer/me`) y **detalle autenticado del organizador** (`GET /api/events/organizer/{id}`) —, así que un cliente real puede resolver qué tipo de ticket comprar usando **exclusivamente** la respuesta HTTP de cualquiera de ellos, sin necesitar ningún otro medio para conocer el `TicketTypeId`.

### 7.1. `TicketGroupDto` — solo entrada — vs. `TicketTypeResponseDto` — solo salida

- **`TicketGroupDto`** (usado en `EventCreateRequest`/`EventUpdateRequest.ticketGroups`): `nombre`, `precio`, `cantidadDisponible`. **Nunca** tiene un campo `id` — el request de creación/edición ni acepta ni necesita un `TicketTypeId`, porque siempre reemplaza/crea la colección completa y el identificador lo genera el servidor.
- **`TicketTypeResponseDto`** (usado en `EventResponse.ticketGroups`): `id`, `nombre`, `precio`, `cantidadDisponible`. El `id` es el `TicketTypeId` real, persistido, generado por `EventService` — nunca aceptado del cliente, nunca calculado en el momento de leer.

Ejemplo de flujo completo reutilizando el `id` recibido:

```jsonc
// 1) POST /api/events -> 200 OK
{
  "id": "evento-abc",
  // ...
  "ticketGroups": [
    { "id": "tipo-general-123", "nombre": "General", "precio": 5000, "cantidadDisponible": 200 }
  ]
}
```

```json
// 2) POST /api/tickets/buy — el ticketTypeId es exactamente el id recibido arriba, sin
//    resolverlo por ninguna otra vía
{ "eventoId": "evento-abc", "ticketTypeId": "tipo-general-123", "cantidad": 1 }
```

---

## 8. Control: asignar un Control existente a otro evento

```
POST /api/events/{eventId}/controls/{controlPersonaId}
Authorize: CONTROL_CREAR
```

Reutiliza la policy `CONTROL_CREAR` ya existente — no se agregó una 21ª acción. No crea ninguna identidad, `Persona`, `Usuario` ni `UsuarioRol` nueva: solo vincula un Control ya provisionado a un evento adicional del mismo organizador.

Reglas, en orden:

1. El actor autenticado debe ser `Event.OrganizadorPersonaId` del evento destino (releído de Firestore) → si no, 403.
2. El evento debe existir → si no, 404.
3. El evento no puede estar `Cancelado` ni `Publicado` con estado efectivo `Finalizado` → si no, 409.
4. `controlPersonaId` debe corresponder a un `Usuario` existente, activo, con rol `CONTROL` activo. Los tres motivos de rechazo posibles (no existe / inactivo / sin rol Control) colapsan en **un único** mensaje público, para no filtrar si una `PersonaId` dada existe en el sistema → 404.
5. Ese Control debe tener ya, como mínimo, **una** asignación previa creada por este mismo organizador (a cualquier evento) — evita que un organizador se apropie de un Control administrado exclusivamente por otro → si no, 403.

Idempotente: llamar dos veces con el mismo par `(controlPersonaId, eventId)` es un no-op exitoso; siempre devuelve `AssignedByPersonaId`/`CreatedAt` de la **primera** asignación.

Respuesta `200 OK` (`ControlAsignacionResponseDto`) — nunca expone el UID de Firebase ni el `UsuarioId`:

```json
{
  "controlPersonaId": "persona-control-...",
  "eventId": "evento-...",
  "assignedByPersonaId": "persona-organizador-...",
  "createdAt": "2026-08-02T15:00:00Z"
}
```

### 8.1. Consultas operativas (API-MVP 5)

Tres endpoints de solo lectura para que Organizador y Control elijan Control/evento desde una lista real, sin copiar `PersonaId`/`EventId` a mano. Ninguno crea, modifica ni elimina una `ControlAsignacion`; ninguno agrega una acción nueva (reutilizan `CONTROL_CREAR`/`TICKET_VALIDAR`).

#### `GET /api/events/organizer/controls` — Policy: `CONTROL_CREAR`

Controles distintos que el organizador autenticado asignó alguna vez, a cualquiera de sus eventos (sin duplicados, orden determinístico). `200 OK` → `ControlResumenResponseDto[]`:

```json
[
  { "controlPersonaId": "persona-control-...", "userName": "control_puerta_norte", "activo": true }
]
```

`200 []` si el organizador nunca asignó ningún Control (nunca 404). Un Control desactivado sigue apareciendo, con `activo: false`.

#### `GET /api/events/{eventId}/controls` — Policy: `CONTROL_CREAR`

Controles asignados a un evento propio. Mismo criterio de ownership que §8: 403 (`EVENT_OWNERSHIP`) si el evento es de otro organizador, 404 (`EVENT_NOT_FOUND`) si no existe. `200 OK` → `ControlAsignadoResponseDto[]`:

```json
[
  {
    "controlPersonaId": "persona-control-...",
    "userName": "control_puerta_norte",
    "activo": true,
    "assignedByPersonaId": "persona-organizador-...",
    "createdAt": "2026-08-02T15:00:00Z"
  }
]
```

`200 []` si el evento no tiene controles asignados.

#### `GET /api/events/control/me` — Policy: `TICKET_VALIDAR`

Eventos a los que el Control autenticado fue asignado, en cualquier estado persistido/efectivo — la UI decide qué habilitar; la validación (§9) sigue aplicando sus propias reglas de vigencia. `200 OK` → `EventoAsignadoResponseDto[]`:

```json
[
  {
    "eventId": "evento-...",
    "nombre": "Festival de Verano",
    "ubicacion": "Parque Central",
    "fechaInicio": "2026-12-01T22:00:00Z",
    "fechaFin": "2026-12-02T04:00:00Z",
    "estado": "Publicado"
  }
]
```

`200 []` si el Control no tiene asignaciones.

Los tres DTOs nunca incluyen UID de Firebase, `ExternalSubjectId`, `UsuarioId`, DNI, teléfono, roles/acciones completos, ni tipos de ticket/precio/stock. `userName` se deriva del email sintético del Control (`{userName}@control.hoydonde.com`, §6). Usuarios y Eventos relacionados se resuelven siempre en batch (`WhereIn`/lectura batch), nunca con una consulta por fila.

---

## 9. Tickets (`/api/tickets`)

### `POST /api/tickets/buy` — Policy: `TICKET_COMPRAR`

```json
{ "eventoId": "evento-...", "ticketTypeId": "tipo-...", "cantidad": 2 }
```

`cantidad`: 1 a 10 por operación. Dentro de una única transacción de Firestore: se relee el `Event`, se valida vigencia de compra (`Publicado && UtcNow < FechaInicio`), se valida que `ticketTypeId` exista en ese evento y que haya stock suficiente, se descuenta el stock y se emiten los tickets — todo o nada. La transacción de Firestore (reintento automático en conflicto) es lo que evita sobreventa bajo compras concurrentes contra el mismo stock.

Cada `Ticket` emitido persiste una **fotografía inmutable** tomada de esa misma lectura transaccional: `EventoNombre`, `TicketTypeNombre`, `PrecioPagado`, `FechaInicio`, `FechaFin`. Ninguna de las cinco se recalcula después ni se acepta del cliente (`TicketBuyRequest` no tiene campo de fecha ni de precio).

`200 OK` → `List<TicketResponseDto>` (uno por ticket comprado, ver forma abajo).

### `GET /api/tickets/me` — Policy: `TICKET_VER_PROPIO`

Devuelve todos los tickets del cliente autenticado. Resuelve los eventos involucrados con una única lectura batch por evento distinto (nunca una lectura por ticket).

### `POST /api/tickets/validate` — Policy: `TICKET_VALIDAR`

```
POST /api/tickets/validate?ticketId=ticket-...&eventId=evento-...
```

El `PersonaId` del Control sale exclusivamente del token (nunca del query). Dentro de una transacción: se relee el `Ticket` y el `Event`, se verifica que el Control esté asignado a ese evento (`ControlAsignacion`), se verifica vigencia de validación (`Publicado && UtcNow <= FechaFin`) y que el ticket no esté ya `Usado`/`Anulado`. Si todo es válido, se marca `Usado`, se graba `FechaUso` y `ValidadoPorPersonaId` (nunca aceptado del cliente).

Respuestas posibles:

| Resultado de dominio | HTTP | Body |
|---|---|---|
| `Success` | 200 | `{ "valid": true, "message": "Ticket validado para este evento." }` |
| `NotAuthorized` (Control no asignado a ese evento) | 403 | `{ "valid": false, "message": "No autorizado para validar tickets de este evento." }` |
| `AlreadyUsed` | 409 | `{ "valid": false, "message": "El ticket ya fue utilizado." }` |
| `Anulado` | 409 | `{ "valid": false, "message": "El ticket fue anulado." }` |
| `EventoCancelado` | 409 | `{ "valid": false, "message": "El evento fue cancelado." }` |
| `EventoFinalizado` | 409 | `{ "valid": false, "message": "El evento ya finalizó." }` |
| `NotFound` | 404 | `{ "valid": false, "message": "Ticket no encontrado." }` |

Un evento cancelado o finalizado **nunca** deja tocar (escribir) el documento del `Ticket`: su `Estado` histórico permanece `Emitido`, aunque la validación se rechace.

### `TicketResponseDto`

```json
{
  "id": "ticket-...",
  "eventoId": "evento-...",
  "ticketTypeId": "tipo-...",
  "clientePersonaId": "persona-cliente-...",
  "fechaCompra": "2026-08-02T12:00:00Z",
  "estado": "Emitido",
  "utilizable": true,
  "motivoNoUtilizable": null,
  "eventoNombre": "Festival de Verano",
  "ticketTypeNombre": "General",
  "precioPagado": 5000,
  "fechaInicio": "2026-12-01T22:00:00Z",
  "fechaFin": "2026-12-02T04:00:00Z"
}
```

- `estado`: histórico/persistido (`Emitido`/`Usado`/`Anulado`) — nunca reescrito por una cancelación de evento.
- `utilizable`/`motivoNoUtilizable`: **derivados en el momento de la lectura** contra el `Event` actual (no la fotografía). `motivoNoUtilizable` es `null` si `utilizable == true`; si no, uno de `"Usado"`, `"Anulado"`, `"EventoCancelado"`, `"EventoFinalizado"`.
- `eventoNombre`/`ticketTypeNombre`/`precioPagado`/`fechaInicio`/`fechaFin`: fotografía inmutable de la compra — nunca se recalculan contra el `Event` actual, ni siquiera si este cambiara.

---

## 10. Administración de seguridad (`/api/security`)

Roles y acciones son entidades Firestore administrables, no constantes de código. Todo bajo `/api/security` requiere una de las policies `ROL_EDITAR` / `ROL_CREAR` / `ROL_ACTIVAR` / `ROL_ASIGNAR_ACCION` / `ROL_QUITAR_ACCION` / `ROL_ELIMINAR` / `USUARIO_ASIGNAR_ROL` / `USUARIO_QUITAR_ROL` / `USUARIO_VER_PERMISOS_EFECTIVOS` / `USUARIO_DESACTIVAR` según el endpoint — hoy, en la práctica, solo el rol `ADMINISTRADOR` las tiene todas asignadas.

| Método y ruta | Policy | Descripción |
|---|---|---|
| `POST /api/security/roles` | `ROL_CREAR` | Crea un rol (`CreateRolRequestDto`: `codigo`, `nombre`, `descripcion`). |
| `PUT /api/security/roles/{codigo}` | `ROL_EDITAR` | Edita nombre/descripción (`UpdateRolRequestDto`); el código es inmutable. |
| `POST /api/security/roles/{codigo}/activar` | `ROL_ACTIVAR` | Activa un rol (baja lógica inversa). |
| `POST /api/security/roles/{codigo}/desactivar` | `ROL_ACTIVAR` | Baja lógica: desactiva un rol (bloqueado si dejaría 0 Administradores efectivos). |
| `DELETE /api/security/roles/{codigo}` | `ROL_ELIMINAR` | Baja física: borra el rol de forma permanente. Ver §10.1. |
| `GET /api/security/roles` | `ROL_EDITAR` | Lista todos los roles. |
| `GET /api/security/roles/{codigo}` | `ROL_EDITAR` | Detalle de un rol, incluida su lista de acciones. |
| `GET /api/security/acciones` | `ROL_ASIGNAR_ACCION` | Lista el catálogo completo de acciones. |
| `POST /api/security/roles/{rolCodigo}/acciones/{accionCodigo}` | `ROL_ASIGNAR_ACCION` | Asigna una acción a un rol. |
| `DELETE /api/security/roles/{rolCodigo}/acciones/{accionCodigo}` | `ROL_QUITAR_ACCION` | Quita una acción de un rol. |
| `POST /api/security/usuarios/{usuarioId}/roles/{rolCodigo}` | `USUARIO_ASIGNAR_ROL` | Asigna un rol a un usuario. |
| `DELETE /api/security/usuarios/{usuarioId}/roles/{rolCodigo}` | `USUARIO_QUITAR_ROL` | Quita un rol de un usuario (mismo guard del último Administrador). |
| `GET /api/security/usuarios` | `USUARIO_VER_PERMISOS_EFECTIVOS` | Lista usuarios (`UsuarioResumenResponseDto`: nunca expone `ExternalSubjectId`). |
| `GET /api/security/usuarios/{usuarioId}/permisos-efectivos` | `USUARIO_VER_PERMISOS_EFECTIVOS` | Roles/acciones efectivas resueltas en vivo. |
| `POST /api/security/usuarios/{usuarioId}/activar` | `USUARIO_DESACTIVAR` | Activa un usuario. |
| `POST /api/security/usuarios/{usuarioId}/desactivar` | `USUARIO_DESACTIVAR` | Desactiva un usuario (mismo guard del último Administrador). |

Toda mutación que dejaría el sistema sin **ningún** Administrador efectivo se rechaza transaccionalmente (`UltimoAdministradorException` → 409), evaluado dentro de la misma transacción de Firestore que intentaría el cambio — nunca se llega a escribir el estado inválido. Solo una mutación que **efectivamente ocurrió** genera un registro en `security_audits`; una operación no-op idempotente nunca audita.

`PermisosEfectivosResponseDto`:

```json
{
  "usuarioId": "usuario-...",
  "personaId": "persona-...",
  "usuarioActivo": true,
  "roles": ["ORGANIZADOR"],
  "acciones": ["EVENTO_CREAR", "EVENTO_EDITAR_PROPIO", "..."]
}
```

### 10.1. Baja lógica vs. baja física de un Rol (docs/api-mvp-plan.md §12)

- **Baja lógica** (`POST /api/security/roles/{codigo}/desactivar`, policy `ROL_ACTIVAR`): `Rol.Activo = false`. El rol y todas sus asignaciones (`RolAccion`, `UsuarioRol` de cualquier usuario) se conservan intactas; un rol inactivo simplemente deja de otorgar acciones efectivas. Reversible en cualquier momento (`.../activar`). Idempotente: repetir el mismo estado no vuelve a auditar. Mismo guard transaccional del último Administrador que el resto de `/api/security`.
- **Baja física** (`DELETE /api/security/roles/{codigo}`, policy `ROL_ELIMINAR`, una acción independiente — nunca implícita en `ROL_ACTIVAR`): borra el documento `Rol` y toda su subcolección `roles/{codigo}/acciones`. Solo se permite cuando se cumplen **todas** estas condiciones, evaluadas dentro de una única transacción Firestore:
  1. El rol existe (si no, 404 `ROLE_NOT_FOUND`).
  2. No es uno de los 4 roles esenciales sembrados por `SecurityCatalogSeeder` — `ADMINISTRADOR`/`ORGANIZADOR`/`CLIENTE`/`CONTROL` nunca pueden eliminarse físicamente (409 `ROL_PROTEGIDO`).
  3. El rol ya está inactivo (409 `ROL_DEBE_ESTAR_INACTIVO` si sigue activo — hay que darlo de baja lógica primero).
  4. No tiene ninguna asignación `UsuarioRol`, activa **ni inactiva** (409 `ROL_TIENE_USUARIOS_ASIGNADOS`) — evita dejar una asignación histórica apuntando a un rol que ya no existe.
- Nunca borra la `Accion` del catálogo (`acciones/{codigo}`) ni ningún `Usuario`; nunca toca otros roles. Las entradas de `security_audits` previas al rol se conservan siempre — la eliminación solo agrega una entrada nueva (`ROL_ELIMINAR`), nunca reescribe ni borra el historial.
- La colección raíz del catálogo (`roles`) y la subcolección `usuarios/{usuarioId}/roles` (`UsuarioRol`) comparten el mismo nombre de colección en Firestore; la verificación de la condición 4 usa una collection-group query que descarta explícitamente los documentos sin un `Usuario` padre real, para no confundir ambos orígenes.
- `FirestoreUsuarioRepository.AsignarRolAsync` lee el documento `Rol` dentro de su propia transacción (no solo el chequeo previo de `SecurityAdminService`), de modo que Firestore serialice correctamente una asignación concurrente contra una baja física del mismo rol — nunca queda una asignación huérfana apuntando a un rol ya borrado.
- **Comando para el Firestore real ya existente** (mismo patrón que `seed-report-actions`, §15): `dotnet run --project HoyDonde.API -- seed-role-deletion-action` crea únicamente la Accion `ROL_ELIMINAR` (idempotente, nunca la asigna a ningún rol, nunca toca roles/usuarios existentes).

---

## 11. Contrato uniforme de error

Todo error de este API (excepción de dominio tipada, `ModelState` inválido, o una excepción no anticipada) responde con la misma forma (`ExceptionMiddleware`, único punto central de mapeo — ningún controller mapea excepciones a HTTP por su cuenta):

```json
{
  "code": "EVENT_NOT_FOUND",
  "message": "El evento 'evento-123' no existe.",
  "traceId": "3fa4c9d2-....",
  "errors": null
}
```

- **`code`**: string estable, comprobable por tests — nunca cambia entre versiones para la misma condición de error. Ver tabla completa abajo.
- **`message`**: texto público. Para la enorme mayoría de las excepciones tipadas es directamente su mensaje (contiene solo ids/nombres ya conocidos por quien hizo la petición, nunca UID/`ExternalSubjectId`/nombres internos de colección/mensajes crudos de Firestore o Firebase). La única excepción deliberada es `EVENT_OWNERSHIP`, cuyo mensaje interno incluye el UID del actor (solo para logging): la respuesta pública usa un texto fijo genérico.
- **`traceId`**: correlaciona la respuesta con el log estructurado del servidor para esa misma request (mismo id que `RequestId` en los logs de `LoggingMiddleware`).
- **`errors`**: **solo** presente para errores de validación de campos (`code: "VALIDATION_ERROR"`), como `{ "ubicacion": ["La ubicación del evento es obligatoria."] }`. En cualquier otro error, este campo está ausente (no `null` explícito — se omite del JSON).
- **`detail`** *(no listado arriba porque casi siempre está ausente)*: solo aparece en un `500` en entorno `Development`, con el detalle técnico de la excepción para diagnóstico local. En `Production`, un `500` **nunca** incluye este campo ni ningún detalle interno — el `message` es un texto genérico fijo ("Ocurrió un error inesperado. Contactá al soporte indicando el TraceId.").

### Códigos por excepción tipada

| HTTP | Code | Origen típico |
|---|---|---|
| 400 | `VALIDATION_ERROR` | `ModelState` inválido (DataAnnotations) o `ArgumentException` de servicio |
| 400 | `EVENT_VALIDATION_ERROR` | Reglas de negocio de Event/TicketGroup a nivel de servicio |
| 403 | `IDENTITY_NOT_PROVISIONED` | Token válido, pero sin `Usuario` provisionado para ese UID |
| 403 | `EVENT_OWNERSHIP` | El actor no es el organizador dueño del evento |
| 403 | `CONTROL_FOREIGN` | El Control pertenece exclusivamente a otro organizador |
| 404 | `EVENT_NOT_FOUND` | Evento inexistente |
| 404 | `TICKET_TYPE_INVALID` | `ticketTypeId` no corresponde al evento |
| 404 | `CONTROL_INVALID` | `controlPersonaId` no es un Control elegible |
| 404 | `ROLE_NOT_FOUND` / `ACTION_NOT_FOUND` / `USER_NOT_FOUND` | Recurso de administración de seguridad inexistente |
| 409 | `EVENT_INVALID_TRANSITION` | Transición de estado no permitida |
| 409 | `EVENT_MISSING_TICKET_TYPES` | Publicar sin tipos de ticket |
| 409 | `EVENT_NOT_EDITABLE` | Editar un evento fuera de `Borrador` |
| 409 | `EVENT_NOT_AVAILABLE_FOR_PURCHASE` | Compra fuera de vigencia |
| 409 | `EVENT_NOT_AVAILABLE_FOR_CONTROL_ASSIGNMENT` | Asignar Control a un evento `Cancelado`/`Finalizado` |
| 409 | `TICKET_STOCK_INSUFFICIENT` | Stock insuficiente para la cantidad pedida |
| 409 | `IDENTITY_EMAIL_ALREADY_EXISTS` | Email ya usado en el proveedor de identidad |
| 409 | `ROLE_ALREADY_EXISTS` / `ACTION_ALREADY_EXISTS` | Código de rol/acción duplicado |
| 409 | `LAST_ADMINISTRATOR` | La operación dejaría el sistema sin Administradores efectivos |
| 409 | `ROL_PROTEGIDO` | Baja física de uno de los 4 roles esenciales |
| 409 | `ROL_DEBE_ESTAR_INACTIVO` | Baja física de un rol todavía activo |
| 409 | `ROL_TIENE_USUARIOS_ASIGNADOS` | Baja física de un rol con al menos una `UsuarioRol` (activa o inactiva) |
| 500 | `UNEXPECTED_ERROR` | Cualquier excepción no anticipada |

`403 Forbidden` "puro" (sin body de `ErrorResponse`, generado directamente por el middleware de autorización de ASP.NET) ocurre cuando el actor está autenticado pero la policy nunca llega a `Succeed` — por ejemplo, un `Usuario` desactivado, o uno sin la acción concedida. `401 Unauthorized` ocurre cuando no hay identidad autenticada en absoluto (sin header `Authorization`, o token inválido/expirado).

---

## 12. Reglas de compra y validación (resumen operativo)

- Una compra exige `Publicado && UtcNow < FechaInicio`: no se puede comprar una vez que el evento ya empezó, aunque siga visible y validando entradas.
- Una validación exige `Publicado && UtcNow <= FechaFin`: se puede validar desde que el evento está publicado (incluso antes de `FechaInicio`, sin ventana de "apertura de puertas") hasta `FechaFin` inclusive.
- Cancelar un evento **nunca** actualiza tickets en batch: el bloqueo de compra/validación es consecuencia directa de que ambas exigen `Estado == Publicado`, reevaluado en cada operación dentro de su propia transacción.
- Ningún ticket puede validarse dos veces: la segunda validación de un ticket ya `Usado` se rechaza con 409 (`AlreadyUsed`), verificado también bajo validaciones concurrentes del mismo ticket.
- Ninguna compra puede sobrevender el stock: verificado con N compras concurrentes contra stock 1 (exactamente una compra exitosa, `CantidadDisponible` nunca negativo).

---

## 13. Comandos reproducibles

```bash
# Build
dotnet build HoyDonde.sln

# Solo tests que no requieren Firestore Emulator
dotnet test HoyDonde.sln

# Suite completa (unit + controller + integración) contra Firestore Emulator real
npx --yes firebase-tools@13.35.1 emulators:exec --only firestore --project hoydonde-security-refactor-tests "dotnet test HoyDonde.sln"

# Levantar la API en local
dotnet run --project HoyDonde.API

# Bootstrap del primer Administrador (requiere Bootstrap:AllowAdminBootstrap=true)
dotnet run --project HoyDonde.API -- bootstrap-admin admin@hoydonde.com

# Sembrar únicamente la Accion ROL_ELIMINAR contra un Firestore real ya existente (§10.1)
dotnet run --project HoyDonde.API -- seed-role-deletion-action
```

No se requiere `firebase login` ni credenciales reales para el comando de test: `hoydonde-security-refactor-tests` es un ID de proyecto arbitrario que nunca resuelve contra un proyecto Firebase real, y el Firestore Emulator no valida credenciales.

`HoyDonde-frontend/` es un workspace npm independiente (`npm install`, `npm run start`, `npm run start:lan` para un dispositivo físico en la misma Wi-Fi, `npm run lint`, `npm run typecheck`, `npm test`) — no participa de `dotnet build`/`dotnet test`. Para probar desde un dispositivo físico vía Expo Go, la API debe escuchar en todas las interfaces: `dotnet run --project .\HoyDonde.API --urls "http://0.0.0.0:5053"`, con `EXPO_PUBLIC_API_URL` apuntando a la IP LAN de esa máquina (no `localhost`).

---

## 14. Capacidades fuera de este MVP

Explícitamente no implementadas (`docs/api-mvp-plan.md` §10) — no asumir que existen:

- Pagos reales, reservas temporales de stock, reventa de tickets.
- QR firmado / validación offline.
- Notificaciones push/email.
- Analíticas de organizador con pantalla/PDF (§15 documenta el backend ya implementado del reporte de eventos propios; falta la UI y la exportación).
- Auditoría de dominio (`security_audits`-equivalente) para compras/validaciones/cambios de estado de evento — hoy solo hay `ILogger` estructurado.
- Aprobación administrativa de eventos antes de publicar.
- Transición `Finalizado` persistida vía job/scheduler (permanece derivada indefinidamente).
- Endpoint de "desasignar" un Control de un evento.
- Edición de un evento después de publicado (ni siquiera parcial).
- Recuperación de contraseña vía UI del frontend.
- Pantallas más allá del alcance de Frontend 0 (catálogo, login, registro de Cliente, perfil/logout) — Frontend 1–5 (`docs/api-mvp-plan.md` §7) permanecen pendientes; ver `CLAUDE.md`, "Frontend status".

---

## 15. Módulo de reportes (docs/api-mvp-plan.md §11) — cerrado

Los tres reportes están implementados: eventos propios del Organizador, eventos globales del Administrador y auditoría de seguridad del Administrador (primer corte aprobado). Frontend y exportación a PDF (`expo-print`/`expo-sharing`) también están cerrados — ver CLAUDE.md, "Reports module".

### Acciones nuevas

`Authorization/Acciones.cs` suma `REPORTE_VER_GLOBAL` (usada por los dos reportes del Administrador) y `REPORTE_VER_PROPIO` (usada por el reporte del Organizador) — 20 → 22 acciones. `SecurityCatalogSeeder` las asigna a `ADMINISTRADOR`/`ORGANIZADOR` respectivamente, pero **solo para instalaciones nuevas** (dev/test/emulador): contra Firestore real, `SecurityCatalogSeeder.SeedAsync()` no vuelve a correr una vez que existe un Administrador efectivo. Contra el Firestore real ya existente (`hoydonde-f5a05`), ambas acciones ya fueron creadas (`seed-report-actions`) y asignadas manualmente a `ADMINISTRADOR`/`ORGANIZADOR`.

### Comando para Firestore real ya existente

```bash
dotnet run --project HoyDonde.API -- seed-report-actions
```

Crea únicamente los dos documentos `Accion` (`REPORTE_VER_GLOBAL`/`REPORTE_VER_PROPIO`), idempotente (informa "creada" o "ya existente" por acción, código de salida 0). **Nunca** crea/edita roles, **nunca** asigna una acción a un rol, **nunca** repone una asignación que un Administrador haya revocado. El Administrador asigna estas acciones a los roles que decida desde `/api/security` después de correr el comando; cada sesión afectada necesita `refreshSessionPermissions()` (o volver a loguearse) para verlo reflejado, mismo patrón que el resto de `/api/security`.

### `GET /api/reports/organizer/events` — Policy: `REPORTE_VER_PROPIO`

```
GET /api/reports/organizer/events?fechaDesde=2026-01-01T00:00:00Z&fechaHasta=2026-02-01T00:00:00Z
```

| Query param | Obligatorio | Semántica |
|---|---|---|
| `fechaDesde` | Sí | UTC explícito (con `Z`/offset — nunca interpretado con la zona horaria del servidor), inclusiva sobre `Event.FechaInicio`. |
| `fechaHasta` | Sí | UTC explícito, exclusiva sobre `Event.FechaInicio`. Rango máximo 366 días. |
| `estado` | No | `Borrador`/`Publicado`/`Cancelado`/`Finalizado` (efectivo, igual criterio que `EventResponse.Estado`). |
| `categoria` | No | `Event.EventCategory`. |
| `eventId` | No | Debe ser un evento propio (ownership releído de Firestore, nunca confiado del cliente). |
| `ticketTypeId` | Solo junto con `eventId` | Acota métricas y desglose a ese único tipo de entrada. |

`organizadorPersonaId` **no existe** como parámetro: el organizador sale siempre de `IAuthenticatedPersonaResolver` (UID del token → `PersonaId`).

Errores propios: rango ausente/invertido/sin UTC explícito/mayor a 366 días → 400 `REPORT_RANGE_INVALID`; `ticketTypeId` sin `eventId` → 400 `REPORT_FILTER_INVALID`. Reutiliza excepciones existentes para el resto: evento ajeno → 403 `EVENT_OWNERSHIP`; evento inexistente → 404 `EVENT_NOT_FOUND`; `ticketTypeId` que no pertenece al evento → 404 `TICKET_TYPE_INVALID`. Un evento propio fuera del rango/estado/categoría pedidos da un reporte vacío, nunca una fuga de datos.

Ownership: sin `eventId`, la query Firestore siempre incluye `WhereEqualTo(OrganizadorPersonaId, actorPersonaId)` junto al rango de `FechaInicio` — nunca solo un filtro en memoria después de leer. `estado`/`categoria` se aplican en memoria sobre ese conjunto ya acotado. Los tickets de los eventos resultantes se leen con `WhereIn(EventoId, chunk)` en lotes de **máximo 30** (límite real de Firestore), nunca una lectura por evento; si no hay eventos, no se ejecuta ninguna query de tickets. Lectura no transaccional (reporte informativo): una compra concurrente durante la generación puede quedar dentro o fuera según el momento exacto del snapshot.

Respuesta (`ReporteEventosResponseDto`):

```json
{
  "fechaDesde": "2026-01-01T00:00:00Z",
  "fechaHasta": "2026-02-01T00:00:00Z",
  "aclaracionImporte": "El MVP no procesa pagos reales: \"importe emitido\" es la suma de los precios fotografiados en cada ticket al comprar, nunca una recaudación ni un cobro real.",
  "resumen": {
    "cantidadEventos": 1, "capacidadInicial": 10, "stockDisponible": 8,
    "entradasEmitidas": 2, "entradasUsadas": 1, "entradasAnuladas": 0, "entradasPendientes": 1,
    "porcentajeOcupacion": 20.0, "porcentajeAsistencia": 50.0, "porcentajeUtilizacion": 10.0,
    "importeEmitido": 200.00
  },
  "eventos": [ { "eventId": "evento-...", "nombre": "...", "tiposDeEntrada": [ "..." ] } ]
}
```

Nunca expone `OrganizadorPersonaId`, UID de Firebase, `UsuarioId` ni `ExternalSubjectId` (el organizador ya es el actor autenticado). El importe siempre se llama **"importe emitido"**, nunca "recaudación"/"cobrado"/"ganancia" — `PrecioPagado` es la fotografía inmutable tomada en la compra (§7.1/§9), sumarla es válido para "cuánto se emitió", nunca para "cuánto se cobró" (el MVP no procesa pagos reales). Capacidad a nivel evento sale de `Event.CapacidadMaxima`; por tipo de entrada es una **derivación** (`CantidadDisponible` actual + entradas ya emitidas de ese tipo), no un dato persistido. División por cero en cualquier porcentaje → `0`, nunca una excepción.

### `GET /api/reports/admin/events` — Policy: `REPORTE_VER_GLOBAL`

```
GET /api/reports/admin/events?fechaDesde=2026-01-01T00:00:00Z&fechaHasta=2026-02-01T00:00:00Z&organizadorPersonaId=persona-...
```

Mismos `fechaDesde`/`fechaHasta`/`estado`/`categoria` que el reporte del Organizador (mismas reglas y códigos de error: `REPORT_RANGE_INVALID`). Agrega `organizadorPersonaId` opcional y **arbitrario** (aceptado del cliente: este endpoint es exclusivo de Administrador vía la policy). Deliberadamente **sin** `eventId`/`ticketTypeId`: es un reporte de actividad agregada, no un drill-down a un evento puntual.

Query Firestore: sin `organizadorPersonaId`, solo rango sobre `FechaInicio` (índice automático de campo simple); con `organizadorPersonaId`, se agrega `WhereEqualTo(OrganizadorPersonaId, ...)` — mismo índice compuesto que el reporte del Organizador (ver abajo). `estado`/`categoria` siempre en memoria. Tickets vía `WhereIn(EventoId, chunk)` en lotes de máximo 30, igual que el reporte del Organizador.

Respuesta (`ReporteAdminEventosResponseDto`): mismo shape que `ReporteEventosResponseDto`, pero cada elemento de `eventos` (`ReporteAdminEventoDetalleDto`) agrega `organizadorPersonaId` — nunca el Firebase UID, `UsuarioId` ni `ExternalSubjectId`. El frontend resuelve ese id a email reutilizando `GET /api/security/usuarios`.

### `GET /api/reports/admin/security-audits` — Policy: `REPORTE_VER_GLOBAL`

```
GET /api/reports/admin/security-audits?fechaDesde=2026-05-01T00:00:00Z&fechaHasta=2026-06-01T00:00:00Z&operacion=ROL_ASIGNAR_ACCION&actorUsuarioId=usuario-...&targetTipo=RolAccion&targetId=ORGANIZADOR%2FEVENTO_CREAR
```

| Query param | Obligatorio | Semántica |
|---|---|---|
| `fechaDesde` / `fechaHasta` | No | UTC explícito si se informan. Sin ninguno de los dos, default = últimos 30 días hasta `UtcNow`. Rango máximo 366 días si se informa explícitamente (`REPORT_RANGE_INVALID` → 400 en cualquier violación). |
| `operacion` | No | Código exacto tal como lo escribe `SecurityAdminService` (p. ej. `ROL_CREAR`, `ROL_ASIGNAR_ACCION`, `USUARIO_ASIGNAR_ROL`, `USUARIO_DESACTIVAR`, etc.). |
| `actorUsuarioId` | No | Match exacto contra `SecurityAudit.ActorUsuarioId`. |
| `targetTipo` | No | `Rol` \| `Usuario` \| `RolAccion` \| `UsuarioRol`. **`UsuarioRol` es una adición respecto al diseño original de §11** (que solo enumeraba los otros tres): es el valor real que persisten `AsignarRolAUsuarioAsync`/`QuitarRolDeUsuarioAsync`, necesario para poder filtrar esa operación. |
| `targetId` | No | Match exacto (nunca substring) contra `SecurityAudit.TargetId` — para `RolAccion`/`UsuarioRol` es el string compuesto `"{rol}/{accion}"` o `"{usuarioId}/{rol}"`. |

Solo el rango sobre `Timestamp` es una query Firestore (`WhereGreaterThanOrEqualTo`/`WhereLessThan` + `OrderByDescending`, índice automático de campo simple); `operacion`/`actorUsuarioId`/`targetTipo`/`targetId` se filtran en memoria sobre ese conjunto ya acotado por fecha (volumen esperado bajo). `ActorEmail` se resuelve en batch por referencia directa a documento (nunca `WhereIn`, nunca una lectura por auditoría).

Respuesta (`SecurityAuditReporteResponseDto`):

```json
{
  "fechaDesde": "2026-05-02T14:30:00Z",
  "fechaHasta": "2026-06-01T14:30:00Z",
  "auditorias": [
    { "timestamp": "2026-05-15T12:00:00Z", "operacion": "ROL_ASIGNAR_ACCION", "actorUsuarioId": "usuario-...", "actorEmail": "admin@hoydonde.com", "targetTipo": "RolAccion", "targetId": "ORGANIZADOR/EVENTO_CREAR", "detalle": "rol=ORGANIZADOR;accion=EVENTO_CREAR" }
  ]
}
```

`fechaDesde`/`fechaHasta` en la respuesta son siempre el rango **efectivo** aplicado (incluye el default de 30 días cuando el caller no informó ninguno), nunca lo que el caller mandó crudo. `actorEmail` es `null` si el `Usuario` actor ya no existe. Nunca expone `ActorPersonaId` ni ningún identificador del proveedor de identidad.

### Índice Firestore nuevo

```json
{ "collectionGroup": "events", "fields": [
  { "fieldPath": "OrganizadorPersonaId", "order": "ASCENDING" },
  { "fieldPath": "FechaInicio", "order": "ASCENDING" } ] }
```

Agregado a `firestore.indexes.json`, probado contra el Firestore Emulator y **desplegado y en estado READY contra el proyecto Firebase real** (`hoydonde-f5a05`). Cubre tanto el reporte del Organizador como el reporte del Administrador cuando filtra por `organizadorPersonaId` — no se agregó ningún índice nuevo para el reporte Admin ni para la auditoría de seguridad.

### Estado

Módulo completo (Organizador + Admin eventos + auditoría de seguridad + frontend + PDF), verificado contra Firestore Emulator real: **505 passed, 0 failed, 0 skipped** (suite completa; 2 tests de concurrencia no relacionados con este módulo son intermitentes bajo contención de la suite completa, verificados en verde de forma aislada). Frontend: `npm test` 408 passed, `npm run typecheck`/`npm run lint` limpios, `npx expo-doctor` 18/18, `npx expo export --platform android` exitoso, y **verificado a mano en Expo Go contra la API/Firestore reales** (ambos reportes de eventos y sus filtros, métricas coherentes, auditoría de seguridad y sus filtros, los tres PDF generándose/abriéndose/compartiéndose correctamente, Cliente/Control sin ningún acceso a reportes) — sin errores encontrados. **El módulo de reportes queda cerrado por completo.**
