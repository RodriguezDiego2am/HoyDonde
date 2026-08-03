# Roadmap del MVP — HoyDonde?

> Documento operativo y fuente de estado del MVP.  
> Los contratos HTTP exactos viven en `API_Documentation.md`.  
> La arquitectura, comandos y reglas para modificar el repositorio viven en `CLAUDE.md`.  
> El historial detallado del refactor de seguridad vive en `docs/security-refactor-plan.md`.

Este archivo evita repetir DTOs completos, inventarios de archivos, pseudocódigo y listas exhaustivas de pruebas. Cada etapa conserva solamente alcance, reglas esenciales y una condición verificable de cierre.

---

## 1. Estado general

| Etapa | Estado | Resultado principal |
|---|---|---|
| Seguridad 0–6 | Cerrada | Persona, Usuario, roles, acciones y policies dinámicas; modelo legacy retirado |
| API-MVP 1 | Cerrada | Eventos, fechas, estados, privacidad y DTOs |
| API-MVP 2 | Cerrada | Compra, stock, tickets y validación transaccional |
| API-MVP 3 | Cerrada | Un Control puede asignarse a varios eventos propios |
| API-MVP 4 | Cerrada | Errores uniformes, documentación y recorrido HTTP integral |
| Puente backend–frontend | Cerrado | Enums textuales y `acciones` en `/api/auth/sync`; suite: 360/360 |
| API-MVP 5 | Cerrada | Consultas operativas de controles y eventos asignados sin ids copiados a mano; suite: 391/391 |
| Frontend 0 | Cerrada | Expo 54 + Firebase Auth real + `/api/auth/sync`; validado a mano en dispositivo físico; suite: 396/396 |
| Frontend 1 | Cerrada | Catálogo, compra de demostración y Mis entradas con QR; validado a mano en Expo Go contra API/Firestore reales |
| Frontend 2 | Cerrada (circuito mínimo) | Alta de Organizador, ciclo de vida de eventos y alta/asignación de Control; validado a mano en Expo Go |
| Frontend 3 | Cerrada | Validación de Control por QR (`expo-camera`) e ingreso manual, verificada end-to-end con cámara física, API y Firestore reales; recorrido real de los cuatro perfiles (Admin, Organizador, Cliente, Control) completado |
| Frontend 4 | Cerrada | Administración de roles/acciones/usuarios operable desde la interfaz por acción efectiva; modificación de permisos verificada a mano (quitar/reponer `EVENTO_CREAR`) |
| Frontend 5 | Cerrada | Filtros de Cartelera cerrados; módulo de reportes (§11) cerrado: reporte propio del Organizador, reporte global y auditoría de seguridad del Administrador, con pantallas y exportación a PDF (`expo-print`/`expo-sharing`) |

Al cerrar una etapa se actualizan únicamente esta tabla, su breve resultado y la última verificación. No se conserva un diario de implementación dentro de este archivo.

---

## 2. Reglas funcionales que no deben romperse

### Seguridad

- Firebase autentica; HoyDonde autoriza.
- Flujo: UID de Firebase → IdentidadExterna → Usuario → Persona → Rol → Acción → Policy.
- El frontend recibe `usuarioId`, `personaId`, `roles` y `acciones` desde `POST /api/auth/sync`.
- Los roles son configurables. La interfaz usa acciones para mostrar funciones; el backend siempre decide si están autorizadas.
- Solo Cliente se autorregistra. Admin y Organizador los crea un Admin; Control lo crea un Organizador.
- Ningún contrato del dominio expone UID de Firebase ni `ExternalSubjectId`.

### Eventos

- Estados persistidos: `Borrador`, `Publicado`, `Cancelado`.
- `Finalizado` es derivado: `Publicado && UtcNow > FechaFin`.
- Crear produce un Borrador; solo un Borrador puede editarse o publicarse.
- Un evento Cancelado o Finalizado es terminal.
- Catálogo y detalle público: `Publicado && UtcNow <= FechaFin`.
- Compra: `Publicado && UtcNow < FechaInicio`.
- Validación: `Publicado && UtcNow <= FechaFin`.
- El Organizador solo modifica eventos propios, identificado por `PersonaId`.

### Tickets

- Compra y descuento de stock ocurren en una transacción Firestore.
- No puede existir sobreventa.
- El ticket conserva una fotografía de nombre del evento, tipo, precio y fechas de compra.
- Cancelar un evento no reescribe tickets en lote.
- `Utilizable` y `MotivoNoUtilizable` se calculan desde el ticket histórico y el estado actual del evento.
- Validar consume el ticket atómicamente; un segundo intento se rechaza.

### Controles

- La relación Control↔Evento es N:N mediante `ControlAsignacion`.
- Un Organizador crea controles para eventos propios y puede reutilizarlos en otros eventos propios.
- Un Control solo valida en eventos a los que está asignado.
- Las consultas operativas reutilizan `CONTROL_CREAR` y `TICKET_VALIDAR`; no se agrega una acción 21.

---

## 3. Backend completado

### API-MVP 1 — Eventos

Implementado:

- `FechaInicio`/`FechaFin`, validaciones y máquina de estados.
- Edición completa de tipos de ticket únicamente en Borrador.
- Catálogo/detalle públicos y consultas privadas del Organizador.
- DTOs en todos los endpoints; ningún modelo Firestore se devuelve directamente.
- Paginación y filtros server-side con índices declarados.

### API-MVP 2 — Tickets

Implementado:

- Compra y stock transaccionales.
- Fotografía inmutable del ticket.
- Consulta de tickets propios sin N+1.
- Validación transaccional, ownership y rechazo de reutilización.
- Estados derivados para tickets no utilizables.

### API-MVP 3 — Asignación de controles

Implementado:

- Alta de Control por Organizador.
- Asignación idempotente de un Control existente a otro evento propio.
- Ownership por PersonaId y concurrencia cubierta.

### API-MVP 4 — Cierre contractual

Implementado:

- Error uniforme `{ code, message, traceId, errors?, detail? }`.
- `ExceptionMiddleware` como único mapeador HTTP.
- DTO de tipos de ticket con id generado por el servidor.
- Recorrido integral Organizador → Cliente → Control contra Firestore Emulator.
- `API_Documentation.md` actualizado como referencia de contratos.

### Puente backend–frontend

Implementado:

- Enums HTTP serializados como texto y números rechazados.
- `/api/auth/sync` devuelve acciones efectivas, únicas y ordenadas.
- Suite completa verificada: 360 passed, 0 failed, 0 skipped.

---

## 4. API-MVP 5 — Consultas operativas de Control

**Estado:** cerrada e implementada. Último agregado backend antes del frontend; satisface la dependencia declarada en Frontend 2 y Frontend 3 (§7).

