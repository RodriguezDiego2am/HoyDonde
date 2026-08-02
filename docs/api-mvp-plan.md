# Plan del MVP funcional de la API HoyDonde? (post-seguridad)

Este documento es el roadmap de implementación posterior al refactor de seguridad (`docs/security-refactor-plan.md`, Etapas 0–6, **cerrado**). Su fuente es la auditoría funcional completa realizada sobre HEAD `b96f86c` (Event, Ticket, Control, contratos HTTP, tests) y las decisiones de producto cerradas por el dueño del producto el 2026-08-01, incluida la revisión del 2026-08-01 sobre duración del evento (`FechaInicio`/`FechaFin`), máquina de estados, edición de tipos de ticket, contratos de rutas y contrato de ticket.

Para distinguirlas de las **Etapas** del refactor de seguridad, las etapas de este plan se llaman **API-MVP N**. No reabren ni modifican el módulo de seguridad: reutilizan sus policies, su catálogo de 20 acciones y su mecanismo de autorización tal como están.

Este documento nació como documento de planificación: no se implementó código ni se ejecutó la suite de tests al crearlo, y no se modificaron `CLAUDE.md` ni `API_Documentation.md` en esa revisión inicial. La revisión del 2026-08-02 **cerró API-MVP 1**: código implementado y verificado (ver §2, "Estado: implementada y verificada"); `CLAUDE.md` se actualizó con el estado funcional real de `Event`. Esa misma revisión **cerró además API-MVP 2**: compra, validación y consulta de tickets implementadas y verificadas (ver §3, "Estado: implementada y verificada"); `CLAUDE.md` se actualizó con el estado funcional real del flujo de `Ticket`. Esta revisión (2026-08-02) **cierra además API-MVP 3**: asignación de un Control existente a otro evento propio implementada y verificada (ver §4, "Estado: implementada y verificada"); `CLAUDE.md` se actualizó con el contrato y las reglas reales de esta asignación. Esa misma revisión **cierra además API-MVP 4** (ver §5, "Estado: implementada y verificada"): contrato uniforme de error, `ExceptionMiddleware` como único mapeador, recorrido HTTP integral contra el Firestore Emulator, y `API_Documentation.md` reescrito por completo. **Con este cierre, API-MVP 1–4 quedan implementadas y verificadas, y el backend funcional del MVP queda cerrado** (suite completa: 347 passed, 0 failed, 0 skipped); Frontend 1 (§6), Frontend 2 (§7) y la preparación de Entrega (§8) siguen pendientes.

---

## 0. Decisiones de producto que gobiernan este plan

Estas decisiones ya están cerradas y no se vuelven a discutir como pendientes; se documentan aquí como restricciones de diseño.

1. **Cancelación y tickets**: el estado del evento es la fuente de verdad. No hay actualización masiva de tickets al cancelar. Compra y validación leen el evento dentro de su propia operación transaccional y rechazan según las reglas de vigencia de §0.1. Un ticket de un evento cancelado nunca se valida. Cancelar bloquea inmediatamente compra y validación (es consecuencia directa de que ambas exigen `Estado == Publicado`). El contrato de lectura debe poder señalar que un ticket ya no es utilizable aunque su documento histórico conserve el `Estado` original (`Emitido`).
2. **Duración del evento y finalización** *(cerrado en esta revisión)*: el modelo distingue `FechaInicio` y `FechaFin`, ambas en UTC. `Finalizado` es un estado **efectivo, derivado y terminal desde el punto de vista funcional**, calculado como `Estado == Publicado && UtcNow > FechaFin`. No se persiste, no requiere jobs ni schedulers ni infraestructura nueva. Ver reglas exactas de vigencia en §0.1.
3. **Estados**: se elimina la ambigüedad `Activo`/`Pendiente`. Único estado inicial: `Borrador`. Los únicos estados **persistidos** son `Borrador`, `Publicado` y `Cancelado`. Transiciones válidas cerradas en la tabla de §1. No hay aprobación administrativa de eventos en este MVP.
4. **Control**: se incluye la asignación de un Control existente a otro evento del mismo organizador, reutilizando la acción `CONTROL_CREAR` ya existente. No se agrega una acción 21 ni se reabre el diseño de seguridad. Ruta cerrada en §4.
5. **Consulta de eventos**: `GET /api/events/{id}` es estrictamente público y solo devuelve eventos `Publicado` y no finalizados (ver vigencia de catálogo en §0.1); el resto responde 404. `GET /api/events/organizer/{id}` es una ruta autenticada separada, protegida con `EVENTO_VER_PROPIOS`, para que el organizador consulte el detalle de un evento propio en cualquier estado. Todos los endpoints HTTP de eventos devuelven DTOs, nunca el modelo `Event`.
6. **Edición**: un evento solo puede editarse mientras está en `Borrador`, y esa edición incluye sus tipos de ticket (ver reglas exactas en §2). Publicado, es inmutable; puede cancelarse, no despublicarse.
7. **Tickets**: al comprar se guarda una fotografía inmutable (`EventoNombre`, `TicketTypeNombre`, `PrecioPagado`). `TicketResponseDto` incluye esos campos, `FechaInicio`/`FechaFin` del evento, el estado histórico del ticket, `Utilizable` y el motivo público cuando no es utilizable (ver contrato exacto en §3). Entradas gratuitas permitidas (`Precio >= 0`). `CantidadDisponible >= 1`, nombres no vacíos, al menos un tipo de ticket por evento (exigido para poder publicar).
8. **Errores**: excepciones de negocio tipadas, introducidas en la etapa donde aparece cada regla (no una etapa aparte de "arreglar todo junto"). Eliminación progresiva de `catch (Exception)` con `ex.Message`. Lo no anticipado sube a `ExceptionMiddleware`. Sin fuga de detalles internos en producción.
9. **Verificación**: antes de iniciar el frontend debe existir (a) un test con Firestore Emulator que encadene Organizador crea → publica → Cliente compra → Control valida → segundo intento rechazado, y (b) un test concurrente que demuestre ausencia de sobreventa.

### 0.1 Duración del evento: `FechaInicio`/`FechaFin` y vigencia (cerrado)

El modelo distingue inicio y fin del evento, ambos en UTC. No hay una única "vigencia" global: cada operación tiene su propio límite temporal, y las tres se cierran explícitamente:

| Operación | Condición exacta | Motivo |
|---|---|---|
| **Catálogo público** (`SearchEventsAsync`) y **detalle público** (`GET /api/events/{id}`) | `Estado == Publicado && UtcNow <= FechaFin` | Un evento en curso (entre `FechaInicio` y `FechaFin`) sigue siendo visible; deja de estarlo recién al pasar `FechaFin` (momento en que pasa a `Finalizado`). |
| **Compra** (`BuyTicketsAsync`) | `Estado == Publicado && UtcNow < FechaInicio` | No se puede comprar una vez que el evento ya empezó, aunque siga visible y validando entradas. |
| **Validación** (`ValidateTicketAsync`/`TryConsumeAsync`) | `Estado == Publicado && UtcNow <= FechaFin` | Un Control autorizado puede validar desde que el evento está publicado (incluso antes de `FechaInicio`, sin ventana de "apertura de puertas") hasta `FechaFin` inclusive. No hay una ventana arbitraria adicional: la única condición es que el evento siga `Publicado` y no haya pasado `FechaFin`. |

Validaciones de datos al crear/editar: `FechaInicio` debe ser futura; `FechaFin` debe ser posterior a `FechaInicio`.

`Finalizado` (`Estado == Publicado && UtcNow > FechaFin`) es terminal desde el punto de vista funcional: no admite compra, no admite validación, no admite cancelación, no aparece en catálogo ni en el detalle público. Sigue siendo consultable por el organizador vía `GET /api/events/organizer/{id}` (ruta autenticada, sin filtro de vigencia), pero cualquier intento de transición sobre él —incluida `Cancelado`— se rechaza con excepción tipada y HTTP 409, según la tabla de §1.

---

## 1. Máquina de estados de `Event` (cerrada)

