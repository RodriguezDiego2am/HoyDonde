# HoyDonde? — frontend

Cliente Expo SDK 54 / React Native 0.81 (TypeScript, Expo Router) de la plataforma HoyDonde?. Ver el [README raíz del repositorio](../README.md) para una descripción general del proyecto y [`CLAUDE.md`](../CLAUDE.md) para el detalle técnico completo.

"Olvidé mi contraseña" (Login) y el cambio de contraseña autenticado (`/account/security`) usan exclusivamente el Firebase Client SDK ya configurado con las variables `EXPO_PUBLIC_FIREBASE_*` de abajo — no requieren configuración adicional. `/account/security` es accesible desde Perfil o desde el hub de Control (una cuenta Control puede cambiar su contraseña ahí si conoce la actual); "Olvidé mi contraseña" no le sirve a Control, cuyo email sintético no recibe correo real — si la olvidó, necesita que el Administrador le genere y comparta un enlace desde el detalle de Usuario.

Inmediatamente después de una compra simulada exitosa (`app/events/[id].tsx`), el Cliente puede descargar un comprobante PDF ("HOYDONDE? — COMPROBANTE DE COMPRA SIMULADA", con N.º de operación) generado enteramente en el dispositivo (`expo-print`/`expo-sharing`) a partir de la `Compra` que devuelve la API, con el detalle agrupado por tipo de entrada y un código QR por entrada (mismo payload `{ticketId, eventId}` que ya usa el escáner de Control). Es un comprobante no fiscal de un pago simulado: no hay historial persistente de comprobantes más allá de esa pantalla — para volver a ver una entrada después, se usa Mis entradas.

Organizador (`/organizer/reports`) y Administrador (`/admin/reports`) tienen, además del reporte de desempeño de eventos (filtra por fecha de evento), un reporte de **ventas simuladas** (`/organizer/reports/sales`, `/admin/reports/sales`) que filtra por fecha de compra: lectura rápida, evolución temporal (barras por día/semana/mes, en horario Argentina) y ranking de eventos por importe emitido. Los gráficos son componentes propios (`components/charts/`, Views/texto plano) — sin ninguna librería de charts. Ambos reportes exportan a PDF igual que el resto del módulo.

## Configuración

Copiá `.env.example` a `.env` (en esta misma carpeta) y completá los valores públicos del SDK cliente de Firebase de tu propio proyecto (panel de Firebase → Configuración del proyecto → tus apps). Esos valores son públicos por diseño; nunca copies acá la cuenta de servicio del backend (`HoyDonde.API/firebase-service-account.json`).

`EXPO_PUBLIC_API_URL` debe incluir `/api`. En desarrollo, sin esta variable se usa un fallback automático (`10.0.2.2` para el emulador Android, `localhost` para web/iOS). Para probar desde un **dispositivo físico** con Expo Go en la misma red Wi-Fi que tu backend, usá la IP LAN de tu máquina en vez de `localhost`, por ejemplo:

```
EXPO_PUBLIC_API_URL=http://192.168.1.40:5053/api
```

## Instalación y ejecución

```bash
npm install
npx expo start --lan -c   # o: npm run start:lan
```

`-c` limpia la caché de Metro si veniás de cambiar variables de entorno. Elegí Android/iOS/web desde la salida del CLI, o escaneá el QR con Expo Go para un dispositivo físico.

La API debe estar corriendo y accesible desde el dispositivo/emulador — ver la sección "Ejecutar el backend" del README raíz (para un dispositivo físico, la API debe levantarse con `--urls "http://0.0.0.0:5053"`).

## Verificaciones

```bash
npm run typecheck   # tsc --noEmit
npm run lint         # eslint .
npm test             # jest
```

## Scripts disponibles

| Script | Qué hace |
|---|---|
| `npm run start` | `expo start` |
| `npm run start:lan` | `expo start --lan`, para un dispositivo físico en la misma red |
| `npm run typecheck` | `tsc --noEmit` |
| `npm run lint` | `eslint .` |
| `npm test` | `jest` |