### Objetivo

Evitar que Organizador y Control deban copiar `PersonaId` o `EventId` manualmente.

### Endpoints

- `GET /api/events/organizer/controls` — controles del ámbito del Organizador (`CONTROL_CREAR`) → `ControlResumenResponseDto[]`.
- `GET /api/events/{eventId}/controls` — controles asignados a un evento propio (`CONTROL_CREAR`) → `ControlAsignadoResponseDto[]`.
- `GET /api/events/control/me` — eventos asignados al Control autenticado (`TICKET_VALIDAR`) → `EventoAsignadoResponseDto[]`.

### Reglas

- Actor resuelto exclusivamente desde el token y convertido a PersonaId.
- Un Organizador nunca consulta datos de otro Organizador.
- Un Control nunca consulta asignaciones de otro Control.
- Resultados vacíos devuelven `200 []`.
- Listas sin duplicados y con orden determinístico.
- Controles inactivos siguen visibles para el Organizador con estado inactivo.
- Responses mínimos: no UID, `ExternalSubjectId`, `UsuarioId`, DNI, teléfono, roles completos, stock ni precios.
- Consultas por filtros simples y resolución batch de relaciones (`WhereIn`/lectura batch de eventos); evitar N+1.

### Cierre

- Los tres endpoints funcionan por HTTP con policies y ownership reales, verificados contra Firestore Emulator.
- Casos propio/ajeno/inexistente/vacío/inactivo/múltiple cubiertos.
- Catálogo de seguridad permanece en 20 acciones.
- `dotnet build` sin errores; suite completa contra Firestore Emulator real: **391 passed, 0 failed, 0 skipped**.
- Riesgo aceptado: la primera corrida tuvo un lock timeout aislado en un test de concurrencia preexistente de API-MVP 3 (contención del emulador bajo carga paralela, no relacionado con este cierre); la segunda corrida quedó completamente verde.
- Riesgo aceptado: la resolución batch usa `WhereIn` sin chunking ni paginación, suficiente para el volumen esperado del MVP.

---

## 5. Decisiones técnicas del frontend

- Expo + Expo Router + TypeScript.
- Firebase JavaScript SDK modular; compatible con Expo Go.
- Persistencia nativa mediante Firebase Auth + AsyncStorage. En web se usa persistencia web compatible.
- Nunca se guarda manualmente el ID token.
- Axios obtiene `currentUser.getIdToken()`; ante 401 refresca una sola vez. Un 403 no cierra la sesión.
- Catálogo y detalle funcionan sin login.
- Todo perfil autenticado ejecuta `/api/auth/sync` para cargar su sesión local.
- El registro de Cliente coordina `createUserWithEmailAndPassword` y `/api/auth/sync` para impedir un sync vacío concurrente.
- `EXPO_PUBLIC_API_URL` y variables `EXPO_PUBLIC_FIREBASE_*`; ninguna credencial administrativa en el frontend.
- Tipos TypeScript explícitos; evitar `any` en contratos.
- Los 20 códigos de acción se centralizan como constantes. Nunca se replica rol→acción en el cliente.
- Los detalles/formularios viven fuera de `(tabs)`; el grupo de tabs contiene solo accesos principales.
- `lint.json` no se elimina sin autorización del usuario.

### Rutas objetivo

```text
app/
  index.tsx                      -> cartelera pública
  login.tsx
  register.tsx                  -> solo Cliente
  events/[id].tsx
  (tabs)/
    index.tsx                    -> Cartelera
    tickets.tsx                 -> Cliente
    organizer.tsx               -> Organizador
    control.tsx                 -> Control
    admin.tsx                   -> Administrador
    explore.tsx                 -> Perfil
  organizer/events/new.tsx
  organizer/events/[id]/index.tsx
  organizer/events/[id]/edit.tsx
  organizer/events/[id]/controls.tsx
  admin/altas.tsx
  admin/roles.tsx
  admin/usuarios.tsx
```

Las tabs protegidas se muestran si el usuario posee alguna acción del área. Cada botón verifica su acción exacta y siempre maneja un eventual 403 del backend.

---

## 6. Dirección visual — “Cartelera urbana”

HoyDonde? no debe parecer una plantilla genérica de eventos ni un dashboard generado automáticamente. La identidad combina cartel cultural urbano, afiche impreso y entrada física.

### Principios

- Jerarquía editorial: títulos grandes, fechas protagonistas y composición asimétrica controlada.
- Superficies tipo papel, bordes firmes y sombras cortas; evitar tarjetas flotantes idénticas.
- Cada categoría genera un póster visual determinístico mediante color, tipografía y formas. No se requieren imágenes remotas para que el catálogo tenga identidad.
- Tickets inspirados en entradas impresas: numeración, perforación visual y QR.
- Estados expresados con texto, forma y color; nunca solo con color.
- El área Control usa un “modo puerta”: alto contraste, controles grandes y resultado inequívoco.
- Organizador y Administrador usan una variante “backstage”: más densa y operativa, pero con los mismos tokens.

### Paleta inicial

| Token | Valor | Uso |
|---|---|---|
| Papel | `#F3EBDD` | Fondo principal |
| Tinta | `#171512` | Texto y bordes |
| Tomate | `#F04E3E` | Acción primaria / Música |
| Cobalto | `#3454D1` | Tecnología / enlaces |
| Lima | `#C8F25B` | Sellos y acentos |
| Arena | `#D8CDBB` | Superficies secundarias |
| Éxito | `#167A50` | Validación correcta |
| Error | `#C83737` | Rechazo y errores |

La paleta se valida por contraste antes de cerrar Frontend 0.

### Tipografía

- Familia principal: **Archivo**, disponible para Expo.
- Pesos altos para afiches/títulos; pesos regulares para formularios y lectura.
- Números y fechas pueden usar ancho condensado si la familia cargada lo permite.

### Componentes visuales base

- `PosterCard`: evento como afiche, variación determinística por categoría/id.
- `TicketStub`: ticket físico con QR y estado.
- `StatusStamp`: sello para Borrador/Publicado/Cancelado/Finalizado y estados del ticket.
- `ActionButton`: botón de tinta con desplazamiento corto al presionar.
- `FormField`: input editorial accesible, sin cajas genéricas excesivamente redondeadas.
- `GateResult`: resultado de validación a pantalla completa, legible a distancia.

### Evitar

- Gradientes violeta/azul, glassmorphism y neón como identidad principal.
- Tarjetas blancas indistinguibles con sombra difusa.
- Cuadrículas de iconos de colores tipo dashboard genérico.
- Emojis como sistema visual.
- Animaciones ornamentales que retrasen compra o validación.
- Copiar una aplicación de referencia pantalla por pantalla.