Estados **persistidos** en `EventStatus` (Firestore): **`Borrador`**, **`Publicado`**, **`Cancelado`**. `Activo`, `Pendiente` y `Finalizado` no existen como valores persistidos; `Finalizado` es exclusivamente un estado efectivo derivado (§0.1), expuesto en las respuestas de lectura pero nunca escrito en Firestore.

Tabla de transiciones cerrada:

| Transición | Validez | Condición |
|---|---|---|
| `Borrador → Publicado` | Válida | El evento tiene datos válidos y al menos un tipo de ticket. |
| `Borrador → Cancelado` | Válida | Sin condición adicional (descartar un borrador). |
| `Publicado → Cancelado` | Válida **solo si `UtcNow <= FechaFin`** | Cancelación antes o durante el curso del evento (el evento todavía no es `Finalizado`). |
| `Publicado (Finalizado) → Cancelado` | **Inválida** | Un evento `Publicado` cuyo estado efectivo ya es `Finalizado` (`UtcNow > FechaFin`) es terminal: la cancelación se rechaza con excepción tipada y HTTP 409. |
| `Cancelado → *` | Inválida | `Cancelado` es terminal: no persiste transición de salida. |
| `Publicado → Publicado` (publicar de nuevo) | Inválida | Rechazada. |
| `Cancelado → Cancelado` (cancelar de nuevo) | Inválida | Rechazada. |
| `Cancelado → Publicado` / `Finalizado → Publicado` | Inválida | Publicar un evento cancelado o finalizado se rechaza. |

`Finalizado` es un estado efectivo derivado y **terminal desde el punto de vista funcional**: no admite compra, no admite validación, y **tampoco admite cancelación** (ni ninguna otra transición). No es un nodo de la máquina de estados persistida: no se "entra" ni se "sale" de él con una transición explícita, simplemente se calcula en cada lectura/operación — pero por ser terminal, la implementación de `CancelEventAsync` debe evaluar primero el **estado efectivo** del evento (¿ya pasó `FechaFin`?) y no solo el estado persistido `Publicado`, antes de aceptar la transición a `Cancelado`.

Cualquier transición marcada como inválida en la tabla se rechaza mediante una excepción tipada (p. ej. `EventInvalidTransitionException`) mapeada a **HTTP 409**.

---

## 2. API-MVP 1 — Validaciones, estados, privacidad y DTOs de Evento (implementada y verificada)

### Objetivo
Cerrar los huecos de la auditoría sobre creación, validación, transición de estados, edición y exposición pública de `Event`, incorporar `FechaInicio`/`FechaFin`, y unificar todos los endpoints de lectura a DTOs.

### Reglas funcionales exactas
- `EventCreateRequest`/`TicketGroupDto`: `Nombre` y `Ubicacion` del evento no vacíos; `TicketGroups` con al menos un elemento; cada tipo de ticket con `Nombre` no vacío, `Precio >= 0` (entradas gratuitas permitidas), `CantidadDisponible >= 1`; `FechaInicio` estrictamente futura; `FechaFin` estrictamente posterior a `FechaInicio`.
- Estado inicial de todo evento nuevo: `Borrador`.
- `PublishEventAsync`: solo válido desde `Borrador` (tabla de §1); exige `TicketTypes.Any()`; ownership verificado contra `OrganizadorPersonaId` re-leído de Firestore.
- `CancelEventAsync`: válido desde `Borrador`, y desde `Publicado` **solo si `UtcNow <= FechaFin`** (el evento aún no es `Finalizado`, ver §1). La implementación evalúa primero el estado efectivo del evento, no solo el persistido: un `Publicado` cuyo estado efectivo ya es `Finalizado` (`UtcNow > FechaFin`) rechaza la cancelación con una excepción tipada → HTTP 409. No toca ningún `Ticket` (decisión 1); ownership verificado igual que publish.
- `UpdateEventAsync`: solo permitido si `Estado == Borrador`; en cualquier otro estado, rechazado con una excepción tipada → 409. No existe edición parcial de campos publicados: para corregir un evento publicado hay que cancelarlo (no hay "despublicar").
  - **Edición de tipos de ticket (cerrado)**: la edición en `Borrador` incluye sus tipos de ticket. Para el MVP, `EventUpdateRequest` reemplaza la **colección completa** de `TicketGroups` (no hay edición incremental por id de tipo de ticket individual). Esto es seguro porque un evento en `Borrador` no acepta compras: no hay stock vendido que pueda quedar huérfano o inconsistente al reemplazar la colección. Cada tipo reemplazado exige `Nombre` no vacío, `Precio >= 0`, `CantidadDisponible >= 1`. El evento debe conservar al menos un tipo de ticket para poder publicarse (si `UpdateEventAsync` dejara la colección vacía, sigue siendo válido guardarlo en `Borrador`, pero `PublishEventAsync` lo rechazará por falta de tipos de ticket).
- `GetByIdAsync` (público, `[AllowAnonymous]`, `GET /api/events/{id}`): devuelve el evento solo si `Estado == Publicado && UtcNow <= FechaFin` (vigencia de catálogo, §0.1); en cualquier otro caso, 404 (nunca 403, para no confirmar existencia a un anónimo). Un evento en curso (entre `FechaInicio` y `FechaFin`) sigue siendo visible.
- Nueva ruta autenticada `GET /api/events/organizer/{id}`: devuelve el detalle de un evento propio en cualquier estado (`Borrador`, `Publicado`, `Cancelado`, `Finalizado` derivado), protegida con la policy `EVENTO_VER_PROPIOS` ya existente + verificación de ownership (404 si no existe, 403 si existe pero no es del actor).
- `SearchEventsAsync` (catálogo público): filtra `Estado == Publicado && UtcNow <= FechaFin` (misma vigencia de catálogo que el detalle público, §0.1 / decisión "mantener el filtro de vigencia tanto en búsqueda como en detalle público").
- `GET /api/events/organizer/me` (lista propia): sin filtro de estado/fecha (el organizador ve todos sus eventos en cualquier estado), pero devuelve DTOs.
- Los tres endpoints de lectura (`GetEvent`, `SearchEvents`, `GetMyEvents`) y la nueva ruta de detalle propio devuelven `EventResponse`/`PagedResponse<EventResponse>`; ninguno devuelve `Event` crudo.
- `EventResponse` expone `FechaInicio`, `FechaFin` y un campo `Estado` **efectivo/derivado** que puede valer `"Borrador"`, `"Publicado"`, `"Cancelado"` o `"Finalizado"` (los primeros tres reflejan el campo persistido; el cuarto se calcula en el momento de la respuesta según §0.1).

### Archivos/componentes probablemente afectados
- `HoyDonde.API/Models/Event.cs` — reducir `EventStatus` a `Borrador`/`Publicado`/`Cancelado`; reemplazar el campo persistido `Fecha` por `FechaInicio` (alinea el nombre interno con el que ya usan `EventCreateRequest`/`EventResponse`, cerrando además la inconsistencia de nombres detectada en la auditoría) y agregar `FechaFin`.
- `HoyDonde.API/DTOs/EventCreateRequest.cs`, `TicketGroupDto.cs` — `DataAnnotations` (`[Required]`, `[Range]`) + `FechaFin`.
- `HoyDonde.API/DTOs/EventUpdateRequest.cs` — incorporar `FechaInicio`, `FechaFin` y reemplazo completo de `TicketGroups` (editables solo en `Borrador`).
- `HoyDonde.API/DTOs/EventResponse.cs` — `FechaInicio`, `FechaFin`, campo `Estado` derivado; usar en todos los reads.
- `HoyDonde.API/DTOs/EventSearchFilterDto.cs`, `PagedResponse.cs` — ajuste de tipo genérico a `EventResponse`.
- `HoyDonde.API/Services/IEventService.cs`, `EventService.cs` — `CreateEventAsync`, `PublishEventAsync`, `CancelEventAsync`, `UpdateEventAsync`, `GetByIdAsync`, nuevo `GetOwnedByIdAsync` (o similar), `SearchEventsAsync`, `GetByOrganizerIdAsync`, cálculo del `Estado` efectivo.
- `HoyDonde.API/Controllers/EventsController.cs` — nueva ruta `GET /api/events/organizer/{id}`; ajustar tipos de retorno de los `GET` existentes.
- Nuevas excepciones tipadas (p. ej. `EventInvalidTransitionException`, `EventNotEditableException`, `EventMissingTicketTypesException`) mapeadas a 409/400 en el controller, siguiendo el patrón ya usado por `EventNotFoundException`/`EventOwnershipException`.
- Eliminar `EventService.GetAllAsync` (código muerto, sin caller) si no se necesita para ninguna ruta nueva de este plan.

