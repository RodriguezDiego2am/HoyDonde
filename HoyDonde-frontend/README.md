# HoyDonde? — frontend

Cliente Expo SDK 54 / React Native 0.81 (TypeScript, Expo Router) de la plataforma HoyDonde?. Ver el [README raíz del repositorio](../README.md) para una descripción general del proyecto y [`CLAUDE.md`](../CLAUDE.md) para el detalle técnico completo.

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