La identidad se aplica durante Frontend 0; no requiere otro documento extenso ni una etapa separada.

---

## 7. Etapas del frontend

### Frontend 0 — Fundación

**Estado: cerrada.** Expo SDK 54 / React Native 0.81. Firebase Authentication real (proyecto `hoydonde-f5a05`) autentica desde el Firebase Client SDK; el backend verifica cada ID token con el Firebase Admin SDK (`FirebaseAuth.DefaultInstance.VerifyIdTokenAsync`, `HoyDonde.API/Authentication/FirebaseAuthenticationHandler.cs`) y solo lee UID/email — nunca un rol del token. `/api/auth/sync` provisiona/recupera Persona+Usuario y devuelve roles/acciones efectivas desde Firestore.

**Alcance**

- Firebase, persistencia, AuthContext y `/api/auth/sync`.
- Cliente HTTP tipado, errores uniformes y reintento 401 controlado.
- Rutas públicas y protegidas.
- Login, registro de Cliente, Perfil y logout.
- Tokens visuales y componentes base de “Cartelera urbana”.
- Adaptación cuidadosa de los cambios frontend preexistentes.

**Cierre — validación manual real completada** (dispositivo físico, Expo Go, API y Firestore reales, no emulador):

- Expo Go SDK 54 corre en dispositivo físico contra la API real, arrancada con `dotnet run --project .\HoyDonde.API --urls "http://0.0.0.0:5053"` y Expo en modo LAN (`npm run start:lan`).
- Cartelera pública consulta la API real y muestra correctamente el estado vacío sin sesión.
- El bootstrap (`bootstrap-admin`) creó el primer Administrador; su login funciona y la app muestra su email y rol.
- Registro de Cliente funciona end-to-end contra Firebase real.
- Persistencia de sesión, logout y un nuevo login vuelven a funcionar sin manipular tokens a mano.
- Firestore real y los índices desplegados (`firestore.indexes.json`, incluido el índice collection-group `roles.Activo`) resuelven las consultas de `/api/auth/sync` sin error.
- Ninguna llamada a endpoints legacy.
- TypeScript, ESLint y Jest verdes.
- Suite backend completa contra Firestore Emulator: **396 passed, 0 failed, 0 skipped**.

### Frontend 1 — Cliente

**Alcance**

- Catálogo paginado y detalle público.
- Compra de entradas.
- Mis tickets, utilizabilidad y motivo.
- QR sin firma con `ticketId` + `eventId`, acompañado por ids legibles como fallback.

**Cierre — validado a mano.** Un Cliente se registra, ve el evento publicado, compra una entrada de demostración y la consulta con su QR desde la aplicación, sin manipular tokens ni ids internos (Expo Go, API y Firestore reales).

### Frontend 2 — Organizador

**Dependencia:** API-MVP 5 cerrada.

**Alcance**

- Eventos propios: crear, editar Borrador, publicar y cancelar.
- Reemplazo completo de tipos de ticket en Borrador.
- Alta de Control y asignación de uno existente mediante selección visual.
- Consulta de controles propios y asignados al evento.

**Cierre — validado a mano (circuito mínimo).** Un Organizador crea, edita, publica y cancela su evento, y crea/asigna Control sin copiar PersonaId (Expo Go, API y Firestore reales). Administración avanzada de seguridad queda en Frontend 4.

### Frontend 3 — Control

**Dependencia:** API-MVP 5 cerrada.

**Alcance**

- Experiencia exclusiva para una cuenta cuyas acciones efectivas son únicamente `TICKET_VALIDAR` (decidido por acción, nunca por rol): sin Cartelera, Mis entradas, Perfil ni barra de tabs.
- Escaneo QR con `expo-camera`, reutilizando el payload `{ticketId, eventId}` de Frontend 1.
- Ingreso manual como alternativa siempre disponible, con el mismo servicio y lock anti-repetición que el escáner.
- Resultado claro para éxito, usado, anulado, evento cancelado/finalizado, falta de permiso o ticket/evento inexistente; la API decide siempre, nunca el cliente.

**Cierre — validado a mano.** Con cámara física, Firebase, API y Firestore reales: una cuenta Control exclusiva es redirigida a su interfaz específica y no muestra Cartelera/Mis entradas/Perfil/tabs; escanea el QR real de un ticket y la primera lectura lo valida y consume; la segunda lectura del mismo QR se rechaza como ya utilizada; el ingreso manual valida otro ticket y su repetición también se rechaza como ya utilizado; logout y navegación funcionan.

**Riesgo aceptado:** el ingreso manual exige `ticketId`/`eventId` completos (largos). Mejora futura evaluada, no implementada: un `CodigoValidacion` corto, único y generado por backend.

### Frontend 4 — Administrador

**Alcance**

- Alta de Administrador y Organizador.
- Usuarios, roles, acciones y permisos efectivos.
- Asignar/quitar roles y acciones.
- Activar/desactivar usuarios.
- Mostrar correctamente el rechazo del guard del último Administrador.

**Cierre — implementado y validado a mano.** Administración de roles, acciones y usuarios operada íntegramente desde la interfaz (`/admin`, `/admin/altas`, `/admin/roles`, `/admin/usuarios`), sin hardcodear relaciones rol→acción. Verificado contra Firebase/API/Firestore reales: un Admin le quitó `EVENTO_CREAR` al rol `ORGANIZADOR`, el Organizador actualizó permisos (`refreshSessionPermissions`) y perdió "Crear evento"; se lo reasignaron y, tras actualizar de nuevo, la opción volvió a aparecer.

Reportes/analíticas y el QA final (Frontend 5) siguen pendientes; los filtros de Cartelera (Frontend 5) ya cerraron — ver nota debajo.

### Frontend 5 — Cierre

**Filtros de Cartelera — implementado y validado a mano.** `GET /api/events` acepta `fechaDesde`/`fechaHasta` (UTC, Desde inclusiva/Hasta exclusiva sobre `Event.FechaInicio`), `categoria` y `ubicacion` (exacta), combinables y paginados junto con `lastEventId`/`limit`, todos aplicados en la propia consulta de Firestore antes del cursor (ver `API_Documentation.md` §7). Verificado en Expo Go contra API y Firestore reales: cartelera pública sin sesión, rango Desde/Hasta con límites correctos, solo Desde, solo Hasta, categoría, ubicación exacta, combinación rango+categoría+ubicación, rango inválido rechazado antes de llamar a la API, estado vacío filtrado, limpiar filtros, indicador de filtros activos, refresh conservando filtros, sin errores de índices (los cuatro índices compuestos existentes ya cubrían la combinación). Reportes/PDF quedan como siguiente bloque de Frontend 5.