### Pruebas necesarias
- Unit/DTO: rechazo de precio negativo, cantidad ≤0, nombre vacío, `TicketGroups` vacío, `FechaInicio` pasada, `FechaFin <= FechaInicio`; aceptación de precio `0` (entrada gratuita).
- Controller/Emulador: `PublishEventAsync` rechaza sin tipos de ticket, rechaza si no está en `Borrador`; `CancelEventAsync` rechaza desde `Cancelado`, acepta desde `Borrador`, acepta desde `Publicado` no finalizado (`UtcNow <= FechaFin`, incluido un evento en curso) y **rechaza con 409 desde `Publicado` ya `Finalizado`** (`UtcNow > FechaFin`); `UpdateEventAsync` rechaza fuera de `Borrador`, acepta y persiste el reemplazo completo de `TicketGroups` en `Borrador`.
- `GetEvent` anónimo: 404 sobre `Borrador`/`Cancelado`/`Publicado`-finalizado (`UtcNow > FechaFin`); 200 sobre `Publicado` con `UtcNow <= FechaFin`, incluyendo el caso "evento en curso" (`FechaInicio <= UtcNow <= FechaFin`) para confirmar que sigue visible.
- Nueva ruta de detalle propio: 200 para el dueño en cualquier estado (incluido `Borrador`, `Cancelado`, `Finalizado`), 403 para otro organizador, 404 si no existe.
- `SearchEvents`: no incluye eventos `Borrador`/`Cancelado`/`Publicado`-finalizado; sí incluye eventos `Publicado` en curso.
- Contrato: todos los `GET` de eventos devuelven `EventResponse`, nunca serializan campos no declarados en el DTO; `Estado` derivado correcto en cada caso.

### Criterio verificable de finalización
Todos los tests anteriores en verde; `EventStatus` persistido tiene solo 3 valores; el modelo persiste `FechaInicio`/`FechaFin`; `CancelEventAsync` evalúa el estado efectivo (no solo el persistido) y rechaza con 409 la cancelación de un evento ya `Finalizado`; ningún controller de eventos retorna `Event` crudo; `dotnet build` sin errores; los tests existentes de la suite de seguridad/ownership de `EventsControllerTests`/`EventServiceEmulatorTests` siguen pasando sin modificación de su intención original.

### Estado: implementada y verificada (cierre, 2026-08-02)

API-MVP 1 está **implementada y verificada** contra el HEAD actual. Resultado final de verificación:

- `dotnet build HoyDonde.sln`: sin errores.
- Suite completa (unit + controller + integración) contra Firestore Emulator real (`npx firebase-tools@13.35.1 emulators:exec ...`, ver `CLAUDE.md`): **270 passed, 0 failed, 0 skipped**.

Resumen de lo efectivamente implementado:

- `Event` persiste `FechaInicio`/`FechaFin`, ambas UTC, en reemplazo del campo único `Fecha`.
- Estados persistidos reducidos a `Borrador`/`Publicado`/`Cancelado`; `Finalizado` es exclusivamente un estado efectivo derivado (`Event.GetEstadoEfectivo`), nunca escrito en Firestore.
- Máquina de estados de §1 implementada tal cual, incluida la evaluación del estado efectivo (no solo el persistido) antes de aceptar una cancelación; toda transición inválida se rechaza con `EventInvalidTransitionException` → 409.
- Validaciones de esta sección implementadas en dos capas: DTOs (`DataAnnotations`) y servicio (`EventValidationException` → 400) — textos obligatorios, fechas, `TicketGroups` (al menos uno al crear, no exigido al editar) y cada tipo de ticket (`Nombre`/`Precio >= 0`/`CantidadDisponible >= 1`).
- `UpdateEventAsync` solo opera sobre `Borrador` (`EventNotEditableException` → 409 en cualquier otro estado) y reemplaza la colección **completa** de `TicketGroups`, tal como cerró la decisión 6.
- `GET /api/events/{id}` y `SearchEventsAsync` respetan la privacidad pública de §0.1: solo `Publicado && UtcNow <= FechaFin`; cualquier otro caso, 404 (nunca 403, para no confirmar existencia a un anónimo).
- `GET /api/events/organizer/{id}` implementada, protegida con `EVENTO_VER_PROPIOS` + verificación de ownership, devuelve el evento propio en cualquier estado.
- Los cuatro endpoints de lectura de eventos (`GetEvent`, `SearchEvents`, `GetMyEvents`, `GetOwnedEvent`) devuelven DTOs (`EventResponse`/`PagedResponse<EventResponse>`); ninguno expone el modelo `Event`.
- `SearchEventsAsync` pagina completamente del lado de Firestore: el filtro opcional `FechaInicio`, el cursor (`StartAfter`) y el `Limit` son parte de la misma consulta — no hay filtrado en memoria en ningún punto de la paginación.
- Cuatro índices compuestos explícitos en `firestore.indexes.json` cubren las combinaciones reales de filtros de igualdad opcionales (`Categoria`/`Ubicacion`) junto con el rango obligatorio (`Estado`+`FechaFin`+`FechaInicio`). **Nota prudente**: el Firestore Emulator valida la lógica de las consultas, no la existencia de índices — una suite en verde contra el emulador no acredita que estos índices estén efectivamente desplegados en un proyecto de Firestore de producción; eso requiere `firebase deploy --only firestore:indexes` (o equivalente) contra ese proyecto, verificación que queda fuera del alcance de este cierre.

API-MVP 2 (§3), API-MVP 3 (§4) y API-MVP 4 (§5) están **cerradas** (ver sus propias secciones "Estado: implementada y verificada").

### Dependencias
Ninguna — puede iniciarse de inmediato.

### Fuera de alcance
Aprobación administrativa de eventos; transición automática a `Finalizado` persistida (permanece derivada); job/scheduler de ningún tipo; edición de un evento publicado (ni siquiera parcial); reactivar (`Cancelado → Publicado`); edición incremental de un tipo de ticket individual por id (se reemplaza la colección completa); ventana de "apertura de puertas" antes de `FechaInicio` para habilitar la validación.

---

## 3. API-MVP 2 — Compra, cancelación, consulta enriquecida y concurrencia de Tickets

### Objetivo
Cerrar los huecos de compra, la falta de verificación del estado y vigencia del evento en la validación, el DTO pobre de ticket, y probar formalmente la ausencia de sobreventa.

