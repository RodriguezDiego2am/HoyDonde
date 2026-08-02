const { getDefaultConfig } = require('expo/metro-config');

const config = getDefaultConfig(__dirname);

// expo-router trata cada archivo dentro de app/ como una ruta candidata, incluyendo
// los .test.ts(x) colocados junto a las rutas reales (app/index.test.tsx,
// app/routes.test.ts). Sin este blockList, Metro intenta empaquetarlos como parte
// de la app y arrastra dependencias de testing (react-test-renderer) al bundle.
config.resolver.blockList = [/.*\.(test|spec)\.[jt]sx?$/];

module.exports = config;