**Alcance (resto de Frontend 5, pendiente)**

- Estados loading/vacío/error/403 en todas las pantallas.
- Accesibilidad, validación de formularios y limpieza del scaffold Expo.
- Configuración documentada para web, emulador Android y dispositivo físico.
- Recorrido real por Admin, Organizador, Cliente y Control.
- Actualización final de `API_Documentation.md` y `CLAUDE.md` solo si el estado real cambió.

**Cierre**

- `tsc`, ESLint y Jest verdes.
- Suite backend con emulador verde.
- Los cuatro recorridos funcionan sin manipulación manual de tokens ni reinstalar la app.

---

## 8. Criterio de calidad proporcional al MVP

Cada etapa debe tener:

- pruebas unitarias para lógica con riesgo real;
- pruebas de integración para contratos críticos;
- una comprobación manual del flujo visible;
- build/lint/typecheck en verde.

No se exige:

- cubrir combinaciones teóricas sin impacto funcional;
- documentar cada archivo modificado dentro de este roadmap;
- rediseñar arquitectura cuando una solución local y testeable alcanza;
- repetir la suite completa varias veces si la primera corrida final queda verde.

Una etapa se revisa una vez. Si cumple su flujo, no filtra datos, respeta autorización y deja las pruebas verdes, se cierra y se avanza.

---

## 9. Dependencias del usuario

Antes de cerrar Frontend 0 se necesita:

- aplicación web registrada en el mismo proyecto Firebase que usa la API;
- Email/Password habilitado en Firebase Authentication;
- valores públicos `EXPO_PUBLIC_FIREBASE_*`;
- `EXPO_PUBLIC_API_URL` correcto para el entorno usado.

La cuenta de servicio del backend nunca se copia al frontend.

---

## 10. Fuera del MVP

- Pagos reales y reservas temporales.
- Reventa.
- QR firmado o validación offline.
- Recuperación de contraseña desde la UI.
- Notificaciones y analíticas.
- Merchandising.
- Aprobación administrativa de eventos.
- Edición posterior a publicación.
- Desasignar controles.
- Migración/despliegue productivo y optimizaciones sin volumen medido.
- Jobs para persistir `Finalizado`.

Estas capacidades solo se incorporan mediante una decisión nueva de producto; no deben aparecer como mejoras espontáneas durante las etapas del MVP.

### Bajas lógica y física (evaluar por tipo de entidad)

No toda entidad necesita las dos formas de baja: la decisión es caso por caso, según integridad referencial, auditoría y reglas de negocio — nunca una regla uniforme ("todo tiene baja física" o "todo es solo lógico").

- **Roles personalizados: implementado — ver §12.** Baja física solo si el rol no está asignado a ningún usuario (ni activa ni inactivamente) y no es uno de los 4 roles base sembrados (`ADMINISTRADOR`/`CLIENTE`/`ORGANIZADOR`/`CONTROL`).
- **Eventos:** baja física solo para un `Borrador` sin tickets emitidos ni Control asignado. Con historial (compras, validaciones), la baja es lógica: cancelar/archivar, nunca borrar el documento.
- **Usuarios:** desactivar (`Usuario.IsActive = false`) sigue siendo la operación ordinaria (ya implementada). Una baja física exigiría coordinar Firebase Auth, `IdentidadExterna`, `Persona`, `UsuarioRol` y toda referencia de dominio (tickets, eventos, `ControlAsignacion`) — no se implementa sin ese análisis.
- **Tickets y `security_audits`:** conservan su historial siempre; sin baja física ordinaria.
- **`ControlAsignacion` u otras asociaciones:** una eventual baja física transaccional con su propio registro de auditoría queda para analizar más adelante.

Cualquier baja física que se implemente en el futuro necesita, como mínimo: confirmación reforzada, verificación de referencias antes de borrar, autorización explícita, registro de auditoría y tests dedicados — nunca un botón "Eliminar" simple. La pasarela de pago real y el `CodigoValidacion` corto (§7, Frontend 3) siguen igual de pendientes que antes de esta nota.

---

## 11. Módulo de reportes — diseño técnico

Verificado contra el código real (Event/Ticket/TicketType/SecurityAudit/Usuario/Persona/Rol/Accion, servicios, repositorios, `firestore.indexes.json`, `Acciones.cs`, `SecurityCatalogSeeder`, `Program.cs`) al cierre de Frontend 5 (filtros de Cartelera). **Primer checkpoint backend implementado: §11.5 (acciones/comando) y el endpoint Organizador de §11.3 (`GET /api/reports/organizer/events`).** El resto de esta sección (reporte Admin, auditoría de seguridad, PDF/frontend) sigue sin implementar — ver §11.10.

### 11.1 Alcance cerrado

Tres reportes JSON, los tres aprobados para diseño (nunca PDF desde el backend):

- **A. Administrador — actividad global de eventos.** Filtros: `fechaDesde`/`fechaHasta` (sobre `Event.FechaInicio`), `estado`, `categoria`, `organizadorPersonaId` (opcional, arbitrario — solo admin).
- **B. Organizador — rendimiento propio.** Mismos filtros de fecha/estado/categoria, más `eventId` opcional (ownership verificado server-side) y `ticketTypeId` opcional (requiere `eventId`, acota desglose y métricas a ese tipo). El organizador nunca se acepta del cliente: sale siempre de `IAuthenticatedPersonaResolver`.
- **C. Administrador — auditoría de seguridad. Aprobado para el primer corte** con filtros por fecha, operación, actor (`actorUsuarioId`) y objetivo (`targetTipo` ∈ `Rol`/`Usuario`/`RolAccion`). El filtro específico "por Acción" queda **postergado** (§11.8) y `SecurityAudit` **no se amplía ahora**. Los datos históricos nunca se completan ni se inventan retroactivamente para calzar con un filtro que no existía cuando se escribieron.

Rango UTC: Desde inclusiva, Hasta exclusiva. El frontend resuelve día local → UTC (mismo patrón que Cartelera, `utils/datetime.ts`). **A/B:** rango obligatorio, sin default, máximo **366 días** (`ReporteRangoInvalidoException` → 400) — agregan sobre todos los tickets de los eventos en rango, costo real de lectura. **C:** rango opcional con **default 30 días** y **máximo 366 días** — aunque el volumen esperado de `security_audits` es bajo, igual se fija un máximo para no dejar sin cota una lectura si se pasa un rango explícito enorme.

Ningún reporte usa "recaudación" ni "cobrado": el MVP no procesa pagos reales (§10). El monto de A/B se llama **"importe emitido"**, con aclaración textual explícita en la respuesta y en el PDF.

### 11.2 Viabilidad de métricas (A y B)