### Reglas funcionales exactas
- `BuyTicketsAsync`: dentro de la misma transacción de Firestore que lee/descuenta stock, lee también el `Event` y rechaza si no cumple la vigencia de compra (§0.1): `Estado == Publicado && UtcNow < FechaInicio`. Ya no acepta `Estado == Activo` (ese estado deja de existir tras API-MVP 1). Una vez que el evento empezó (`UtcNow >= FechaInicio`), la compra se rechaza aunque el evento siga `Publicado`, siga visible en catálogo y siga aceptando validaciones.
- Al emitir cada `Ticket`, se persiste una fotografía inmutable: `EventoNombre`, `TicketTypeNombre`, `PrecioPagado`, `FechaInicio`, `FechaFin` (las cinco copiadas del `Event`/`TicketType` leído dentro de la misma transacción de compra, nunca recalculadas después ni aceptadas del cliente). `Ticket.Estado` inicial: `Emitido`. *(Corrección post-cierre, 2026-08-02: la primera implementación resolvía `FechaInicio`/`FechaFin` en vivo desde el `Event` en el momento de construir la respuesta en lugar de desnormalizarlas en el `Ticket`; se corrigió para que ambas fechas también formen parte de la fotografía persistida, igual que `EventoNombre`/`TicketTypeNombre`/`PrecioPagado`, y así queden protegidas ante cualquier cambio futuro del `Event`.)*
- `ValidateTicketAsync`/`TryConsumeAsync`: dentro de la misma transacción que intenta marcar el ticket como `Usado`, lee también el `Event` y rechaza si no cumple la vigencia de validación (§0.1): `Estado == Publicado && UtcNow <= FechaFin`. No se escribe ningún cambio en el `Ticket` de un evento cancelado o finalizado; el documento del ticket conserva su `Estado` histórico (`Emitido`).
- No hay actualización masiva/batch de tickets al cancelar un evento (decisión 1, ya reflejada en API-MVP 1: `CancelEventAsync` no toca tickets).
- `TicketResponseDto` expone, además de los campos actuales:
  - `Estado` — valor **histórico/persistido** del ticket (`Emitido`/`Usado`/`Anulado`), nunca reescrito por una cancelación de evento.
  - `Utilizable: bool` — campo **derivado**, calculado en el momento de la lectura (sin mutar el documento) como: `Estado == Emitido && Event.Estado == Publicado && UtcNow <= Event.FechaFin`. Es decir, `false` si el ticket ya fue usado o anulado, si el evento fue cancelado, o si el evento ya está "Finalizado" derivado.
  - `MotivoNoUtilizable` — string público, nulo si `Utilizable == true`; en caso contrario, uno de: `"Usado"`, `"Anulado"`, `"EventoCancelado"`, `"EventoFinalizado"` (según cuál condición de las anteriores aplique).
  - `EventoNombre`, `TicketTypeNombre`, `PrecioPagado`, `FechaInicio`, `FechaFin` — fotografía inmutable de la compra, persistida en el propio documento del `Ticket`; nunca se recalculan contra el `Event` actual.
- `GetTicketsByClienteIdAsync` resuelve `Utilizable`/`MotivoNoUtilizable` por evento (lectura batch agrupada por `EventoId` distinto, una sola resolución por evento — no una lectura por ticket — de los eventos involucrados en el lote de tickets del cliente); `EventoNombre`/`TicketTypeNombre`/`PrecioPagado`/`FechaInicio`/`FechaFin` ya están fijos en el propio documento del ticket y no requieren esa lectura.

### Archivos/componentes probablemente afectados
- `HoyDonde.API/Models/Ticket.cs` — nuevos campos `EventoNombre`, `TicketTypeNombre`, `PrecioPagado`, `FechaInicio`, `FechaFin` (fotografía inmutable completa).
- `HoyDonde.API/DTOs/TicketResponseDto.cs` — campos nuevos (`Estado`, `Utilizable`, `MotivoNoUtilizable`, `EventoNombre`, `TicketTypeNombre`, `PrecioPagado`, `FechaInicio`, `FechaFin`).
- `HoyDonde.API/Services/TicketService.cs` — `BuyTicketsAsync` (lectura de evento dentro de la transacción, chequeo de vigencia-compra, escritura de la fotografía), `ValidateTicketAsync` (chequeo de vigencia-validación), `GetTicketsByClienteIdAsync` (resolución de `Utilizable`/`MotivoNoUtilizable`/fechas).
- `HoyDonde.API/Repositories/ITicketValidationStore.cs`, `FirestoreTicketValidationStore.cs` — `TryConsumeAsync` debe leer el `Event` dentro de la misma transacción y devolver un resultado que distinga ticket usado/anulado de evento cancelado/finalizado.
- `HoyDonde.API/Models/TicketValidationOutcome.cs` — agregar valores para distinguir "evento cancelado" de "evento finalizado" (ambos se mapean a 409, pero el mensaje público difiere).
- Nuevas excepciones tipadas para `BuyTicketsAsync` (p. ej. `EventoNoDisponibleParaCompraException`, `StockInsuficienteException`, `TicketTypeInvalidoException`) mapeadas a 404/409/400 en `TicketsController`.
- `HoyDonde.API/Controllers/TicketsController.cs` — mapeo de las excepciones nuevas.

### Pruebas necesarias
- Emulador: compra rechazada sobre `Borrador`/`Cancelado`/`Publicado`-en-curso (`UtcNow >= FechaInicio`)/`Publicado`-finalizado; compra exitosa sobre `Publicado` con `UtcNow < FechaInicio`, incluyendo `Precio == 0`.
- Emulador: **test de concurrencia de sobreventa** — N compras concurrentes (`Task.WhenAll`) contra un `TicketType` con stock pequeño (p. ej. 1); assert de que la suma de tickets emitidos nunca excede el stock inicial y que `CantidadDisponible` nunca queda negativo. Este es el test explícitamente exigido por la decisión 9 y debe quedar verde antes de cerrar esta etapa (se reverifica también como parte del gate final de API-MVP 4).
- Emulador: cancelar un evento con tickets `Emitido` → `ValidateTicketAsync` sobre esos tickets se rechaza, y el documento del `Ticket` conserva `Estado == Emitido` (no se reescribe en batch); `TicketResponseDto.Utilizable == false` con `MotivoNoUtilizable == "EventoCancelado"`.
- Emulador: validar un ticket de un evento `Publicado` **en curso** (`FechaInicio <= UtcNow <= FechaFin`) → debe aceptarse (confirma que la vigencia de validación no depende de `FechaInicio`).
- Emulador: validar un ticket de un evento `Publicado` cuyo `FechaFin` ya pasó → debe rechazarse (`Finalizado`), `TicketResponseDto.Utilizable == false` con `MotivoNoUtilizable == "EventoFinalizado"`.
- Contrato: `GetMyTickets`/respuesta de `BuyTickets` incluyen `EventoNombre`, `TicketTypeNombre`, `PrecioPagado`, `FechaInicio`, `FechaFin`, `Estado`, `Utilizable` y `MotivoNoUtilizable`; `Utilizable` cambia a `false` tras cancelar el evento o tras pasar `FechaFin`, sin que el `Estado` persistido del ticket cambie.

### Criterio verificable de finalización
Todos los tests anteriores en verde, incluido el de concurrencia; ningún ticket de un evento cancelado o finalizado puede pasar por `ValidateTicketAsync` con éxito; un ticket sigue siendo validable durante todo el curso del evento (desde publicado hasta `FechaFin`), incluso después de `FechaInicio`; la respuesta de "mis tickets" es autosuficiente para una pantalla de frontend sin llamadas adicionales.

### Dependencias
API-MVP 1 (necesita `EventStatus` reducido a 3 valores y `FechaInicio`/`FechaFin` ya persistidos en `EventService`).

### Fuera de alcance
Actualización masiva/batch de tickets al cancelar (rechazada explícitamente por decisión 1); reembolsos; notificaciones al cliente al cancelar; anulación manual de un ticket individual fuera del flujo de validación; recalcular `PrecioPagado` si el organizador pudiera editar precios después de publicar (no aplica: API-MVP 1 ya prohíbe editar un evento publicado); ventana de "apertura de puertas" previa a `FechaInicio` (decisión explícita: la validación se habilita desde que el evento está publicado, sin ventana adicional).

### Estado: implementada y verificada (cierre, 2026-08-02)

API-MVP 2 está **implementada y verificada** contra el HEAD actual. Resultado final de verificación:

- `dotnet build HoyDonde.sln`: **0 errores**.
- Suite completa (unit + controller + integración) contra Firestore Emulator real (`npx firebase-tools@13.35.1 emulators:exec ...`, ver `CLAUDE.md`): **291 passed, 0 failed, 0 skipped**.

Resumen de lo efectivamente implementado:

- **Compra atómica** (`TicketService.BuyTicketsAsync`): el `Event` se lee dentro de la misma transacción de Firestore que descuenta stock y crea los tickets; solo se acepta la compra con `Estado == Publicado && UtcNow < FechaInicio` (`EventoNoDisponibleParaCompraException` → 409 en cualquier otro caso); el `TicketType` inexistente (`TicketTypeInvalidoException` → 404) y el stock insuficiente (`StockInsuficienteException` → 409) se validan contra el `Event` recién leído, nunca contra datos enviados por el cliente. La transacción de Firestore (reintento automático del SDK sobre conflicto real, `ABORTED`) protege contra sobreventa bajo concurrencia: verificado con un test de N compras concurrentes contra stock 1 (`BuyTicketsAsync_ConcurrentPurchases_StockOne_OnlyOneSucceeds`) — exactamente una compra exitosa, un solo `Ticket` creado, `CantidadDisponible` nunca negativo.
- **Fotografía inmutable completa**: `EventoNombre`, `TicketTypeNombre`, `PrecioPagado`, `FechaInicio` y `FechaFin` se copian las cinco del `Event`/`TicketType` leído dentro de la transacción de compra y se persisten en el propio documento del `Ticket`; ninguna se recalcula después ni se acepta del cliente (`TicketBuyRequest` no tiene siquiera un campo de fecha o precio). Ver nota de corrección más arriba en "Reglas funcionales exactas".
- **Validación atómica** (`FirestoreTicketValidationStore.TryConsumeAsync`): el `Ticket` y el `Event` se leen dentro de la misma transacción antes de marcar `Usado`; se rechaza con `EventoCancelado`/`EventoFinalizado` (409) si el `Event` no está `Publicado` o ya pasó `FechaFin`, sin escribir ningún cambio en el `Ticket` (su `Estado` histórico permanece `Emitido`); el doble uso del mismo ticket se rechaza (`AlreadyUsed`) tanto en secuencia como bajo concurrencia (`ValidateTicketAsync_ConcurrentValidations_OnlyOneSucceeds`, con `Task.WhenAll` sobre el mismo ticket).
- **Cancelación sin actualización masiva**: `CancelEventAsync` (API-MVP 1) no toca ningún `Ticket`; verificado explícitamente en `CancelarEvento_CambiaUtilizable_PeroNoLaFotografiaHistoricaDelTicket` — cancelar el evento cambia `Utilizable`/`MotivoNoUtilizable` en la lectura, pero el documento del `Ticket` conserva su `Estado` (`Emitido`) y su fotografía histórica intactos.
- `Utilizable`/`MotivoNoUtilizable` se derivan del `Event` **actual** en el momento de la lectura (nunca de la fotografía): `Usado`/`Anulado` si el `Ticket` ya fue consumido/anulado, `EventoCancelado`/`EventoFinalizado` según el estado efectivo del `Event`, o `Utilizable == true` en caso contrario.
- `GetTicketsByClienteIdAsync` agrupa los tickets del cliente por `EventoId` distinto y resuelve esos eventos con una única lectura batch (`FirestoreDb.GetAllSnapshotsAsync`) — nunca una lectura por ticket; verificado con `GetTicketsByClienteIdAsync_MultipleTicketsSameEvent_ResolveConsistently` (3 tickets del mismo evento reflejan consistentemente el mismo resultado derivado de una única resolución).
- Excepciones tipadas nuevas (`EventoNoDisponibleParaCompraException`, `TicketTypeInvalidoException`, `StockInsuficienteException`) mapeadas a 409/404/409 en `TicketsController`; `TicketValidationOutcome`/`TicketConsumeResult` distinguen `Anulado` (ticket) de `EventoCancelado`/`EventoFinalizado` (evento), todos mapeados a 409 con mensaje público distinto. Ningún `catch (Exception ex)` genérico quedó en los flujos de compra/validación de `TicketsController`; lo no anticipado sube a `ExceptionMiddleware`.

**Riesgo no bloqueante**: durante una corrida de la suite completa en paralelo, `BuyTicketsAsync_ConcurrentPurchases_StockOne_OnlyOneSucceeds` falló una vez con `Grpc.Core.RpcException: Aborted — Transaction lock timeout` por contención real del Firestore Emulator bajo ejecución concurrente de múltiples clases de test a la vez; en aislamiento y en las corridas subsiguientes de la suite completa pasó sin problemas (291 passed, 0 failed, 0 skipped). No se considera un defecto del código de producción — es contención del emulador local bajo carga de test paralela, no reproducida contra una sola transacción real de negocio. Revisar la paralelización de la suite de integración (por ejemplo, desactivar el paralelismo de xUnit entre clases que comparten el emulador) solo si esta falla vuelve a repetirse.

API-MVP 3 (§4) y API-MVP 4 (§5) están **cerradas** (ver sus propias secciones "Estado: implementada y verificada").

---

## 4. API-MVP 3 — Asignación de un Control existente a otro evento

### Objetivo
Cerrar el hueco entre "el modelo soporta N:N Control↔Evento" y "la API lo expone", sin tocar el catálogo de acciones de seguridad.

### Reglas funcionales exactas
- Ruta cerrada: `POST /api/events/{eventId}/controls/{controlPersonaId}`, protegida con la policy existente `CONTROL_CREAR` (decisión 4: sin acción nueva) y con verificación de ownership del evento destino.
- El actor autenticado debe ser el `OrganizadorPersonaId` del evento destino (mismo patrón de ownership ya usado en `RegisterControlAsync`, re-leído de Firestore).
- El `controlPersonaId` recibido debe corresponder a un `Usuario` existente con rol `CONTROL` activo (no se crea ninguna identidad nueva; no se acepta cualquier `PersonaId` arbitrario).
- La asignación reutiliza `IControlAsignacionRepository.AsignarAsync` (ya idempotente y transaccional a nivel repositorio, probado con concurrencia).
- Llamar al endpoint dos veces con el mismo par Control-Evento es un no-op exitoso (idempotencia), no un error.

### Archivos/componentes probablemente afectados
- `HoyDonde.API/Controllers/EventsController.cs` — nueva acción `POST /api/events/{eventId}/controls/{controlPersonaId}` (la ruta cuelga de `events`, no de `users`, según el contrato cerrado).
- `HoyDonde.API/Services/UserService.cs` (o `EventService.cs`, según dónde termine viviendo la lógica de asignación) — nuevo método (p. ej. `AsignarControlExistenteAsync(actorUid, eventId, controlPersonaId)`), validación de ownership + validación de que la `PersonaId` recibida es un `Usuario` con rol `CONTROL`.
- `HoyDonde.API/Repositories/IControlAsignacionRepository.cs`, `FirestoreControlAsignacionRepository.cs` — sin cambios (ya soportan el caso).
- Excepción tipada para "la `PersonaId` dada no es un Control válido" si no existe ya una equivalente reutilizable.

### Pruebas necesarias
- HTTP + service: asignación exitosa a un segundo evento propio; rechazo por evento ajeno (403); rechazo por evento inexistente (404); rechazo si `controlPersonaId` no corresponde a un `Usuario` con rol `CONTROL`; idempotencia (segunda llamada no falla ni duplica).
- Emulador: confirmar persistencia del segundo `ControlAsignacion` con el mismo `ControlPersonaId` y `EventId` distinto (ya cubierto en parte por los tests de repositorio existentes; agregar el camino HTTP completo).

### Criterio verificable de finalización
Un organizador puede asignar un Control ya existente a un segundo evento propio sin crear una cuenta nueva, vía `POST /api/events/{eventId}/controls/{controlPersonaId}`, con los tests anteriores en verde.

### Dependencias
Ninguna estructural respecto a API-MVP 1/2; se ubica en tercer lugar para no tocar `EventsController`/`UserService` dos veces mientras esas etapas están en curso.

### Fuera de alcance
Endpoint de "desasignar" un Control de un evento; listar las asignaciones de un Control; que un Control se autoasigne o cambie de evento sin intervención del organizador.

### Estado: implementada y verificada (cierre, 2026-08-02)

API-MVP 3 está **implementada y verificada** contra el HEAD actual. Resultado final de verificación:

- `dotnet build HoyDonde.sln`: sin errores.
- Suite completa (unit + controller + integración) contra Firestore Emulator real (`npx firebase-tools@13.35.1 emulators:exec ...`, ver `CLAUDE.md`): **327 passed, 0 failed, 0 skipped** (291 antes de este cierre; 36 tests nuevos).

Resumen de lo efectivamente implementado:

- Ruta cerrada `POST /api/events/{eventId}/controls/{controlPersonaId}`, protegida con la policy **ya existente** `CONTROL_CREAR` (`EventsController.AssignControl`); no se agregó ninguna acción 21 ni se tocó el catálogo de 20 acciones.
- El actor autenticado se resuelve exclusivamente en servidor (`IAuthenticatedPersonaResolver`, UID de token → `PersonaId`), nunca desde un campo del body/query; se compara contra `Event.OrganizadorPersonaId` re-leído de Firestore antes de cualquier otra validación (`EventOwnershipException` → 403 si no coincide, `EventNotFoundException` → 404 si el evento no existe).
- Estados admitidos y rechazados: se permite asignar mientras el evento está en `Borrador`, `Publicado` (no iniciado) o `Publicado` en curso; se rechaza con `EventoNoDisponibleParaAsignacionControlException` → 409 si el evento está `Cancelado`, o si es `Publicado` con estado efectivo `Finalizado` (`UtcNow > FechaFin`). La verificación evalúa el estado efectivo, igual que `CancelEventAsync` (API-MVP 1).
- Validación uniforme del Control destino (`UserService.AsignarControlExistenteAsync`): la `PersonaId` recibida debe corresponder a un `Usuario` existente (`IUsuarioRepository.GetByPersonaIdAsync`, nuevo), con `IsActive == true`, y con el rol `CONTROL` activo (`GetRolCodigosActivosAsync`). Los tres motivos de rechazo (Persona inexistente, Usuario inactivo, sin rol CONTROL activo) colapsan en una única excepción pública `ControlInvalidoException` → HTTP 404, con un mensaje que nunca distingue cuál de los tres aplica, para no filtrar si una `PersonaId` dada existe o no en el sistema.
- Pertenencia previa al mismo organizador (ámbito, nuevo método `IControlAsignacionRepository.ExisteAsignacionPorAsignadorAsync`): antes de asignar a un evento nuevo, el Control debe tener ya al menos una `ControlAsignacion` (a cualquier evento) creada por ese mismo organizador; de lo contrario `ControlAjenoException` → HTTP 403. Esto impide que un organizador se apropie de un Control administrado exclusivamente por otro. La consulta usa dos filtros de igualdad + `Limit(1)`, sin cargar la colección completa en memoria y sin requerir un índice compuesto nuevo (Firestore resuelve igualdad+igualdad con los índices de campo simple por defecto).
- Idempotencia y concurrencia: la asignación reutiliza `IControlAsignacionRepository.AsignarAsync` (ya transaccional a nivel repositorio desde API-MVP/Etapa 4 de seguridad); se agregó `GetAsignacionAsync` para leer el documento resultante y devolver siempre `AssignedByPersonaId`/`CreatedAt` de la **primera** asignación, incluso en una repetición del mismo par `(controlPersonaId, eventId)`. Verificado con llamadas concurrentes tanto a nivel repositorio (test preexistente) como a nivel `UserService` contra el emulador real (`UserServiceControlAssignmentEmulatorTests.AsignarControlExistenteAsync_CalledConcurrently_ForSamePair_NoneFail_AndOnlyOneAssignmentSurvives`, 8 llamadas concurrentes para el mismo par).
- No se crea ninguna cuenta Firebase, `Persona`, `Usuario` ni `UsuarioRol` nuevos: la asignación opera exclusivamente sobre un Control ya aprovisionado. Verificado explícitamente (`identityProvider.Verify(... CreateIdentityAsync ..., Times.Never)` y `usuarioRepository.Verify(... ProvisionarAsync ..., Times.Never)` en los tests de servicio, tanto con mocks como contra el emulador real).
- Contrato público acotado: `ControlAsignacionResponseDto` expone únicamente `ControlPersonaId`, `EventId`, `AssignedByPersonaId` y `CreatedAt`; nunca el UID de Firebase, el `UsuarioId` ni ningún dato del proveedor de identidad. Verificado con un test HTTP que confirma que la respuesta no contiene el UID del actor ni las cadenas `"ExternalSubjectId"`/`"UsuarioId"`.

API-MVP 4 (§5) está **cerrada** (ver su propia sección "Estado: implementada y verificada"). El frontend (§6/§7) sigue sin tocarse.

---

## 5. API-MVP 4 — Contratos HTTP, errores, documentación y recorrido completo con emulador

### Objetivo
Cerrar la inconsistencia de manejo de errores acumulada en las tres etapas anteriores, actualizar `API_Documentation.md` al estado real del código, y dejar el gate de verificación exigido por la decisión 9 antes de tocar frontend.

### Reglas funcionales exactas
- Ningún controller (`EventsController`, `TicketsController`, `UserController`) conserva un `catch (Exception ex) => BadRequest(new { message = ex.Message })` genérico para reglas de negocio conocidas; cada regla usa la excepción tipada introducida en su etapa correspondiente (API-MVP 1/2/3), mapeada al código HTTP semánticamente correcto (400 validación, 404 no encontrado, 403 ownership, 409 conflicto de estado/transición/vigencia).
- Cualquier excepción no anticipada (no tipada) deja de capturarse en el controller y sube al `ExceptionMiddleware`, que ya oculta el detalle interno en producción y agrega `RequestId`.
- `API_Documentation.md` se reescribe para reflejar exactamente: rutas reales (incluidas `GET /api/events/organizer/{id}` y `POST /api/events/{eventId}/controls/{controlPersonaId}`), DTOs reales (incluidos `FechaInicio`/`FechaFin`/`Utilizable`/`MotivoNoUtilizable` en `TicketResponseDto` y el `Estado` derivado en `EventResponse`), mecanismo de autorización real (`IPermissionService`, no Custom Claims), colecciones reales de Firestore. Esta reescritura es un entregable de esta etapa, no de la planificación actual.
- Gate de verificación (decisión 9), ejecutado sobre el Firestore Emulator:
  1. Test encadenado: Organizador crea evento (con `FechaInicio`/`FechaFin` futuras) → publica → Cliente compra ticket (antes de `FechaInicio`) → Control (asignado por el organizador) valida el ticket (en cualquier momento entre la publicación y `FechaFin`, incluido después de `FechaInicio`) → segundo intento de validación sobre el mismo ticket se rechaza. Un único test, con estado real compartido entre los pasos (no mocks), recorriendo los endpoints HTTP reales vía `TestApplicationFactory` o equivalente contra el emulador.
  2. Reverificación del test de concurrencia/sobreventa introducido en API-MVP 2, como parte de la corrida de regresión completa de esta etapa.

### Archivos/componentes probablemente afectados
- `HoyDonde.API/Controllers/EventsController.cs`, `TicketsController.cs`, `UserController.cs` — limpieza final de manejo de excepciones.
- `API_Documentation.md` — reescritura completa.
- Nuevo archivo de test de integración (p. ej. `HoyDonde.API.Tests/Integration/FullJourneyEmulatorTests.cs`) para el recorrido encadenado.

### Pruebas necesarias
- Test de contrato de error: para cada excepción tipada introducida en API-MVP 1/2/3, verificar el código HTTP y que el mensaje público no filtra detalles internos de una excepción no anticipada.
- El recorrido encadenado completo (descrito arriba), en verde.
- Reejecución del test de concurrencia de compra de API-MVP 2, en verde.

### Criterio verificable de finalización
Grep de `catch (Exception ex)` en los tres controllers no debe encontrar ningún caso que devuelva `ex.Message` para una regla de negocio ya tipada; `API_Documentation.md` coincide con el inventario de endpoints real; el test de recorrido encadenado y el de concurrencia están en la suite y pasan; `dotnet test` completo (con emulador) en verde.

### Dependencias
API-MVP 1, 2 y 3 completas (el recorrido encadenado necesita Event + Ticket + Control funcionando end-to-end, y las excepciones tipadas de cada etapa ya deben existir para limpiarse aquí).

