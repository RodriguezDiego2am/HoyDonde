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
| Frontend 4–5 | Pendiente | Administración avanzada de roles/acciones, filtros de eventos, reportes y QA final |

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

**Cierre**

El módulo de seguridad configurable se opera íntegramente desde la interfaz sin hardcodear relaciones rol→acción.

### Frontend 5 — Cierre

**Alcance**

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