| Métrica | Fuente | Fórmula | Viable | Nota |
|---|---|---|---|---|
| Cantidad de eventos | `Event` en rango/filtros | `COUNT(Event)` | Sí | Rango sobre `FechaInicio`, igual campo que Cartelera. |
| Entradas emitidas | `Ticket.EventoId` | `COUNT(Ticket)` por evento | Sí | Cada `Ticket` existe únicamente si se emitió; no hay estado "reservado". |
| Entradas usadas | `Ticket.Estado` | `COUNT(Estado==Usado)` | Sí | Persistido, nunca reescrito por cancelación (§Tickets). |
| Entradas anuladas | `Ticket.Estado` | `COUNT(Estado==Anulado)` | Parcial | `Anulado` es un valor de enum sin ningún flujo real que lo escriba hoy (verificado: ningún servicio/endpoint transiciona un ticket a `Anulado`). El reporte debe mostrar el conteo real (hoy siempre 0) y no debe inventar una funcionalidad de anulación que no existe. |
| Entradas pendientes (no usadas, no anuladas) | `Ticket.Estado` | `COUNT(Estado==Emitido)` | Sí | Distinta de "anuladas": no mezclar ambas en una sola cifra (pedido §3/§4). |
| Stock disponible | `TicketType.CantidadDisponible` | `SUM` por evento/tipo | Sí, con matiz | Es stock **restante**, no capacidad inicial: solo decrece en `BuyTicketsAsync`, nunca se repone (no hay flujo de reposición ni de anulación-repone-stock). |
| Capacidad inicial | `Event.CapacidadMaxima` | Snapshot tomado en `CreateEventAsync`/`UpdateEventAsync` | Sí, a nivel evento | Es la única fuente confiable: se fija una sola vez y `UpdateEventAsync` solo corre en `Borrador` (antes de cualquier venta), así que nunca queda desincronizada. No existe un campo equivalente por `TicketType`; reconstruirla por tipo como `CantidadDisponible actual + COUNT(Ticket de ese TicketTypeId)` es matemáticamente correcto bajo el código actual (sin reposición), pero es una **derivación**, no un dato guardado — debe documentarse como tal, no como hecho persistido. |
| % Ocupación (venta) | `Emitidas / CapacidadMaxima` | — | Sí | Cuánto del inventario se vendió. |
| % Asistencia | `Usadas / Emitidas` | — | Sí | Cuánto de lo vendido efectivamente ingresó. Solo tiene sentido pleno tras `FechaFin`; se muestra igual para eventos en curso, aclarando que es parcial. |
| % Utilización | `Usadas / CapacidadMaxima` | — | Sí, complementaria | Combina venta + asistencia; se ofrece como dato adicional, nunca sustituye a los dos anteriores (pedido §4: no mezclar semánticas). |
| Importe emitido | `SUM(Ticket.PrecioPagado)` | — | Sí, con aclaración | `PrecioPagado` es fotografía inmutable tomada en la compra (nunca se recalcula); sumarlo es válido para "cuánto se emitió", nunca para "cuánto se cobró". |

Eventos cancelados/finalizados: sus tickets **no** se reescriben (§Tickets), así que el reporte usa siempre `Ticket.Estado` histórico, nunca `Utilizable`/`MotivoNoUtilizable` (esos son derivados para la UX del Cliente, no para auditoría). La fotografía de cada `Ticket` (`FechaInicio`/`FechaFin`/nombre/precio) nunca puede divergir del `Event` referenciado, porque `UpdateEventAsync` solo corre en `Borrador` y ningún ticket existe antes de `Publicado`; no hay caso real de "ticket histórico cuyo evento cambió sus fechas".

Cuando el reporte B recibe `ticketTypeId`, toda la tabla anterior se recalcula únicamente sobre tickets de ese tipo dentro del evento indicado: stock disponible, capacidad inicial derivada, ocupación, asistencia e importe emitido quedan acotados a ese tipo, y el desglose "por tipo de entrada" colapsa a una sola fila (la seleccionada) en vez de listar todos los tipos del evento.

### 11.3 Endpoints propuestos

- `GET /api/reports/admin/events` — policy `REPORTE_VER_GLOBAL`. Query: `fechaDesde`, `fechaHasta` (obligatorios), `estado?` (`Borrador`/`Publicado`/`Cancelado`; `Finalizado` se filtra sobre el estado efectivo, no el persistido), `categoria?`, `organizadorPersonaId?`. 400 `REPORT_RANGE_INVALID` si falta el rango o excede 366 días. Respuesta: resumen agregado + detalle por evento (y por tipo de entrada dentro de cada evento). Sin paginación: conjunto acotado por el rango. **Sin `eventId`:** este reporte es de actividad agregada por filtros, no de drill-down a un evento puntual — ese drill-down ya se logra combinando `organizadorPersonaId` con un rango de fecha ajustado a ese evento, sin sumar un parámetro nuevo ni una rama de código que salte toda la estrategia de rango/índice de §11.4.
- `GET /api/reports/organizer/events` — policy `REPORTE_VER_PROPIO`. Mismos query params de fecha/estado/categoria **sin** `organizadorPersonaId` (nunca aceptado del cliente; ownership vía `IAuthenticatedPersonaResolver`, igual que `GetByOrganizerIdAsync`), más `eventId?` (si no pertenece al organizador autenticado: `EventOwnershipException` → 403 `EVENT_OWNERSHIP`; si no existe: `EventNotFoundException` → 404, mismos códigos que `EventService`) y `ticketTypeId?` (solo válido junto con `eventId`; sin `eventId` es 400 `REPORT_VALIDATION_ERROR`; si no pertenece al evento, 404). Mismo DTO de respuesta que A, sin el campo `OrganizadorPersonaId` por evento (es siempre el propio).
- `GET /api/reports/admin/security-audits` — policy `REPORTE_VER_GLOBAL`. Query: `fechaDesde?`/`fechaHasta?` (default últimos 30 días si se omiten, máximo 366 días si se informan), `operacion?`, `actorUsuarioId?`, `targetTipo?` (`Rol`/`Usuario`/`RolAccion` — sin `Accion` independiente, §11.8), `targetId?` (match exacto, no substring). Respuesta: lista con `Timestamp`, `Operacion`, `ActorUsuarioId`, `ActorEmail` (resuelto en batch desde `Usuario`, nunca UID/`ExternalSubjectId`), `TargetTipo`, `TargetId`, `Detalle`.

DTOs nuevos mínimos (`ReporteEventosResponseDto`, `ReporteEventoDetalleDto`, `ReporteTicketTypeDetalleDto`, `SecurityAuditReporteDto`): nunca exponen modelos Firestore, UID, `ExternalSubjectId`, ni `PrecioPagado`/estado renombrados como "recaudación".

