# Plan técnico: refactor del módulo de seguridad de HoyDonde? (v3)

> **Estado:** Etapas 0, 1, 2, 3, 4 y 5 implementadas y verificadas contra Firestore Emulator real. Etapa 6 pendiente. Etapa 5 (AuthorizationHandler, policies y administración de roles/acciones/usuarios, incluidas las correcciones de idempotencia y los listados administrativos del catálogo): **208 passed, 0 failed, 0 skipped**, suite completa vía `npx firebase-tools@13.35.1 emulators:exec --only firestore --project hoydonde-security-refactor-tests "dotnet test HoyDonde.sln"` (mismo entorno reproducible que las etapas anteriores, ver nota al final de §6, Etapa 2). El alcance de la Etapa 2 quedó corregido antes de su commit (ver §7). Decisiones de Cliente, bootstrap y test de atomicidad cerradas (§2.1, §5, §6). Premisa: **no hay usuarios, eventos, tickets ni datos de producción que preservar** — los datos de desarrollo se pueden reiniciar libremente. Esto elimina toda la complejidad de migración/backfill/branching legacy-vs-nuevo de las versiones anteriores de este plan.

---

## 0. Qué se mantiene sin cambios de v2

- Diagnóstico del modelo legacy (`ApplicationUser`/`Roles.cs`/`[Authorize(Roles=...)]`), ownership actual de Event/Ticket/Control y el mismatch de login del frontend: sin novedades, no se repite acá — ver commits de la Etapa 0/1 y el historial de este documento si hace falta el detalle.
- `IIdentityProvider`/`FirebaseIdentityProvider` (Etapa 0) y el catálogo `Rol`/`Accion` con ID = Codigo (Etapa 1): siguen siendo la base, sin cambios de diseño.

---

## 1. Modelo (fundación)

Sin cambios de fondo respecto a v2, **menos** `MigracionUsuario`/`MigracionEstado` (eliminados: no hay nada que backfillear).

```text
Persona.Id:               UUID propio del dominio, generado por la app
Usuario.Id:                UUID propio del módulo de seguridad, generado por la app
Usuario.PersonaId:         referencia a Persona.Id
Usuario.IdentityProvider:  "FIREBASE"
Usuario.ExternalSubjectId: UID de Firebase
IdentidadExterna:          identidades_externas/{IdentityProvider}#{ExternalSubjectId} -> UsuarioId
UsuarioRol:                usuarios/{UsuarioId}/roles/{RolCodigo}
RolAccion:                 roles/{Codigo}/acciones/{AccionCodigo}  (ya implementado, Etapa 1)
```

Persona sigue siendo el único puente con el dominio (`Event`, `Ticket`, `ControlAsignacion` referencian `PersonaId`, nunca UID externo ni `Usuario.Id`).

---

## 2. Aprovisionamiento

**Eliminado de v2:** doble escritura a `users`/`user_audits` legacy, `ResolveByEmailAsync`, recuperación automática por email, y todo el branching "identidad creada esta vez vs. recuperada".

### 2.1 Flujo Cliente — reutiliza `POST /api/auth/sync` (decisión cerrada)

1. El frontend (Firebase Client SDK, fuera de alcance de este documento — ver §8) crea/autentica la identidad y obtiene un ID token.
2. El frontend llama `POST /api/auth/sync` con ese token (`Authorization: Bearer`) — se reutiliza el endpoint existente, no se crea uno nuevo. Requiere un token de Firebase válido (sigue siendo `[Authorize]`, sin excepción anónima).
3. La API extrae `uid` y `email` **exclusivamente del token** — nunca del body.
4. Si no existe `Usuario` para ese `uid` (no hay `identidades_externas/FIREBASE#{uid}`): provisiona **solamente** `Persona+Usuario+UsuarioRol(CLIENTE)` con el perfil disponible en el token (email, y nombre si el token lo trae) y `AssignedBy = UserService.SelfRegistrationActor` ("SELF_REGISTRATION"). Nunca otro rol, nunca "Organizador por defecto".
5. Si ya existe: devuelve el `Usuario` actual tal cual, sin duplicar nada ni tocar sus roles.
6. La API **nunca** recibe ni almacena una contraseña de Cliente — el endpoint no tiene ningún campo de password.
7. **Compatibilidad de código, no de datos:** mientras sigan existiendo endpoints con `[Authorize(Roles = Roles.Cliente)]` (hasta que la Etapa 5 los reemplace por policies), el paso 4 también asigna temporalmente el custom claim `role=Cliente` sobre esa identidad, y la respuesta del sync incluye una señal explícita (p. ej. `claimsUpdated: true`) para que el frontend sepa que debe forzar la renovación del ID token (`getIdToken(true)`) antes de llamar esos endpoints legacy. Ese claim se elimina del todo en la Etapa 6, cuando ya no quede ningún `[Authorize(Roles=...)]` que lo necesite.

Esto reemplaza el comportamiento roto de `AuthService.SyncUserAsync` (hoy crea `Organizador` por defecto y no setea rol ni claim) por el único comportamiento posible: sync idempotente, siempre y únicamente CLIENTE para altas nuevas.

