# Documentación de la API HoyDonde

## 1. Visión General
Esta API (HoyDonde.API) está construida utilizando ASP.NET Core Web API. Su propósito principal es la gestión de eventos y la venta/validación de tickets para los mismos. Se integra fuertemente con **Firebase** en dos aspectos fundamentales:
- **Autenticación y Autorización:** Utiliza Firebase Auth para las credenciales de usuarios y la gestión de roles a través de *Custom Claims*. Los validadores JWT en .NET verifican estos tokens.
- **Base de Datos:** Utiliza Google Cloud Firestore (Base de datos NoSQL de documentos) para almacenar usuarios, eventos y tickets.

El proyecto está estructurado usando una arquitectura limpia clásica:
- **Controllers:** Interfaz HTTP que recibe peticiones y orquesta respuestas.
- **Services:** Lógica de negocio (Eventos, Tickets, Usuarios).
- **Repositories:** Manejo directo de acceso a datos (particularmente Firestore).
- **Models / DTOs:** Entidades de domino local y objetos para la comunicación HTTP.

---

## 2. Resultado del Debugging y Tests
Durante la inspección y ejecución de `dotnet test` y `dotnet build`:
- **El proyecto compila exitosamente** (0 errores, 0 warnings).
- **Las pruebas unitarias pasan sin errores**.
- **Observaciones Importantes de la Implementación:**
  1. Se utiliza adecuadamente el mecanismo de transacciones atómicas (`_firestore.RunTransactionAsync`) en la compra de tickets para evitar inconsistencias de inventario en ventas simultáneas.
  2. Al autenticar un usuario en `api/auth/sync`, si el usuario no existía o falta en la base local (Firestore), este es guardado en la base de datos por defecto como un rol `Organizador` (en el `AuthService.cs`). Esto puede ser un comportamiento deseado o no, pero requiere atención en el flujo de negocio.
  3. Las fechas de Firestore pueden dar errores de deserialización si no se sincronizan correctamente en formato UTC; la API ya hace un casting adecuado con `.ToUniversalTime()`.

---

## 3. Arquitectura de Usuarios y Roles (RBAC)
La identidad del sistema gira alrededor de diferentes tipos de roles administrados en la base de datos y en los *Claims* de Firebase:
- **Admin:** Tiene acceso total (implementado a futuro para paneles de administración general).
- **Organizador:** Puede crear eventos, registrar usuarios de "Control", agrupar y poner tipos de tickets.
- **Cliente:** Puede comprar y visualizar sus propios tickets para eventos activos/publicados.
- **Control:** Usuario secundario bajo el mando de un Organizador, responsable de escanear y validar los tickets mediante el endpoint correspondiente en las puertas de un evento.

### Registro y Flujo de Login
El registro de usuarios lo gestiona el `UserController`, el cual utiliza FirebaseAdmin SDK para `CreateUserAsync` y seguidamente inyecta el claim (ej: `"role" : "Cliente"`) a la cuenta recién creada usando `SetCustomUserClaimsAsync`. Tras este paso en Firebase Auth, guarda los metadatos en Firestore en la colección `users`.

---

## 4. Endpoints y Controladores Principales

### AuthController (`/api/auth`)
- `POST /api/auth/sync`: Valida el token JWT del cliente, lee el `NameIdentifier` (UID) y sincroniza el perfil del usuario con la colección `users` en Firestore.

### UserController (`/api/users`)
Rutas para provisionar usuarios. De forma individual manejan perfiles definidos en la carpeta `Models/`:
- `POST /api/users/cliente`
- `POST /api/users/organizador`
- `POST /api/users/admin`
- `POST /api/users/control` (Solo un Organizador puede registrar usuarios de control para sus eventos).

### EventsController (`/api/events`)
Gestiona el ciclo de vida de los eventos:
- `GET /api/events`: Búsqueda pública de eventos. Permite filtros por categoría, ubicación o fecha. `AllowAnonymous`.
- `GET /api/events/{eventId}`: Detalles de un evento específico.
- `GET /api/events/organizer/me`: Obtiene los eventos creados por el organizador autenticado.
- `POST /api/events`: Creación de un evento (Debe ser Rol = Organizador).
- `POST /api/events/{eventId}/publish`: Cambia el estado del evento a Publicado para que pueda recibir compras de tickets.

### TicketsController (`/api/tickets`)
Servicios transaccionales de venta:
- `POST /api/tickets/buy`: (Rol = Cliente). Genera *N* tickets atados a un tipo de ticket de un evento, reduciendo atómicamente la cantidad disponible.
- `GET /api/tickets/me`: Extrae todos los tickets históricos obtenidos por un cliente específico.
- `POST /api/tickets/validate?ticketId=...&eventId=...`: (Rol = Control). Chequea en tiempo real si el `ticketId` en la colección `tickets` existe y pertenece a la base de ese evento.

---

## 5. Diseño de Base de Datos (Firestore)
Las colecciones principales observadas son:
- **`users`:** Instancias de `ApplicationUser` polimórficas (Admin, Cliente, Organizador, Control). 
- **`events`:** Contiene información del evento y sub-listas de los distintos tipos de entradas (`TicketGroups`). 
- **`tickets`:** Instancias de tickets físicos/virtuales por usuario. Se emiten bajo su propio ID y hacen referencia al `TicketTypeId` del evento.
- **`user_audits`:** Un registro creado por el `FirestoreUserRepository` para mantener trazabilidad sobre eventos de modificación/creación de usuarios en el sistema.