### 11.4 Consultas Firestore e índices

**Eventos — estrategia corregida.** La consulta de catálogo (`SearchEventsAsync`) fija `Estado==Publicado && FechaFin>=now`; el reporte necesita eventos en *cualquier* estado, así que es una consulta distinta, no una reutilización.

- **Admin sin `organizadorPersonaId`:** query server-side solo por rango de `FechaInicio` (Desde inclusiva, Hasta exclusiva), `orderBy(FechaInicio, id)` — índice automático de campo simple, sin índice compuesto. `Estado`/`Categoria` se filtran en memoria sobre ese conjunto ya acotado por fecha.
- **Admin con `organizadorPersonaId`:** se agrega `WhereEqualTo(OrganizadorPersonaId, valor)` a la misma query (equality + rango) — usa el índice compuesto declarado abajo. `Estado`/`Categoria` siguen en memoria.
- **Organizador:** `WhereEqualTo(OrganizadorPersonaId, personaIdResuelto)` es **siempre** parte de la query Firestore, nunca solo un filtro aplicado en memoria después de leer — el ownership queda acotado por la propia consulta, no depende únicamente de que el código de la app filtre bien después. Misma forma de query e índice que el caso anterior. `Estado`/`Categoria` en memoria.
- **Si `eventId` viene informado (solo B):** se salta la query por rango — lectura directa de `events/{eventId}`, se verifica `OrganizadorPersonaId` contra el actor resuelto (403/404 igual que `EventService.GetOwnedEventOrThrowAsync`) y se sigue directo a tickets. No usa ni necesita el índice compuesto.

**Índice compuesto nuevo propuesto (uno solo):**

```text
events: OrganizadorPersonaId ASC, FechaInicio ASC
```

Cubre tanto "Admin con organizador" como "Organizador": ambas son la misma forma de query (una equality + un rango, mismo `orderBy`). "Admin sin organizador" no lo necesita — un solo campo en rango ya cubierto por el índice automático de campo simple. **Se corrige la afirmación anterior de "cero índices nuevos": es un índice nuevo, no cero.**

**Tickets.** Con los `EventId` resultantes, `WhereIn("EventoId", chunk)` en lotes de ≤30 (límite real de Firestore) — round-trips = `ceil(cantidadEventos / 30)`, no uno por evento (mismo patrón que `GetEventosByIdsAsync`/`GetByPersonaIdsAsync`, ya usado en el código). Si además viene `ticketTypeId` junto con `eventId` (B), se agrega `WhereEqualTo(TicketTypeId, valor)` a la query de tickets de ese evento — equality-only sobre dos campos, sin índice nuevo (Firestore no requiere índice compuesto para queries de solo igualdad). `Estado` se sigue agregando en memoria sobre el lote ya acotado.

**Auditoría (C).** Sin cambios respecto al índice: `security_audits` no tiene ningún índice compuesto declarado hoy ni lo necesita. Rango obligatorio-con-default sobre `Timestamp` (índice automático de campo simple, orden por `Timestamp`+id) y `Operacion`/`ActorUsuarioId`/`TargetTipo`/`TargetId` como filtro en memoria — volumen esperado bajo (solo mutaciones administrativas), consistente con "conjunto acotado del período" en vez de paginación con cursor sobre esos filtros.

**Lecturas estimadas:** 1 query de eventos (o 1 lectura directa si viene `eventId`) + `ceil(N_eventos/30)` queries de tickets + resolución batch de `Usuario` para C (por `GetAllSnapshotsAsync` sobre IDs de actor distintos, mismo patrón que `GetEventosByIdsAsync`). Sin N+1 en ningún caso. Consistencia: lecturas no transaccionales (reportes, no invariantes de negocio) — una venta concurrente durante la generación puede quedar fuera o dentro según el momento exacto del snapshot; aceptable para un reporte, no para el flujo transaccional de compra/validación.

### 11.5 Acciones y seed — decisión final

`Acciones.cs`/`Program.cs` ya registran una policy por cada entrada de `Acciones.Todas` (`foreach (var accion in Acciones.Todas) options.AddPolicy(...)`), así que agregar `REPORTE_VER_GLOBAL`/`REPORTE_VER_PROPIO` a esa lista alcanza para las policies — sin tocar `Program.cs`. `Acciones.Todas` pasa de 20 a 22; ningún test hoy afirma el número 20 (se revisó `SecurityCatalogSeederTests`, `SecurityAdminControllerTests`; el "20" solo aparece en comentarios/documentación, que hay que actualizar: `Acciones.cs`, `API_Documentation.md`, `CLAUDE.md`).

**Hallazgo que condiciona el seed:** `SecurityCatalogSeeder.SeedAsync()` no corre en cada arranque de la API — solo se invoca desde `bootstrap-admin` (`Commands/BootstrapAdminCommand.cs`), que se niega a ejecutarse si ya existe un Administrador efectivo, y desde tests. Contra el Firestore real (`hoydonde-f5a05`) ya hay un Administrador, así que `SeedAsync()` **no volverá a correr nunca** en producción. Además, el loop de asignación Rol→Acción del seeder usa `IRolRepository.AssignAccionAsync` (Set crudo, sin auditoría) sobre **todos** los roles sembrados en cada corrida — si alguna vez se re-ejecutara completo, re-otorgaría silenciosamente cualquier acción que un Administrador hubiera revocado deliberadamente a un rol base.

**Instalaciones nuevas (dev/test/emulador):** `SecurityCatalogSeeder` se actualiza a **22 acciones** directamente: `AccionesIniciales` suma `REPORTE_VER_GLOBAL`/`REPORTE_VER_PROPIO`, y `AccionesPorRol` suma `REPORTE_VER_GLOBAL` a `ADMINISTRADOR` y `REPORTE_VER_PROPIO` a `ORGANIZADOR`. Es seguro ahí porque `SeedAsync()` corre sobre un catálogo recién creado, sin ninguna personalización previa que pisar.

**Firestore real ya existente:** un comando dedicado e idempotente, aparte de `bootstrap-admin` y de `SeedAsync()`, que **solo crea** los dos documentos `Accion` (`IAccionRepository.CreateAsync`, atrapa `AccionYaExisteException`) — **no asigna ninguna acción a ningún rol**. El Administrador las asigna después desde la UI (`/admin/roles`, flujo ya auditado de `/api/security`) a los roles que decida (naturalmente `ADMINISTRADOR`/`ORGANIZADOR`, pero queda a su criterio). Así, reejecutar el comando nunca repone una asignación que el Administrador haya quitado: el comando no vuelve a tocar asignaciones una vez creadas las Acciones.

