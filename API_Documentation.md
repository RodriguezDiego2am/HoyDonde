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

1. El cliente (la app, hoy no integrada — ver `CLAUDE.md` "Frontend status") se autentica directamente contra **Firebase Authentication** usando el Firebase Client SDK (login/registro de email+contraseña, o el proveedor que corresponda).
2. Firebase devuelve un **ID token** (JWT) al cliente.
3. El cliente llama a cualquier endpoint autenticado de esta API con:

   ```
   Authorization: Bearer <id-token-de-firebase>
   ```

4. `Program.cs` valida ese JWT contra `https://securetoken.google.com/{Firebase:ProjectId}` (issuer/audience/lifetime) usando `Microsoft.AspNetCore.Authentication.JwtBearer`. La validación de firma/expiración es responsabilidad exclusiva de ese middleware; el resto del pipeline nunca vuelve a tocar el token crudo.
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
  "roles": ["CLIENTE"]
}
```

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
- El catálogo tiene exactamente **20 acciones** (`Authorization/Acciones.cs`), sembradas por `SecurityCatalogSeeder` en 4 roles iniciales: `ADMINISTRADOR`, `ORGANIZADOR`, `CLIENTE`, `CONTROL`. Roles y acciones son entidades Firestore administrables (`/api/security`, §9), no enums ni constantes de código.
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

Roles y acciones son entidades Firestore administrables, no constantes de código. Todo bajo `/api/security` requiere una de las policies `ROL_EDITAR` / `ROL_CREAR` / `ROL_ACTIVAR` / `ROL_ASIGNAR_ACCION` / `ROL_QUITAR_ACCION` / `USUARIO_ASIGNAR_ROL` / `USUARIO_QUITAR_ROL` / `USUARIO_VER_PERMISOS_EFECTIVOS` / `USUARIO_DESACTIVAR` según el endpoint — hoy, en la práctica, solo el rol `ADMINISTRADOR` las tiene todas asignadas.

| Método y ruta | Policy | Descripción |
|---|---|---|
| `POST /api/security/roles` | `ROL_CREAR` | Crea un rol (`CreateRolRequestDto`: `codigo`, `nombre`, `descripcion`). |
| `PUT /api/security/roles/{codigo}` | `ROL_EDITAR` | Edita nombre/descripción (`UpdateRolRequestDto`); el código es inmutable. |
| `POST /api/security/roles/{codigo}/activar` | `ROL_ACTIVAR` | Activa un rol. |
| `POST /api/security/roles/{codigo}/desactivar` | `ROL_ACTIVAR` | Desactiva un rol (bloqueado si dejaría 0 Administradores efectivos). |
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
```

No se requiere `firebase login` ni credenciales reales para el comando de test: `hoydonde-security-refactor-tests` es un ID de proyecto arbitrario que nunca resuelve contra un proyecto Firebase real, y el Firestore Emulator no valida credenciales.

`HoyDonde-frontend/` es un workspace npm independiente (`npm install`, `npx expo start`, `npx eslint .`, `npx tsc --noEmit`, `npx jest`) — no participa de `dotnet build`/`dotnet test`.

---

## 14. Capacidades fuera de este MVP

Explícitamente no implementadas (`docs/api-mvp-plan.md` §10) — no asumir que existen:

- Pagos reales, reservas temporales de stock, reventa de tickets.
- QR firmado / validación offline.
- Notificaciones push/email.
- Analíticas de organizador (dashboards de ventas/asistencia).
- Auditoría de dominio (`security_audits`-equivalente) para compras/validaciones/cambios de estado de evento — hoy solo hay `ILogger` estructurado.
- Aprobación administrativa de eventos antes de publicar.
- Transición `Finalizado` persistida vía job/scheduler (permanece derivada indefinidamente).
- Endpoint de "desasignar" un Control de un evento.
- Edición de un evento después de publicado (ni siquiera parcial).
- Recuperación de contraseña vía UI del frontend.
- Integración real del frontend con este flujo de autenticación (ver `CLAUDE.md`, "Frontend status": la app hoy no está conectada a Firebase Auth ni a `/api/auth/sync`).