### 2.2 Flujo Admin / Organizador / Control (Firebase Admin vía `IIdentityProvider`)

1. El actor autenticado (Admin para Admin/Organizador; Organizador para Control) llama el endpoint correspondiente.
2. La API llama `IIdentityProvider.CreateIdentityAsync(email, password, displayName)`.
   - Si el email ya existe, `CreateIdentityAsync` sigue traduciendo el error real de FirebaseAdmin (`FirebaseAuthException`/`AuthErrorCode.EmailAlreadyExists`, confirmado contra el SDK 3.4.0) a `IdentityEmailAlreadyExistsException` — pero ahora **nunca se recupera automáticamente**: el controller la mapea a `409 Conflict` y listo. No se crea nada, no hay nada que compensar.
3. Si la identidad se creó, la API llama `SetTemporaryClaimAsync` y luego `IUsuarioRepository.ProvisionarAsync` (Persona+Usuario+UsuarioRol+IdentidadExterna, una sola transacción atómica, **sin** escritura a `users`/`user_audits`).
4. Si cualquiera de esos dos pasos falla, se compensa borrando **la identidad recién creada por esta misma llamada** (`DeleteIdentityAsync`) — ya no hace falta la bandera "¿la creé yo o la recuperé?", porque recuperar ya no existe: toda identidad que llega a este bloque la creó esta llamada.
5. Si el borrado de compensación también falla, se registra en `identidades_huerfanas` (se conserva de v2, sin cambios) con el error original y el de compensación.

`IIdentityProvider` pierde `ResolveByEmailAsync` (ya no tiene consumidor). El resto de la superficie (`CreateIdentityAsync`, `DeleteIdentityAsync`, `SetActiveAsync`, `UpdateAttributesAsync`, `GeneratePasswordResetLinkAsync`, `SetTemporaryClaimAsync`, `ClearClaimAsync`) se mantiene tal cual (algunas sin consumidor todavía, reservadas para las etapas de administración de usuarios).

### 2.3 `AssignedBy`

Sin cambios de v2: actor real del token para Admin/Organizador, `organizadorId` real para Control, `UserService.SelfRegistrationActor` para Cliente.

### 2.4 Control crea Control solo para eventos propios

Sin cambios: `RegisterControlAsync` sigue comparando `Event.OrganizadorId` (hoy) / `Event.OrganizadorPersonaId` (después de §4) contra el actor del token antes de aprovisionar nada.

---

## 3. Autorización

Sin cambios de fondo de v2 en el algoritmo de `PermissionService` (resolución directa sin caché, ver Etapa 1/2 ya implementadas), **menos** todo el branching por estado de migración, que se elimina por completo:

- El token de Firebase demuestra identidad (`ExternalSubjectId`), nada más.
- Firestore (`Usuario`→`UsuarioRol`→`Rol`→`RolAccion`→`Accion`) es la única fuente de roles/acciones.
- ASP.NET aplica `[Authorize(Policy = "ACCION_CODIGO")]` por endpoint, resuelto contra `IPermissionService`.
- El custom claim `role` puede seguir existiendo **solo como compatibilidad transitoria de código**: mientras un endpoint todavía tenga `[Authorize(Roles = Roles.X)]` sin migrar a policy, ese endpoint sigue leyendo el claim (nada que ver con estados de migración de datos, es simplemente que el atributo viejo todavía no fue reemplazado en ese controller puntual). Una vez reemplazado el último `[Authorize(Roles=...)]`, el claim deja de tener cualquier función y se retira (Etapa 6).
- **No existe ninguna rama "usuario migrado / no migrado"**: como no hay datos previos, cada `Usuario` que existe en Firestore ya tiene su modelo completo desde el momento en que se creó (§2).

Se conserva de v2 la administración de roles/acciones (crear/editar/activar Rol, asignar/quitar Accion de un Rol, asignar/quitar Rol a un Usuario, consultar permisos efectivos, desactivar Usuario, guard de "no perder el último Administrador" evaluado transaccionalmente) — pasa a ser parte de esta misma etapa (Etapa 5, ver §6) en vez de una etapa aparte, para mantener el roadmap corto.

---

## 4. Adaptación directa del dominio

**Reemplaza** la sección de v2 de "remapeo de claves foráneas" (que asumía datos existentes). Como no hay datos que preservar, esto deja de ser una migración y pasa a ser un cambio de código directo:

- `Event.OrganizadorId` → `Event.OrganizadorPersonaId`: se renombra el campo y se cambia qué valor se le escribe (`PersonaId` en vez de UID de Firebase) directamente en `EventService`. Sin campo aditivo ni convivencia de dos nombres.
- `Ticket.ClienteId` → `Ticket.ClientePersonaId`, `Ticket.ValidadoPor` → `Ticket.ValidadoPorPersonaId`: mismo criterio, en `TicketService`.
- `Control.EventId`/`Control.OrganizadorId` (escalares, un Control = un evento) → colección top-level `control_asignaciones/{PersonaId}_{EventId}` (ID determinístico, ver v2 §9 sin cambios), habilitando de entrada que un Control tenga múltiples eventos asignados.
- Los checks de ownership (organizador dueño del evento, control asignado al evento, cliente dueño del ticket) se actualizan para comparar `PersonaId` en vez de UID — el patrón en sí ("releer de Firestore, comparar contra el actor del token, nunca confiar en el body") no cambia.
- `ValidacionAcceso` (auditoría de intentos de validación) queda fuera de esta versión del plan — no está en la lista de qué conservar ni de qué eliminar; se puede proponer como adición posterior si hace falta, sin bloquear nada de lo anterior.