**Orden operativo:** (1) ejecutar el comando dedicado → quedan creadas `REPORTE_VER_GLOBAL`/`REPORTE_VER_PROPIO`, sin efecto en ningún rol; (2) un Administrador las asigna desde `/admin/roles`; (3) cada sesión afectada corre `refreshSessionPermissions()` (o vuelve a loguearse) para que el reporte aparezca habilitado — mismo patrón ya verificado en el cierre de Frontend 4 (CLAUDE.md, quitar/reponer `EVENTO_CREAR`).

### 11.6 PDF (frontend)

Estrategia validada: API devuelve JSON puro; el frontend construye HTML propio y usa `expo-print` (`Print.printToFileAsync`) + `expo-sharing` (`Sharing.shareAsync`) — ambos compatibles con Expo SDK 54/Expo Go, sin dependencias nativas nuevas para instalar. Contenido mínimo: título, alcance (Admin/Organizador + filtros aplicados en texto legible), fecha de generación, resumen, detalle por evento/tipo, aclaración fija de pagos simulados, identidad visual "Cartelera urbana" (§6: tipografía Archivo, paleta Papel/Tinta/Tomate). Todo texto interpolado en el HTML debe escaparse (entidades HTML básicas) antes de insertarlo — el HTML lo genera el propio frontend, no un motor de plantillas con datos de terceros, pero nombres de evento/organizador vienen de datos de usuario y deben tratarse como no confiables igual. Android: `expo-print` genera el PDF en caché local, `expo-sharing` abre el selector nativo. iOS: mismo flujo. Web (Expo Web): `expo-print` no genera archivo descargable igual que en nativo — fallback razonable es abrir un `window.print()` o vista HTML imprimible, a decidir en implementación. Riesgo de volumen: un reporte con muchos eventos/tickets genera HTML grande → PDF lento o pesado en el dispositivo; el máximo de rango (366 días, §11.1) y el "conjunto acotado" ya mitigan esto, pero conviene un tope adicional de filas en el detalle antes de generar el PDF (a definir en implementación, no bloqueante para el diseño).

### 11.7 Pantallas propuestas

`/admin/reports` y `/organizer/reports`: selector de reporte (Admin: Eventos global / Auditoría; Organizador: solo Eventos propio), filtros (reutilizar `EventFilterPanel`/`SegmentedDateField` de Cartelera para fecha; nuevos selects para estado/categoría/organizador/evento/tipo de entrada/operación/actor), vista previa del resumen agregado antes de generar PDF, estados loading/empty/error explícitos, botón "Generar y compartir PDF". Los selects de `organizadorPersonaId` (Admin), `eventId`/`ticketTypeId` (Organizador) y `actorUsuarioId`/`targetId` (auditoría) siempre se muestran por nombre/email en la UI; el id viaja internamente en la request, nunca se pega a mano ni se expone en pantalla. Gate exclusivamente por `hasAccion(ACCIONES.REPORTE_VER_GLOBAL | REPORTE_VER_PROPIO)`, mismo patrón que `AdminHubScreen`/`OrganizerEventsListScreen`. Requiere agregar las dos entradas a `HoyDonde-frontend/constants/acciones.ts` (espejo de `Acciones.cs`).

### 11.8 Decisiones tomadas en esta iteración

- **Reporte C aprobado para el primer corte** con filtros por fecha, operación, actor (`actorUsuarioId`) y objetivo `Rol`/`Usuario`/`RolAccion`. El filtro específico "por Acción" queda **postergado**: no existe ningún endpoint que cree/edite una `Accion` por HTTP (solo se siembra al arrancar), así que nunca hay un audit con `TargetTipo=="Accion"`; lo más cercano es `Operacion ∈ {ROL_ASIGNAR_ACCION, ROL_QUITAR_ACCION}` con `TargetTipo=="RolAccion"` y `TargetId` empaquetado como `"{rol}/{accion}"` (string compuesto, no filtrable por accion sola sin *substring* en memoria). **`SecurityAudit` no se amplía ahora** para resolver esto; los audits históricos nunca se completan ni se inventan retroactivamente para calzar con un filtro que no existía cuando se escribieron.
- **Estrategia Firestore corregida** (§11.4): se declara **un** índice compuesto nuevo (`events: OrganizadorPersonaId ASC, FechaInicio ASC`), no cero. `Estado`/`Categoria` siguen filtrándose en memoria; el ownership del Organizador es siempre parte de la query Firestore, nunca solo un filtro posterior en memoria.
- **Seed resuelto** (§11.5): `SecurityCatalogSeeder` pasa a 22 acciones para instalaciones nuevas; contra el Firestore real ya existente, un comando dedicado crea únicamente las dos `Accion` y no toca ninguna asignación — el Administrador asigna desde la UI. Orden operativo documentado en §11.5.
- **Sin decisión pendiente, solo un hecho del código actual:** `Anulado` no tiene ningún flujo que lo escriba hoy; el reporte lo muestra en 0, sin implementar aquí una funcionalidad de anulación (fuera de alcance, ver también §10).

### 11.9 Etapas de implementación

1. `Acciones.cs` (20 → 22) + actualización de `SecurityCatalogSeeder` para instalaciones nuevas + comando dedicado para el Firestore real (§11.5) + actualizar `acciones.ts`/documentación del "20" → "22".
2. `GET /api/reports/organizer/events` (ownership obligatorio dentro de la query, `eventId`/`ticketTypeId` opcionales, índice compuesto nuevo, tests de fórmulas/límites/rango/chunking/ownership).
3. `GET /api/reports/admin/events` (mismos filtros de fecha/estado/categoría + `organizadorPersonaId` opcional, reusa el mismo índice cuando está presente, camino sin índice cuando no lo está).
4. `GET /api/reports/admin/security-audits` (auditoría básica: fecha con default/máximo, operación, actor, objetivo `Rol`/`Usuario`/`RolAccion`).
5. Pantallas `/organizer/reports` y `/admin/reports` + PDF (`expo-print`/`expo-sharing`), una vez el backend esté verde.

### 11.10 Checkpoint — estado real de implementación