### Fuera de alcance
OpenAPI/Swagger enriquecido con ejemplos (el Swagger básico ya configurado en `Program.cs` no se toca); auditoría de dominio tipo `security_audits` para compras/validaciones/cambios de estado (queda como capacidad postergada, ver §10); limpieza de items de baja severidad no bloqueantes (`LoggingMiddleware` con lectura de body no usada, `DataAnnotations` de email/password en `RegisterAdminDto`/`RegisterOrganizadorDto`) — se mueven a la etapa **Entrega**.

### Estado: implementada y verificada (cierre, 2026-08-02)

API-MVP 4 está **implementada y verificada** contra el HEAD actual. Resultado final de verificación:

- `dotnet build HoyDonde.sln`: sin errores.
- Suite completa (unit + controller + integración) contra Firestore Emulator real (`npx firebase-tools@13.35.1 emulators:exec ...`, ver `CLAUDE.md`): **347 passed, 0 failed, 0 skipped**.

Resumen de lo efectivamente implementado:

- **Contrato uniforme de error**: toda respuesta de error del API (excepción de dominio tipada, `ModelState` inválido, o excepción no anticipada) tiene la misma forma pública, `{ code, message, traceId, errors?, detail? }` — `errors` solo presente en `code: "VALIDATION_ERROR"`, `detail` solo en un 500 bajo `Development` (nunca en `Production`).
- `HoyDonde.API/Middleware/ExceptionMiddleware.cs` es el **único** punto que mapea excepciones a HTTP: mantiene un catálogo `Tipo de excepción → (status, code, mensaje público)` con 19 excepciones tipadas más el caso `IdentityNotProvisionedException` (403 genérico, ya existente); ningún controller vuelve a decidir un código HTTP por su cuenta.
- `Program.cs` configura `ApiBehaviorOptions.InvalidModelStateResponseFactory` para que los errores automáticos de `ModelState` (DataAnnotations/binding) sigan exactamente el mismo contrato (`code: "VALIDATION_ERROR"`, con `errors` poblado por campo).
- **Catches genéricos eliminados**: `EventsController`, `TicketsController`, `UserController` y `SecurityAdminController` quedaron sin ningún `try/catch` — toda excepción de dominio se deja propagar hasta `ExceptionMiddleware`; en particular se eliminó el `catch (Exception ex) => BadRequest(new { message = ex.Message })` que `UserController` conservaba en las tres altas privilegiadas (`RegisterAdmin`/`RegisterOrganizador`/`RegisterControl`), fuente de fuga de mensajes internos no auditados.
- **Validaciones de altas privilegiadas**: `RegisterAdminDto`/`RegisterOrganizadorDto` exigen `Email` (`[Required]`+`[EmailAddress]`) y `Password` (`[Required]`+`[MinLength(6)]`, el mínimo de Firebase Authentication); `RegisterControlDto` exige `UserName`/`Password`(≥6)/`EventId`.
- `HoyDonde.API/Middleware/LoggingMiddleware.cs`: se eliminó el bufferizado de `Request.Body` (incluía `Password` de `/api/users/*`) que se leía pero nunca se logueaba, y el método `ShouldLogResponseBody` (código muerto, sin caller).
- **Recorrido HTTP integral** (`HoyDonde.API.Tests/Integration/FullJourneyEmulatorTests.cs`): pipeline HTTP real (`WebApplicationFactory<Program>`), controllers/services/repositorios reales contra Firestore Emulator real; lo único sustituido es el esquema de autenticación (`EmulatorFakeAuthHandler`, mismo criterio que el `FakeAuthHandler` ya existente). Encadena Organizador crea evento → publica → Cliente compra → Control asignado valida → segundo intento rechazado, y verifica en Firestore stock descontado, fotografía completa del ticket, `ClientePersonaId`, `ValidadoPorPersonaId` y `Estado == Usado`.
- **Test de Usuario desactivado** (mismo archivo): un Organizador con `Usuario.IsActive == false` recibe 403 real en `POST /api/events`, sin lógica adicional en el controller — consecuencia directa de que `PermissionService` corta apenas lee `IsActive == false` y `AccionAuthorizationHandler` nunca llama a `context.Succeed`.
- **`TicketTypeResponseDto`** (`HoyDonde.API/DTOs/TicketTypeResponseDto.cs`, corrección aplicada antes de este cierre): DTO de salida con `Id`/`Nombre`/`Precio`/`CantidadDisponible`, usado por `EventResponse.TicketGroups` en los seis endpoints de lectura/escritura de Event (creación, actualización, detalle público, búsqueda pública, lista de eventos propios, detalle autenticado del organizador). `TicketGroupDto` queda exclusivamente como DTO de entrada (sin `Id`, nunca aceptado del cliente). Un cliente real puede resolver el `TicketTypeId` a comprar (`POST /api/tickets/buy`) usando exclusivamente la respuesta HTTP de cualquiera de esos seis endpoints — verificado en el recorrido integral, que obtiene `eventId`/`ticketTypeId` solo de la respuesta de `POST /api/events`, nunca leyendo Firestore antes de comprar.
- `API_Documentation.md` reescrito por completo para reflejar el código final: arquitectura y URL base, Firebase Client SDK + Bearer token, `/api/auth/sync`, bootstrap del primer Administrador, altas privilegiadas, asignación de Control, autorización Usuario→Rol→Acción→Policy, inventario real de endpoints con policies, `EventResponse`/`TicketTypeResponseDto` con ejemplo de reutilización del `ticketTypeId` recibido, contrato uniforme de error con tabla de códigos, reglas de compra/validación, DTOs finales, comandos reproducibles y capacidades fuera de alcance — sin describir Custom Claims, colección `users` ni ningún otro rastro legacy.

API-MVP 1, 2, 3 y 4 están **cerradas** (ver sus propias secciones "Estado: implementada y verificada"). El **backend funcional del MVP queda cerrado**: Frontend 1 (§6), Frontend 2 (§7) y la etapa de preparación de Entrega (§8) siguen **pendientes**, sin cambios respecto a lo planificado en este documento.

---

## 6. Frontend 1 — Firebase Client SDK, sesión y `/api/auth/sync`

### Objetivo
Integrar el frontend Expo con Firebase Auth real y el flujo `/api/auth/sync`, hoy no conectado (`CLAUDE.md`, sección "Frontend status").

### Reglas funcionales exactas
- El frontend autentica directamente contra Firebase Client SDK (no contra el backend); el backend nunca recibe credenciales, solo el ID token resultante.
- Tras un login/registro exitoso de Cliente, el frontend llama `POST /api/auth/sync` con el token, y usa la respuesta (`SyncUserResponseDto`) para conocer los roles efectivos del usuario.
- Manejo de sesión: persistencia del token/refresh según el SDK, sin almacenar contraseñas ni tokens de terceros en el propio backend.
- Este documento no fija el detalle de implementación de esta etapa: requiere una auditoría específica del estado actual de `HoyDonde-frontend/` (fuera del alcance de la auditoría backend ya realizada) antes de descomponerla en tareas concretas.

### Archivos/componentes probablemente afectados
`HoyDonde-frontend/services/APIService.ts` (ya tiene cambios locales preexistentes — coordinar, no pisar), integración de Firebase Client SDK, pantallas de login/registro existentes en `HoyDonde-frontend/app/`.

### Pruebas necesarias
Pruebas de integración del flujo de login/sync (a definir tras la auditoría de frontend); verificación manual del golden path en el simulador/dispositivo antes de reportar como completo (regla general de este proyecto para cambios de frontend).

### Criterio verificable de finalización
Un usuario puede autenticarse con Firebase desde la app, el backend lo reconoce vía `/api/auth/sync`, y la app conoce sus roles efectivos.

### Dependencias
API-MVP 1–4 completas (contratos congelados antes de integrar).

### Fuera de alcance
Alta de Admin/Organizador/Control desde la UI (son flujos privilegiados, no de autoregistro); recuperación de contraseña vía UI.

---

## 7. Frontend 2 — Pantallas y navegación por permisos/roles efectivos

### Objetivo
Construir las pantallas mínimas por rol (Admin, Organizador, Cliente, Control) usando los permisos efectivos devueltos por el backend, sin codificar roles como constantes en el frontend.