---

## 5. Bootstrap del primer Administrador (decisión cerrada)

Comando explícito dentro de `HoyDonde.API` — no un endpoint HTTP, no un proyecto de consola aparte — por ejemplo `dotnet run --project HoyDonde.API -- bootstrap-admin <email>`. Reglas:

- **Deshabilitado por defecto**: solo corre si una clave de configuración explícita lo habilita (p. ej. `Bootstrap:AllowAdminBootstrap = true`); si esa clave no está presente o es `false`, se niega a ejecutar.
- **Se niega si ya existe un Administrador efectivo**: antes de crear nada, verifica que no haya ya un `Usuario` activo con una asignación activa de `UsuarioRol(ADMINISTRADOR)` sobre un `Rol` también activo; si existe, aborta sin tocar nada.
- **Contraseña nunca por argumento de línea de comandos ni en configuración versionada**: se lee de una variable de entorno (p. ej. `HOYDONDE_BOOTSTRAP_ADMIN_PASSWORD`) o se pide por entrada oculta si se corre interactivamente. El comando solo recibe el email como argumento.
- **Reutiliza el aprovisionamiento normal**: internamente llama exactamente el mismo camino de §2.2 (crear identidad vía `IIdentityProvider` + transacción atómica con rol `ADMINISTRADOR`), sin un camino de creación paralelo.

Documentado en el README/CLAUDE.md, nunca expuesto por HTTP.

---

## 6. Roadmap

### Etapa 0 — Abstracción de proveedor de identidad — **implementada** (commit `4ff268b`)

### Etapa 1 — Catálogo Rol/Accion + Firestore Emulator — **implementada** (commit `28ebc8d`)

### Etapa 2 — Fundación del modelo — **implementada y verificada**
- **Objetivo:** modelos y repositorios de `Persona`/`Usuario`/`UsuarioRol`/`IdentidadExterna` + `IPermissionService`/`PermissionService`, sin ningún flujo de registro todavía conectado a ellos. **No incluye** `UserService`/`IUserService`/`UserController`/`AuthController`/`IIdentityProvider`/`FirebaseIdentityProvider`/`IdentityEmailAlreadyExistsException`/`IdentidadHuerfana` — eso es Etapa 3 (ver §7 para qué pasa con los cambios sin commit que ya tocaban esos archivos).
- **Componentes:** `Models/Persona.cs`, `Usuario.cs`, `UsuarioRol.cs`, `IdentidadExterna.cs`; `Repositories/IUsuarioRepository.cs`/`FirestoreUsuarioRepository.cs` (transacción Persona+Usuario+UsuarioRol+IdentidadExterna; `PersonaId`/`UsuarioId` se generan **antes** de `RunTransactionAsync` y viajan como parte de `UsuarioProvisioningRequest`, no se generan dentro de la transacción); `Services/IPermissionService.cs`/`PermissionService.cs`; los registros de DI correspondientes en `Program.cs`.
- **Persistencia:** `personas`, `usuarios`, `usuarios/{id}/roles`, `identidades_externas`.
- **Pruebas:** unitarias de repos (mock) + integración contra emulador: creación, lectura, idempotencia por reintento con el mismo `ExternalSubjectId`, y atomicidad todo-o-nada — el test genera `PersonaId`/`UsuarioId` determinísticos, precarga una colisión en `personas/{PersonaId}` o `usuarios/{UsuarioId}`, y verifica que la transacción falla sin dejar escrito ni `UsuarioRol` ni `IdentidadExterna` ni ningún otro documento.
- **Criterio de finalización:** el modelo se puede crear/leer de punta a punta en el emulador; nada lo llama todavía desde un endpoint real. **Cumplido y verificado**: suite completa ejecutada contra Firestore Emulator real (no mockeado) con resultado **61 passed, 0 failed, 0 skipped** — incluye las pruebas de integración de la Etapa 1 y de la Etapa 2 que antes quedaban `Skip` sin emulador disponible.
- **Rollback:** borrar las colecciones nuevas; cero impacto (nada las usa aún).

> **Nota de ejecución reproducible.** Entorno verificado: Temurin Java 17.0.20 + Firebase CLI 13.35.1 (vía `npx`, sin instalación global ni upgrade de Java). Comando:
>
> ```bash
> npx --yes firebase-tools@13.35.1 emulators:exec --only firestore --project hoydonde-security-refactor-tests "dotnet test HoyDonde.sln"
> ```
>
> El Firebase CLI levanta el Firestore Emulator, exporta `FIRESTORE_EMULATOR_HOST` al proceso hijo y lo apaga al terminar. No requiere `firebase login` ni credenciales reales — el proyecto `hoydonde-security-refactor-tests` es un ID arbitrario que nunca se resuelve contra un proyecto de Firebase real.