- **Implementado y verificado — módulo completo:** paso 1 (`Acciones.cs` 20→22, `SecurityCatalogSeeder`, comando `seed-report-actions`), paso 2 (`GET /api/reports/organizer/events`), paso 3 (`GET /api/reports/admin/events`), paso 4 (`GET /api/reports/admin/security-audits`) y paso 5 (pantallas `/organizer/reports`, `/admin/reports` + `/admin/reports/events` + `/admin/reports/security-audits`, con exportación a PDF vía `expo-print`/`expo-sharing`).
- Suite backend contra Firestore Emulator real: **505 passed, 0 failed, 0 skipped** (2 tests de concurrencia no relacionados, `FirestoreControlAsignacionRepositoryTests`/`TicketServiceEmulatorTests`, son intermitentes bajo contención de la suite completa — verificados en verde de forma aislada).
- Suite frontend (`npm test`): **408 passed, 0 failed** — `npm run typecheck`, `npm run lint` (0 errores) y `npx expo-doctor` (18/18) también en verde.
- **Desviación respecto al diseño original:** el filtro `targetTipo` de la auditoría de seguridad admite un cuarto valor real, `UsuarioRol` (además de `Rol`/`Usuario`/`RolAccion`), porque `SecurityAdminService.AsignarRolAUsuarioAsync`/`QuitarRolDeUsuarioAsync` ya persisten ese `TargetTipo` — restringir el filtro a los tres valores originales lo habría dejado incapaz de filtrar la operación más frecuente de `/admin/usuarios` (asignar/quitar rol a un usuario).
- El reporte global de eventos del Administrador expone `OrganizadorPersonaId` por evento (`ReporteAdminEventoDetalleDto`); el frontend lo resuelve a email reutilizando `GET /api/security/usuarios` (acción `USUARIO_VER_PERMISOS_EFECTIVOS`) — si la sesión no tiene esa acción, el selector/columna de organizador simplemente no se puebla con nombre (el reporte sigue funcionando con el id).
- **Verificado a mano en Expo Go contra la API/Firestore reales:** reporte propio del Organizador, reporte global del Administrador, filtros de ambos, métricas coherentes, auditoría de seguridad y sus filtros, generación/apertura/compartido de los tres PDF, y confirmación de que Cliente y Control no reciben ningún acceso de reportes. Sin errores encontrados en el recorrido manual.
- **Con esto, el módulo de reportes (docs/api-mvp-plan.md §11) queda cerrado por completo** — backend, frontend y verificación manual. El índice compuesto y las dos acciones ya estaban desplegados/asignados en Firebase real antes de este cierre (ver arriba); esta etapa no volvió a tocarlos.

---

## 12. Baja lógica y física de roles — cerrado

Ver §10 ("Bajas lógica y física") para el criterio original de por qué solo los roles personalizados obtienen baja física. Implementado sobre `SecurityAdminController`/`SecurityAdminService`/`FirestoreRolRepository` existentes — ver CLAUDE.md, "Administración de roles" (o la sección equivalente) para el detalle operativo completo. Resumen:

- **Acción nueva:** `ROL_ELIMINAR` (`Acciones.cs`, 22 → 23 acciones). `SecurityCatalogSeeder` la asigna a `ADMINISTRADOR` solo para instalaciones nuevas. Comando dedicado para el Firestore real ya existente: `dotnet run --project HoyDonde.API -- seed-role-deletion-action` (crea únicamente la Accion, nunca la asigna, mismo patrón que `seed-report-actions`).
- **Baja lógica:** reutiliza `POST/desactivar` sobre `ROL_ACTIVAR` (`SetRolActivoAsync`) — sin mecanismo nuevo. `Rol.Activo = false` conserva rol y asignaciones; reactivable; idempotente; guard del último Administrador intacto.
- **Baja física:** `DELETE /api/security/roles/{codigo}`, policy `ROL_ELIMINAR`. Solo si el rol existe, no es uno de los 4 esenciales, está inactivo, y no tiene ninguna `UsuarioRol` (activa ni inactiva). Las cuatro condiciones se evalúan dentro de una única transacción Firestore (`FirestoreRolRepository.EliminarAsync`) que también borra la subcolección `roles/{codigo}/acciones` y escribe la auditoría — nunca borra la `Accion` del catálogo ni otros roles/usuarios. `security_audits` históricos se conservan siempre (solo se agrega una entrada nueva, `ROL_ELIMINAR`).
- **Colisión de nombres `roles`:** la colección raíz del catálogo y la subcolección `usuarios/{id}/roles` comparten nombre de colección (ya documentado desde la Etapa 2 del refactor de seguridad). La verificación de asignaciones usa una collection-group query sobre `roles` sin filtrar por `Activo` (a diferencia de `GetUsuarioIdsConRolActivoAsync`/`UltimoAdministradorGuard`, que sí filtran) y descarta explícitamente los documentos sin padre real bajo `usuarios` — mismo criterio, ver comentarios en el código.
- **Carrera asignación/eliminación:** `FirestoreUsuarioRepository.AsignarRolAsync` ahora también lee el documento `Rol` **dentro** de su propia transacción (no solo el chequeo previo no transaccional de `SecurityAdminService`), para que Firestore serialice correctamente contra una `EliminarAsync` concurrente sobre el mismo documento. Verificado con un test de concurrencia real contra el emulador: nunca queda una asignación huérfana apuntando a un rol borrado.
- **Excepciones tipadas** (409): `RolProtegidoException` (`ROL_PROTEGIDO`), `RolDebeEstarInactivoException` (`ROL_DEBE_ESTAR_INACTIVO`), `RolTieneUsuariosAsignadosException` (`ROL_TIENE_USUARIOS_ASIGNADOS`). 404 reutiliza `RolNoEncontradoException`/`ROLE_NOT_FOUND` existente.
- **Frontend:** `RolDetailScreen` — "Dar de baja lógica"/"Reactivar rol" (antes "Desactivar rol"/"Activar rol"), explicación breve de que la baja lógica conserva historial/asignaciones, y una "Zona de peligro" gateada exclusivamente por `hasAccion(ACCIONES.ROL_ELIMINAR)` (nunca por rol del actor): nota discreta para los 4 roles esenciales, nota de "primero dalo de baja lógica" para un rol personalizado activo, y el botón "Eliminar definitivamente" (con confirmación explícita, doble-envío bloqueado por estado) solo para un rol personalizado inactivo. Al eliminar con éxito, navega de vuelta a `/admin/roles`, que se refresca automáticamente (mismo patrón de refresco por foco ya existente en esa pantalla).
- **Estado:** backend **534 passed, 0 failed, 0 skipped** (suite completa, emulador real, incluye el test de concurrencia); frontend **416 passed** (`npm test`), `npm run typecheck`/`npm run lint` limpios, `npx expo-doctor` 18/18, `npx expo export --platform android` exitoso. El comando `seed-role-deletion-action` ya se ejecutó contra el Firestore real (`hoydonde-f5a05`), `ROL_ELIMINAR` quedó asignada manualmente a `ADMINISTRADOR` ahí, y la baja física de un rol personalizado se validó manualmente con éxito contra ese proyecto real. Cualquier otra instalación existente (no nueva) todavía debe correr ese mismo comando y asignar la acción antes de poder usar esta función.
