import genMatrix from 'react-native-qrcode-svg/src/genMatrix';
import transformMatrixIntoPath from 'react-native-qrcode-svg/src/transformMatrixIntoPath';

/**
 * SVG de un QR generado localmente reutilizando el mismo par de funciones puras (sin dependencia
 * de React Native) que react-native-qrcode-svg usa para dibujar su <Path> en pantalla — nunca un
 * QR generado en el backend, nunca otra dependencia de QR. `value` nunca aparece como texto en el
 * SVG (se codifica en la matriz de módulos), así que no requiere escapado HTML.
 */
export interface QrSvgOptions {
  size?: number;
  color?: string;
  backgroundColor?: string;
  errorCorrectionLevel?: 'L' | 'M' | 'Q' | 'H';
}

export function buildQrSvgMarkup(value: string, options: QrSvgOptions = {}): string {
  const { size = 200, color = '#171512', backgroundColor = '#FFFFFF', errorCorrectionLevel = 'M' } = options;

  const matrix = genMatrix(value, errorCorrectionLevel);
  const { path, cellSize } = transformMatrixIntoPath(matrix, size);

  return (
    `<svg width="${size}" height="${size}" viewBox="0 0 ${size} ${size}" xmlns="http://www.w3.org/2000/svg">` +
    `<rect width="${size}" height="${size}" fill="${backgroundColor}"/>` +
    `<path d="${path}" stroke="${color}" stroke-width="${cellSize}" fill="none" stroke-linecap="butt"/>` +
    `</svg>`
  );
}