### Etapa 3 — Aprovisionamiento — **implementada y verificada**
- **Objetivo:** implementar los dos flujos de §2 (Cliente vía `/api/auth/sync`, Admin/Organizador/Control por `IIdentityProvider`), incluida la compensación simplificada (§2.2) y el bootstrap (§5). "End-to-end" acá significa que el **aprovisionamiento en sí** queda completo (identidad + modelo Firestore) — no que los endpoints de dominio (Event/Ticket) ya funcionen sobre el modelo nuevo; eso es la Etapa 4.
- **Componentes:** `Services/UserService.cs`/`IUserService.cs` (Admin/Organizador/Control); `Controllers/UserController.cs` (Admin/Organizador/Control) y `Controllers/AuthController.cs` (Cliente, `/api/auth/sync` reescrito según §2.1); `Services/IIdentityProvider.cs`/`FirebaseIdentityProvider.cs` (sin `ResolveByEmailAsync`); `Exceptions/IdentityEmailAlreadyExistsException.cs`; `Models/IdentidadHuerfana.cs` + `Repositories/IIdentidadHuerfanaRepository.cs`/`FirestoreIdentidadHuerfanaRepository.cs`; `Commands/BootstrapAdminCommand.cs` (bootstrap del primer Admin, §5); `Repositories/IUsuarioRepository.cs`/`FirestoreUsuarioRepository.cs` ampliado con `GetUsuarioIdsConRolActivoAsync` para la verificación de "Administrador efectivo" del bootstrap.
- **Persistencia:** la de la Etapa 2, ahora sí escrita por los flujos reales, más `identidades_huerfanas`. `users`/`user_audits` legacy dejan de recibir escrituras nuevas a partir de esta etapa (siguen existiendo en el código legacy hasta la Etapa 6, pero ya no se les escribe desde ningún flujo nuevo).
- **Pruebas:** unitarias de `UserService` (happy path; falla después de crear identidad incluyendo `SetTemporaryClaimAsync` → compensa; falla la compensación → `identidades_huerfanas` con ambos errores; falla también el registro de la huérfana → logging estructurado con los tres errores; email ya existe → conflicto sin crear ni compensar nada); unitarias de `AuthService` (Cliente nuevo, Cliente existente sin cambio de rol, fallo de aprovisionamiento nunca compensa); unitarias de `BootstrapAdminCommand` (deshabilitado, sin email, exitoso con marcador `BOOTSTRAP`, segundo Administrador rechazado); controller tests de `AuthController`/`UserController` (401 sin token, uid/email del token y no del body, 409 por email duplicado); integración contra emulador del flujo Cliente (`/api/auth/sync` dos veces no duplica ni cambia roles, Usuario con otro rol nunca suma CLIENTE) y del bootstrap (segundo Administrador rechazado contra la collection group query real).
- **Criterio de finalización:** los 4 flujos (Admin, Organizador, Cliente, Control) provisionan correctamente contra el emulador; cero referencias a `ResolveByEmailAsync` en el código. **Cumplido y verificado**: suite completa ejecutada contra Firestore Emulator real (Firebase CLI 13.35.1 vía `npx`, mismo entorno reproducible que la Etapa 2) con resultado **84 passed, 0 failed, 0 skipped**.
- **Desviación registrada durante la verificación:** la collection group query de `GetUsuarioIdsConRolActivoAsync` sobre la subcolección `usuarios/*/roles` colisionaba con la colección raíz `roles` (mismo Id de colección, Etapa 1): un `Rol` de catálogo con `Codigo == rolCodigo` y `Activo == true` calzaba con el filtro y producía `NullReferenceException` (o falsos positivos de "ya existe Administrador") al no tener un `Usuario` padre real. Corregido filtrando explícitamente que el documento resultante cuelgue de la colección `usuarios`; cubierto con test de regresión contra el emulador.
- **Rollback:** revertir `UserService`/`UserController`/`AuthController`/`IIdentityProvider` a la versión de la Etapa 2; el modelo de la Etapa 2 queda vacío pero intacto.

