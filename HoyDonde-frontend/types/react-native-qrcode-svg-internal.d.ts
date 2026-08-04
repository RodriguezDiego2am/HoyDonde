/**
 * react-native-qrcode-svg (dependencia ya declarada en package.json) no expone una API pública
 * para obtener el path SVG del QR sin montar un componente nativo. Internamente sí lo hace en dos
 * módulos puros sin dependencias de React Native (src/genMatrix.js, src/transformMatrixIntoPath.js
 * — el mismo par que usa <QRCode> para construir su <Path>), reutilizados tal cual desde
 * utils/qrSvg.ts en vez de reimplementar la generación de QR o instalar otra dependencia. Estas
 * declaraciones solo tipan esa reutilización; el paquete no trae tipos para sus módulos internos.
 */
declare module 'react-native-qrcode-svg/src/genMatrix' {
  export default function genMatrix(value: string, errorCorrectionLevel?: string): number[][];
}

declare module 'react-native-qrcode-svg/src/transformMatrixIntoPath' {
  export default function transformMatrixIntoPath(
    matrix: number[][],
    size: number
  ): { path: string; cellSize: number };
}