### Reglas funcionales exactas
A definir con detalle en una planificación de frontend dedicada; a alto nivel: la navegación se adapta a las acciones efectivamente disponibles para el usuario (consultadas vía `/api/security` o vía la respuesta de `/api/auth/sync`/provisioning), no a un rol hardcodeado en el cliente.

### Archivos/componentes probablemente afectados
`HoyDonde-frontend/app/(tabs)/` y componentes de navegación (ya tienen cambios locales preexistentes — coordinar).

### Pruebas necesarias
A definir; mínimo, verificación manual del golden path de cada rol en el simulador antes de reportar como completo.

### Criterio verificable de finalización
Cada rol puede completar su recorrido mínimo (crear/publicar evento, comprar ticket, validar ticket, administrar seguridad) desde la UI.

### Dependencias
Frontend 1.

### Fuera de alcance
Cualquier pantalla de las capacidades postergadas (§10): pagos, reservas, reventa, QR avanzado, notificaciones, analíticas, merchandising.

---

## 8. Entrega — Pruebas integrales, limpieza y documentación final

### Objetivo
Cerrar el MVP demostrable con una pasada final de calidad, sin agregar funcionalidad nueva.

### Reglas funcionales exactas
No aplica (etapa de estabilización, no de reglas nuevas).

### Archivos/componentes probablemente afectados
- `HoyDonde.API/Middleware/LoggingMiddleware.cs` — eliminar la lectura de `requestBody` que nunca se loguea (o, si se decide usarla, enmascarar campos sensibles como `Password` antes de loguear).
- `HoyDonde.API/DTOs/RegisterAdminDto.cs`, `RegisterOrganizadorDto.cs` — `[EmailAddress]`/longitud mínima de password, si no se resolvió antes.
- Revisión final de `API_Documentation.md` incorporando cualquier ajuste de contrato descubierto durante Frontend 1/2.

### Pruebas necesarias
Corrida completa de `dotnet test` con Firestore Emulator; verificación manual de los cuatro golden paths (Admin, Organizador, Cliente, Control) en la app.

### Criterio verificable de finalización
Suite completa en verde; los cuatro flujos de rol demostrables en una presentación; documentación coincide con el comportamiento real observado.

### Dependencias
Frontend 1 y 2.

### Fuera de alcance
Cualquier capacidad de §10.

---

## 9. Definición del MVP completo

**Flujo Administrador**: bootstrap del primer admin (ya implementado, CLI) → `/api/security` para dar de alta Organizadores y administrar roles/acciones (ya completo, sin cambios en este plan).

**Flujo Organizador**: login → crear evento en `Borrador` con `FechaInicio`/`FechaFin` válidas y ≥1 tipo de ticket válido (API-MVP 1) → publicar (API-MVP 1) → ver sus eventos en cualquier estado (API-MVP 1) → cancelar si hace falta mientras el evento no esté finalizado (API-MVP 1/2; un evento ya finalizado es terminal y no puede cancelarse) → dar de alta un Control para el evento (ya implementado) → asignar ese mismo Control a un segundo evento propio (API-MVP 3).

**Flujo Cliente**: `/api/auth/sync` (ya implementado) → buscar/ver solo eventos publicados y no finalizados, incluidos los que están en curso (API-MVP 1) → comprar tickets antes de que el evento empiece, incluidas entradas gratuitas (API-MVP 2) → ver "mis tickets" con nombre de evento, tipo, precio pagado, fechas y si siguen siendo utilizables, con motivo explícito cuando no lo son (API-MVP 2).

**Flujo Control**: login (alta por el organizador) → validar tickets del evento asignado desde que está publicado hasta `FechaFin`, incluso después de iniciado el evento, rechazando solo tickets ya usados/anulados o de eventos cancelados/finalizados (API-MVP 2) → operar en más de un evento del mismo organizador sin cuenta nueva (API-MVP 3).

**Reglas indispensables para que el recorrido no mienta en una demo**: máquina de estados de evento real con `FechaInicio`/`FechaFin` (API-MVP 1), compra restringida a eventos publicados antes de que empiecen (API-MVP 2), validación que cubre todo el curso del evento hasta `FechaFin` (API-MVP 2), cancelación que efectivamente bloquea compra y validación sin batch-update (API-MVP 2), sin sobreventa demostrada con test de concurrencia (API-MVP 2), asignación de Control a más de un evento (API-MVP 3), recorrido completo encadenado verificado con el emulador (API-MVP 4).

---

## 10. Capacidades postergadas (explícitamente fuera de este roadmap)

- Pagos reales (procesador de pago, cobro efectivo).
- Reservas temporales de stock antes de confirmar compra.
- Reventa de tickets entre clientes.
- QR firmado / validación offline avanzada.
- Notificaciones (push/email) al cliente u organizador.
- Analíticas de organizador (dashboards de ventas, asistencia).
- Merchandising.
- Migraciones a base de datos productiva / infraestructura de despliegue.
- Optimizaciones de performance sin volumen medido.
- Auditoría de dominio (`security_audits`-equivalente) para compras, validaciones y cambios de estado de evento — hoy solo hay `ILogger` estructurado, suficiente para diagnóstico manual en el MVP pero no para trazabilidad formal de disputas.
- Aprobación administrativa de eventos antes de publicar.
- Transición `Finalizado` persistida vía job/scheduler (se mantiene derivada indefinidamente salvo decisión futura en contrario).
- Ventana de "apertura de puertas" configurable antes de `FechaInicio` para habilitar la validación anticipadamente (decisión explícita: la validación ya está habilitada desde la publicación, sin ventana adicional que agregar).
- Endpoint de "desasignar" Control de un evento.
- Edición de un evento después de publicado (ni siquiera parcial), incluida edición incremental de un tipo de ticket individual por id; para corregirlo hay que cancelar y volver a crear.
- Recuperación de contraseña vía UI del frontend.
- `[EmailAddress]`/longitud mínima de password en DTOs de alta privilegiada y limpieza de `LoggingMiddleware` — movidos a la etapa **Entrega**, no bloqueantes para ninguna etapa API-MVP.

---

## 11. Contradicciones de la revisión anterior — cerradas en esta revisión

La revisión anterior de este documento dejó cinco contradicciones señaladas para confirmación del dueño del producto. Las decisiones recibidas en esta revisión (duración del evento con `FechaInicio`/`FechaFin`, tabla de transiciones exacta, edición de tipos de ticket, rutas cerradas, filtro de vigencia en búsqueda y detalle) las cierran todas:

1. **Definición de "vigente" no era única** → cerrada: §0.1 fija tres vigencias distintas y explícitas (catálogo/detalle, compra, validación), cada una con su condición exacta sobre `FechaInicio`/`FechaFin`. Ya no depende de una interpretación de este documento.
2. **Transiciones de estado no venían especificadas letra por letra** → cerrada: tabla exacta en §1, incluida la regla de que `Publicado → Cancelado` es válida **solo mientras el evento no esté finalizado** (`UtcNow <= FechaFin`); un evento ya `Finalizado` es terminal y rechaza la cancelación (y cualquier otra transición) con excepción tipada y HTTP 409.
3. **Edición de tipos de ticket (`TicketGroups`) durante `Borrador`** → cerrada: confirmado en §0 (decisión 6) y §2 que la edición en `Borrador` incluye los tipos de ticket, con reemplazo completo de la colección vía `EventUpdateRequest`.
4. **Alcance de "vigente" en `SearchEventsAsync`** → cerrada: §0.1 aplica la misma vigencia de catálogo a búsqueda y detalle público.
5. **Ruta y verbo exactos de los endpoints nuevos** → cerrada: `GET /api/events/organizer/{id}` y `POST /api/events/{eventId}/controls/{controlPersonaId}` quedan fijados en §2 y §4 respectivamente.

**No quedó ninguna decisión funcional abierta** tras esta revisión. Los únicos puntos no cerrados son detalles de nomenclatura interna sin impacto funcional (p. ej. los valores exactos del string `MotivoNoUtilizable`, o si la lógica de asignación de Control en API-MVP 3 termina viviendo en `UserService` o en `EventService`), que se resuelven en el momento de implementar cada etapa sin requerir una nueva decisión de producto.