### Etapa 4 — Adaptación directa de Event/Ticket/ControlAsignacion a PersonaId — **implementada y verificada**
- **Objetivo:** los cambios de §4 (renombrar `OrganizadorId`/`ClienteId`/`ValidadoPor` a sus versiones `*PersonaId`, `control_asignaciones` reemplazando el escalar de `Control`), más la resolución centralizada UID → PersonaId que el resto del dominio necesita para escribir/comparar esos campos.
- **Componentes:**
  - `Services/IAuthenticatedPersonaResolver.cs`/`AuthenticatedPersonaResolver.cs`: único punto reutilizado por `EventService`, `TicketService` y `UserService.RegisterControlAsync` para resolver `identidades_externas/FIREBASE#{uid}` → `Usuario` → `Usuario.PersonaId`; lanza `Exceptions/IdentityNotProvisionedException.cs` cuando el token es válido pero no existe esa identidad en el modelo nuevo.
  - `Models/Event.cs` (`OrganizadorPersonaId`), `Ticket.cs` (`ClientePersonaId`, `ValidadoPorPersonaId`), `Control.cs` (sin los escalares legacy `EventId`/`OrganizadorId`); nuevo `Models/ControlAsignacion.cs` + `Repositories/IControlAsignacionRepository.cs`/`FirestoreControlAsignacionRepository.cs`, colección `control_asignaciones/{ControlPersonaId}_{EventId}` (ID determinístico), con `AsignarAsync` implementado dentro de una transacción Firestore para ser idempotente incluso bajo llamadas concurrentes para el mismo par.
  - `EventService`, `TicketService`, `UserService.RegisterControlAsync` actualizados para resolver el actor a `PersonaId` antes de leer/escribir Firestore, y para autorizar la validación de tickets contra `control_asignaciones` en vez del escalar legacy de `Control`.
  - `Middleware/ExceptionMiddleware.cs`: maneja `IdentityNotProvisionedException` de forma centralizada — 403 con mensaje público genérico fijo y log interno con el UID (`ExternalSubjectId`, propiedad separada del mensaje público de la excepción); los controllers que ya tenían un `catch (Exception)` genérico dejan propagar esta excepción puntualmente (`throw;`) en vez de convertirla en 400 o reenviar información interna en el body.
  - `Converters/DecimalFirestoreConverter.cs` aplicado a `TicketType.Precio` — ver "Desviaciones" abajo.
- **Persistencia:** `events.OrganizadorPersonaId`, `tickets.ClientePersonaId`, `tickets.ValidadoPorPersonaId` (campos renombrados, sin datos previos que convertir); nueva colección top-level `control_asignaciones/{ControlPersonaId}_{EventId}`.
- **Pruebas:** unitarias de `AuthenticatedPersonaResolver` (resuelve correctamente, y rechaza explícitamente con `IdentityNotProvisionedException` cuando no hay `IdentidadExterna` o el `Usuario` no existe, sin filtrar el UID en el mensaje público); unitarias de `TicketService`/`UserService` adaptadas a PersonaId y `control_asignaciones` (Control no asignado → 403, Control asignado a 2+ eventos, segundo uso del mismo ticket rechazado, alta de Control para evento ajeno/inexistente rechazada antes de tocar el proveedor de identidad); controller tests (`EventsController`/`TicketsController`/`UserController`) que verifican 403 genérico sin el UID en el body, incluido el camino sin try/catch propio del controller (`GetMyEvents`, resuelto directo por el middleware); integración contra Firestore Emulator real de `EventService` (persiste `OrganizadorPersonaId`, no UID; ownership ajeno → 403; filtro de eventos propios), `TicketService` (compra persiste `ClientePersonaId`; `/tickets/me` no devuelve tickets ajenos; validación persiste `ValidadoPorPersonaId`) y `FirestoreControlAsignacionRepository` (idempotencia secuencial y **concurrente**: N llamadas simultáneas para el mismo par no fallan y el documento no se modifica después de creado; misma `ControlPersonaId` con 2+ asignaciones).
- **Criterio de finalización:** ningún código de producción lee/escribe `OrganizadorId`/`ClienteId`/`ValidadoPor` ni el escalar `Control.EventId`/`Control.OrganizadorId` (verificado con búsqueda dirigida sobre el código de producción, distinguiendo de comentarios/documentación histórica). **Cumplido y verificado**: suite completa ejecutada contra Firestore Emulator real (Firebase CLI 13.35.1 vía `npx`, mismo entorno reproducible que las Etapas 2/3) con resultado **106 passed, 0 failed, 0 skipped**.
- **Desviaciones registradas durante la verificación:**
  - `TicketType.Precio` (`decimal`) no tiene converter nativo en el SDK de Google.Cloud.Firestore para .NET (`System.Decimal` no soportado): cualquier lectura/escritura real de un `Event` con `TicketTypes` fallaba — un bug preexistente ajeno a esta etapa, nunca antes ejercitado contra Firestore real porque los tests de controller mockean `IEventService`/`ITicketService`. Se agregó `Converters/DecimalFirestoreConverter.cs` (persiste como `double`, reconvierte a `decimal` al leer), acotado solo a esa propiedad, para poder verificar la persistencia de `ClientePersonaId` en una compra real.
  - Ventana conocida entre aprovisionar la identidad del Control (Persona+Usuario+UsuarioRol+IdentidadExterna) y crear su `ControlAsignacion`: si el segundo paso falla, queda una identidad Control huérfana sin asignación. No se agregó compensación distribuida para esta ventana — aceptado así para este MVP sin datos reales, señalado explícitamente en vez de ampliar el diseño sin consulta.
- **Rollback:** revertir los archivos puntuales de Event/Ticket/Control/ControlAsignacion y los servicios/controllers que los consumen; sin datos que perder.

