import genMatrix from 'react-native-qrcode-svg/src/genMatrix';
import transformMatrixIntoPath from 'react-native-qrcode-svg/src/transformMatrixIntoPath';
import { buildQrSvgMarkup } from './qrSvg';

describe('buildQrSvgMarkup', () => {
  it('produce un <svg> con el tamaño pedido y un <path> no vacío', () => {
    const svg = buildQrSvgMarkup('hola-mundo', { size: 180 });

    expect(svg).toContain('<svg width="180" height="180" viewBox="0 0 180 180"');
    expect(svg).toMatch(/<path d="M[^"]+"/);
  });

  it('usa 200 como tamaño por default', () => {
    const svg = buildQrSvgMarkup('valor');
    expect(svg).toContain('width="200" height="200"');
  });

  it('codifica exactamente el mismo path/cellSize que genMatrix + transformMatrixIntoPath para el mismo valor', () => {
    // Mismo par de funciones puras que usa <QRCode> de react-native-qrcode-svg para dibujar su
    // <Path> en pantalla (ver TicketQRModal, ya verificado manualmente contra un escaneo real —
    // CLAUDE.md "Frontend 3"): si esta llamada directa coincide con el SVG generado, el QR del
    // comprobante es geométricamente idéntico al QR que ya se sabe que escanea correctamente.
    const value = JSON.stringify({ ticketId: 'ticket-123', eventId: 'evento-456' });
    const { path, cellSize } = transformMatrixIntoPath(genMatrix(value, 'M'), 200);

    const svg = buildQrSvgMarkup(value, { size: 200 });

    expect(svg).toContain(`<path d="${path}" stroke="#171512" stroke-width="${cellSize}"`);
  });

  it('nunca incluye el valor codificado como texto plano en el SVG (va en la matriz de módulos)', () => {
    const svg = buildQrSvgMarkup('{"ticketId":"abc","eventId":"def"}');
    expect(svg).not.toContain('ticketId');
    expect(svg).not.toContain('abc');
  });

  it('es determinístico: el mismo valor produce siempre el mismo SVG', () => {
    const a = buildQrSvgMarkup('mismo-valor', { size: 150 });
    const b = buildQrSvgMarkup('mismo-valor', { size: 150 });
    expect(a).toBe(b);
  });
});