### Etapa 5 — AuthorizationHandler, policies y administración de roles/acciones — **implementada y verificada**
- **Objetivo:** `IAuthorizationHandler` que resuelve `IPermissionService` y reemplaza `[Authorize(Roles=...)]` por `[Authorize(Policy="ACCION_CODIGO")]`, endpoint por endpoint — ya sobre los campos `*PersonaId` que dejó la Etapa 4; endpoints de administración (`ROL_CREAR`, `ROL_EDITAR`, `ROL_ACTIVAR`, `ROL_ASIGNAR_ACCION`, `ROL_QUITAR_ACCION`, `USUARIO_ASIGNAR_ROL`, `USUARIO_QUITAR_ROL`, `USUARIO_VER_PERMISOS_EFECTIVOS`, `USUARIO_DESACTIVAR`), con el guard transaccional de "no dejar cero Administradores efectivos".
- **Componentes:**
  - `Authorization/AccionRequirement.cs`, `Authorization/AccionAuthorizationHandler.cs` y `Authorization/Acciones.cs`: un `AccionRequirement` por código de Accion, registrado como policy en `Program.cs` para las 20 acciones del catálogo (`Acciones.Todas`); el handler resuelve cada decisión exclusivamente contra `IPermissionService` (`Usuario` → `UsuarioRol` → `Rol` → `RolAccion` → `Accion`, todo activo) y **nunca** consulta el claim legacy `role` — el UID sale solo de las claims del principal ya autenticado. Todos los endpoints de dominio (Event/Ticket/User) quedaron migrados de `[Authorize(Roles=...)]` a `[Authorize(Policy=...)]`; los checks de ownership por `PersonaId` (organizador dueño del evento, control asignado, cliente dueño del ticket) se siguen aplicando además de la policy, sin que una acción por sí sola habilite acceso a un recurso ajeno.
  - `Controllers/SecurityAdminController.cs` + `Services/ISecurityAdminService.cs`/`SecurityAdminService.cs`: administración bajo `/api/security` — crear/editar/activar rol, listar roles, listar el catálogo de Acciones (`GET /api/security/acciones`, protegido con `ROL_ASIGNAR_ACCION`, sin agregar una 21ª acción), asignar/quitar Accion de un Rol, listar usuarios (`GET /api/security/usuarios`, protegido con `USUARIO_VER_PERMISOS_EFECTIVOS`, expone únicamente `UsuarioId`/`PersonaId`/email/estado/roles activos — nunca UID externo, tokens ni contraseñas), asignar/quitar Rol de un Usuario, consultar permisos efectivos, activar/desactivar Usuario.
  - DTOs (`CreateRolRequestDto`, `UpdateRolRequestDto`, `RolResponseDto`, `AccionResponseDto`, `UsuarioResumenResponseDto`, `PermisosEfectivosResponseDto`) y excepciones (`RolYaExisteException`, `RolNoEncontradoException`, `AccionNoEncontradaException`, `UsuarioNoEncontradoException`, `UltimoAdministradorException`).
  - `Repositories/UltimoAdministradorGuard.cs`: guard transaccional de "Administrador efectivo" (`Usuario.IsActive` + `UsuarioRol(ADMINISTRADOR).Activo` + `Rol(ADMINISTRADOR).Activo`), invocado dentro de la misma transacción Firestore ya abierta por `FirestoreRolRepository.SetActivoAsync` y `FirestoreUsuarioRepository.SetActivoAsync`/`QuitarRolAsync`; soporta conteo concurrente correcto (dos desactivaciones simultáneas sobre los dos últimos Administradores dejan exactamente uno efectivo).
  - `Repositories/SecurityAuditWriter.cs` + `Models/SecurityAudit.cs`: cada mutación real de `FirestoreRolRepository`/`FirestoreUsuarioRepository` escribe su `SecurityAudit` (colección `security_audits`) dentro de la **misma** transacción que la mutación — si la transacción se descarta (por ejemplo, por el guard del último Administrador), la auditoría nunca queda escrita.
  - Idempotencia real en `FirestoreRolRepository`/`FirestoreUsuarioRepository`: asignar una Accion ya asignada a un Rol, quitar una Accion no asignada, asignar un Rol ya activo a un Usuario, quitar un Rol inexistente/ya inactivo, activar/desactivar un Rol o Usuario ya en ese estado, y editar un Rol con los mismos valores son todos no-ops reales dentro de la transacción — no tocan metadata (`AssignedAt`/`AssignedBy`/`UpdatedAt`) ni generan `SecurityAudit`. `QuitarAccionAsync` distingue explícitamente "Accion inexistente en el catálogo" (404, `AccionNoEncontradaException`) de "Accion existente pero no asignada a este Rol" (no-op).
- **Persistencia:** ninguna nueva más allá de auditoría de cambios (`security_audits`, mismo patrón que `user_audits`).
- **Pruebas:** HTTP por endpoint (con permiso → 200, sin permiso → 403); administración (crear/editar/activar rol, listar roles/acciones/usuarios, asignar/quitar acción, asignar/quitar rol, permisos efectivos, intento de dejar cero administradores → rechazado, concurrencia de dos desactivaciones simultáneas); `AccionAuthorizationHandlerTests` de punta a punta contra una `PermissionService` real (repos mockeados) probando que un claim legacy `role` distinto o ausente nunca cambia la decisión; integración contra Firestore Emulator real de los repos administrativos y del guard del último Administrador, incluidos los no-ops (documento y metadata sin cambios, conteo de `security_audits` sin incrementar) y `GET /api/security/usuarios`/`GET /api/security/acciones` (`GetAllAsync`).
- **Criterio de finalización:** todos los endpoints migrados a `Policy`; cero usos de `[Authorize(Roles=...)]` fuera de los que ya se planea borrar en la Etapa 6 junto con el resto del modelo legacy; exactamente 20 acciones/policies (ninguna acción adicional para los endpoints de lectura administrativa). **Cumplido y verificado**: suite completa ejecutada contra Firestore Emulator real (Firebase CLI 13.35.1 vía `npx`, mismo entorno reproducible que las etapas anteriores) con resultado **208 passed, 0 failed, 0 skipped**.
- **Riesgo aceptado para este MVP:** `GetAllAsync` de `IUsuarioRepository` (detrás de `GET /api/security/usuarios`) hace una lectura N+1 (roles activos por Usuario) y no tiene paginación. Se acepta así porque todavía no hay datos reales ni volumen medido que lo justifique; paginación/optimización queda como endurecimiento futuro (§10), sin agregar caché preventivo ahora.
- **Rollback:** por endpoint, revertir el atributo puntual; para la administración, revertir `SecurityAdminController`/`SecurityAdminService`/DTOs/excepciones y los métodos administrativos agregados a `FirestoreRolRepository`/`FirestoreUsuarioRepository`/`FirestoreAccionRepository`; sin datos que perder.

### Etapa 6 — Retiro del modelo legacy y de los claims temporales
- **Objetivo:** eliminar `ApplicationUser`/subclases, `Roles.cs`, `[Authorize(Roles=...)]` restante, el claim `role` (incluido el que la Etapa 3 asignaba temporalmente en cada sync de Cliente, §2.1 punto 7), `IsAdminCreatedAsync`, `IJwtService` (código muerto ya identificado desde el diagnóstico original).
- **Componentes:** borrado de `Models/Admin.cs`, `Cliente.cs`, `Organizador.cs`, `Control.cs` (reemplazados por `Persona`+roles+`ControlAsignacion`), `Roles.cs`; simplificación del `AuthorizationHandler` a una sola vía.
- **Persistencia:** `users`/`user_audits` se dejan de leer por completo (no hace falta ni conservarlas ni borrarlas — no hay dato de valor ahí; se puede borrar la colección directamente dado que no hay nada que preservar).
- **Pruebas:** regresión completa.
- **Criterio de finalización:** cero referencias a `Roles.cs`/subclases de `ApplicationUser` en el código.
- **Rollback:** el más costoso de la serie, pero de bajo riesgo real porque no hay datos de producción en juego.

---

## 7. Clasificación de los archivos de la Etapa 2 — alcance corregido antes del commit

La Etapa 2 real es solo fundación de modelo (§6). Los cambios que en ese momento tocaban aprovisionamiento/identidad sin estar todavía commiteados eran en realidad Etapa 3: se restauraron a su estado de HEAD (o se quitaron, si el archivo era nuevo) para no mezclar dos etapas en el mismo commit, y quedan pendientes de volver a implementarse en su propia sesión de código ya con las decisiones cerradas acá (§2.1, §5).

### Quedaron en el commit de la Etapa 2

| Archivo | Estado |
|---|---|
| `HoyDonde.API/Models/Persona.cs` | Conservar |
| `HoyDonde.API/Models/Usuario.cs` | Conservar |
| `HoyDonde.API/Models/UsuarioRol.cs` | Conservar |
| `HoyDonde.API/Models/IdentidadExterna.cs` | Conservar |
| `HoyDonde.API/Models/MigracionUsuario.cs` | Eliminar |
| `HoyDonde.API/Models/MigracionEstado.cs` | Eliminar |
| `HoyDonde.API/Repositories/IUsuarioRepository.cs` | Simplificar — quitar `LegacyUser`; `UsuarioProvisioningRequest` pasa a recibir `PersonaId`/`UsuarioId` ya generados por el llamador (ver Etapa 2 en §6) |
| `HoyDonde.API/Repositories/FirestoreUsuarioRepository.cs` | Simplificar — quitar escritura a `users`/`user_audits`/`migracion_usuarios`; usar los IDs recibidos en vez de generarlos dentro de la transacción; queda Persona+Usuario+UsuarioRol+IdentidadExterna |
| `HoyDonde.API/Services/IPermissionService.cs` | Conservar |
| `HoyDonde.API/Services/PermissionService.cs` | Conservar |
| `HoyDonde.API.Tests/PermissionServiceTests.cs` | Conservar |
| `HoyDonde.API.Tests/Integration/FirestoreUsuarioRepositoryTests.cs` | Simplificar — quitar `LegacyUser`; reescribir el test de atomicidad con los IDs determinísticos precargados en `personas/{PersonaId}` o `usuarios/{UsuarioId}` (§6, Etapa 2) |
| `HoyDonde.API.Tests/Integration/PermissionServiceEmulatorTests.cs` | Simplificar — quitar `LegacyUser` de la request de prueba |
| `HoyDonde.API/Program.cs` | Revertir parcialmente — conservar el registro de `IUsuarioRepository`/`FirestoreUsuarioRepository`; quitar el de `IIdentidadHuerfanaRepository`/`FirestoreIdentidadHuerfanaRepository` (es de la Etapa 3) |
| `HoyDonde.API.Tests/TestApplicationFactory.cs` | Revertir parcialmente — conservar el mock de `IUsuarioRepository`; quitar el de `IIdentidadHuerfanaRepository` (Etapa 3) |

### Se restauraron a HEAD / se quitaron (son Etapa 3, no Etapa 2)

| Archivo | Acción tomada |
|---|---|
| `HoyDonde.API/Services/UserService.cs` | Revertir a HEAD (estado de la Etapa 0, commit `4ff268b`) |
| `HoyDonde.API/Services/IUserService.cs` | Revertir a HEAD |
| `HoyDonde.API/Controllers/UserController.cs` | Revertir a HEAD (no lo tocó la Etapa 0; vuelve a su versión original pre-refactor) |
| `HoyDonde.API/Services/IIdentityProvider.cs` | Revertir a HEAD (Etapa 0) — se vuelve a extender en la Etapa 3, esta vez sin `ResolveByEmailAsync` desde el principio |
| `HoyDonde.API/Services/FirebaseIdentityProvider.cs` | Revertir a HEAD (Etapa 0) |
| `HoyDonde.API/Exceptions/IdentityEmailAlreadyExistsException.cs` | Quitar (archivo nuevo, no existe en HEAD) — se vuelve a crear en la Etapa 3 |
| `HoyDonde.API/Models/IdentidadHuerfana.cs` | Quitar — se vuelve a crear en la Etapa 3 |
| `HoyDonde.API/Repositories/IIdentidadHuerfanaRepository.cs` | Quitar |
| `HoyDonde.API/Repositories/FirestoreIdentidadHuerfanaRepository.cs` | Quitar |
| `HoyDonde.API.Tests/UserServiceProvisioningTests.cs` | Quitar (archivo nuevo) — se vuelve a escribir en la Etapa 3, ya sin los tests de recuperación por email (§2.2 los elimina) |
| `HoyDonde.API.Tests/UserServiceControlOwnershipTests.cs` | Revertir a HEAD (Etapa 0) |
| `HoyDonde.API.Tests/UserControllerTests.cs` | Revertir a HEAD (Etapa 0) |

Sin cambios (ya commiteados en la Etapa 1, no forman parte de este diff): `Models/Rol.cs`, `Accion.cs`, `RolAccionAsignacion.cs`, `Repositories/IRolRepository.cs`/`FirestoreRolRepository.cs`/`IAccionRepository.cs`/`FirestoreAccionRepository.cs`, `Services/SecurityCatalogSeeder.cs`, y sus tests.

---

## 8. Fuera de alcance de este documento

- **Frontend**: no se toca hasta terminar la API (Etapa 6 inclusive). El contrato del flujo Cliente (§2.1) ya queda diseñado para cuando corresponda implementarlo.
- **`ValidacionAcceso`** (auditoría de intentos de validación de ticket): no está en el alcance actual: se puede proponer como adición posterior sin bloquear nada de este roadmap.

---

## 9. Decisiones cerradas en esta revisión

1. **Flujo Cliente:** reutiliza `POST /api/auth/sync` — ver §2.1.
2. **Bootstrap del primer Administrador:** comando dentro de `HoyDonde.API`, deshabilitado por defecto, se niega si ya hay un Administrador efectivo, contraseña nunca por CLI ni config versionada — ver §5.
3. **Test de atomicidad todo-o-nada:** `PersonaId`/`UsuarioId` determinísticos generados antes de la transacción, colisión precargada en `personas/{PersonaId}` o `usuarios/{UsuarioId}` — ver §6, Etapa 2.

No queda ninguna decisión abierta en esta versión.

---

## 10. Endurecimiento futuro para producción (fuera de este roadmap)

Todo lo siguiente solo tiene sentido el día que exista un entorno con datos reales y una migración concreta que ejecutar — no antes, y no como trabajo preventivo de las etapas de §6:

- Backups/snapshots antes de cualquier migración de datos real.
- Modo dry-run en cualquier herramienta de migración/backfill.
- Migraciones idempotentes con reanudación tras interrupción.
- Backfill de datos existentes (si en el futuro hay usuarios/eventos/tickets reales que traer desde otro sistema).
- Doble escritura temporal como estrategia de convivencia entre modelos, si hiciera falta desplegar sin downtime contra datos reales.
- Reconciliación automática de inconsistencias detectadas post-migración.
- Plan de rollback de datos (no solo de código) para cada paso de una migración real.

No se diseña nada de esto en detalle ahora: hacerlo sin un caso real concreto es exactamente la complejidad que esta revisión (v3) eliminó a propósito.
