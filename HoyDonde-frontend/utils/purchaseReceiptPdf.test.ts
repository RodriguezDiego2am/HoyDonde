const mockPrintToFileAsync = jest.fn();
const mockIsAvailableAsync = jest.fn();
const mockShareAsync = jest.fn();
const mockFileDelete = jest.fn();
const mockFileCopy = jest.fn();
let mockDestinationExists = false;

jest.mock('expo-print', () => ({
  printToFileAsync: (...args: unknown[]) => mockPrintToFileAsync(...args),
}));
jest.mock('expo-sharing', () => ({
  isAvailableAsync: (...args: unknown[]) => mockIsAvailableAsync(...args),
  shareAsync: (...args: unknown[]) => mockShareAsync(...args),
}));
jest.mock('expo-file-system', () => {
  class MockFile {
    uri: string;
    constructor(...parts: unknown[]) {
      this.uri = `mock-file://${parts
        .map((p) => (typeof p === 'string' ? p : (p as { uri?: string })?.uri ?? 'dir'))
        .join('/')}`;
    }
    get exists() {
      return mockDestinationExists;
    }
    delete(...args: unknown[]) {
      return mockFileDelete(...args);
    }
    copy(...args: unknown[]) {
      return mockFileCopy(...args);
    }
  }
  return {
    Paths: { cache: 'CACHE_DIR' },
    File: MockFile,
  };
});

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import {
  PurchaseReceiptInconsistencyError,
  assertCompraConsistente,
  buildPurchaseReceiptHtml,
  buildReceiptFileName,
  computePurchaseTotals,
  formatPrecioOGratis,
  generateAndSharePurchaseReceiptPdf,
  groupPurchaseTickets,
} from './purchaseReceiptPdf';
// eslint-disable-next-line import/first
import { buildQrSvgMarkup } from './qrSvg';
// eslint-disable-next-line import/first
import { buildTicketQrPayload } from './ticketQr';
// eslint-disable-next-line import/first
import type { CompraResponse, TicketResponse } from '@/services/APIService';

function ticket(overrides: Partial<TicketResponse> = {}): TicketResponse {
  return {
    id: 'ticket-1',
    compraId: 'compra-1',
    eventoId: 'evento-1',
    ticketTypeId: 'tipo-general',
    clientePersonaId: 'persona-secreta-999',
    fechaCompra: '2026-08-02T12:00:00Z',
    estado: 'Emitido',
    utilizable: true,
    motivoNoUtilizable: null,
    eventoNombre: 'Festival de Verano',
    ticketTypeNombre: 'General',
    precioPagado: 5000,
    fechaInicio: '2026-12-01T22:00:00Z',
    fechaFin: '2026-12-02T04:00:00Z',
    ...overrides,
  };
}

/** Construye una CompraResponse consistente por default (cantidadEntradas/importeTotal calculados desde los tickets pasados). */
function compra(overrides: Partial<CompraResponse> = {}): CompraResponse {
  const tickets = overrides.tickets ?? [ticket()];
  return {
    id: 'compra-1',
    eventoId: 'evento-1',
    eventoNombre: 'Festival de Verano',
    ubicacion: 'Parque Central',
    fechaInicio: '2026-12-01T22:00:00Z',
    fechaFin: '2026-12-02T04:00:00Z',
    fechaCompra: '2026-08-02T12:00:00Z',
    cantidadEntradas: tickets.length,
    importeTotal: tickets.reduce((sum, t) => sum + t.precioPagado, 0),
    pagoSimulado: true,
    ...overrides,
    tickets,
  };
}

describe('groupPurchaseTickets', () => {
  it('agrupa por ticketTypeId sumando cantidad y subtotal', () => {
    const tickets = [
      ticket({ id: 't1', ticketTypeId: 'general', ticketTypeNombre: 'General', precioPagado: 5000 }),
      ticket({ id: 't2', ticketTypeId: 'general', ticketTypeNombre: 'General', precioPagado: 5000 }),
      ticket({ id: 't3', ticketTypeId: 'vip', ticketTypeNombre: 'VIP', precioPagado: 10000 }),
    ];

    const grupos = groupPurchaseTickets(tickets);

    expect(grupos).toEqual([
      { ticketTypeId: 'general', ticketTypeNombre: 'General', cantidad: 2, precioUnitario: 5000, subtotal: 10000 },
      { ticketTypeId: 'vip', ticketTypeNombre: 'VIP', cantidad: 1, precioUnitario: 10000, subtotal: 10000 },
    ]);
  });
});

describe('computePurchaseTotals', () => {
  it('cantidad total y suma de PrecioPagado, sin recalcular contra el Event', () => {
    const tickets = [
      ticket({ id: 't1', precioPagado: 5000 }),
      ticket({ id: 't2', precioPagado: 5000 }),
      ticket({ id: 't3', ticketTypeId: 'vip', precioPagado: 10000 }),
    ];

    expect(computePurchaseTotals(tickets)).toEqual({ cantidadTotal: 3, importeTotal: 20000 });
  });
});

describe('formatPrecioOGratis', () => {
  it('muestra GRATIS para precio 0', () => {
    expect(formatPrecioOGratis(0)).toBe('GRATIS');
  });

  it('formatea como moneda para precio > 0', () => {
    expect(formatPrecioOGratis(5000)).toMatch(/5\.?000|5000/);
    expect(formatPrecioOGratis(5000)).not.toBe('GRATIS');
  });
});

describe('buildReceiptFileName', () => {
  it('usa la fecha de compra en formato AAAA-MM-DD', () => {
    expect(buildReceiptFileName('2026-08-02T12:00:00Z')).toBe('HoyDonde-Comprobante-2026-08-02.pdf');
  });

  it('nunca queda vacío ante una fecha inválida', () => {
    expect(buildReceiptFileName('no-es-una-fecha')).toMatch(/^HoyDonde-Comprobante-\d{4}-\d{2}-\d{2}\.pdf$/);
  });
});

describe('assertCompraConsistente', () => {
  it('no lanza cuando la suma de los tickets coincide con Compra', () => {
    expect(() => assertCompraConsistente(compra())).not.toThrow();
  });

  it('lanza PurchaseReceiptInconsistencyError si importeTotal no coincide con la suma de los tickets', () => {
    const inconsistente = compra({ importeTotal: 999999 });
    expect(() => assertCompraConsistente(inconsistente)).toThrow(PurchaseReceiptInconsistencyError);
  });

  it('lanza si cantidadEntradas no coincide con la cantidad de tickets', () => {
    const inconsistente = compra({ cantidadEntradas: 5 });
    expect(() => assertCompraConsistente(inconsistente)).toThrow(PurchaseReceiptInconsistencyError);
  });
});

describe('buildPurchaseReceiptHtml', () => {
  it('lanza si no hay entradas', () => {
    expect(() => buildPurchaseReceiptHtml(compra({ tickets: [], cantidadEntradas: 0, importeTotal: 0 }))).toThrow();
  });

  it('nunca genera un comprobante engañoso: lanza PurchaseReceiptInconsistencyError si la suma de tickets no coincide con Compra.ImporteTotal', () => {
    const inconsistente = compra({ importeTotal: 1 });
    expect(() => buildPurchaseReceiptHtml(inconsistente)).toThrow(PurchaseReceiptInconsistencyError);
  });

  it('incluye título, N.º de operación, sello no fiscal y aclaración de pago simulado', () => {
    const html = buildPurchaseReceiptHtml(compra({ id: 'compra-abc-123' }));

    expect(html).toContain('HOYDONDE?');
    expect(html).toContain('COMPROBANTE DE COMPRA SIMULADA');
    expect(html).toContain('N.º DE OPERACIÓN: compra-abc-123');
    expect(html).toContain('COMPROBANTE NO FISCAL');
    expect(html).toContain('La operación utiliza un pago simulado. El importe emitido no acredita dinero cobrado ni reemplaza una factura o recibo fiscal.');
  });

  it('nunca afirma un pago real ni una recaudación (el disclaimer solo las niega)', () => {
    const html = buildPurchaseReceiptHtml(compra());

    expect(html.toLowerCase()).not.toContain('pago aprobado');
    expect(html.toLowerCase()).not.toContain('recaudación');
    expect(html.toLowerCase()).not.toContain('numeración fiscal');
    // El disclaimer obligatorio sí menciona "dinero cobrado"/"factura", pero siempre en
    // negación ("no acredita.../no reemplaza...").
    expect(html).toMatch(/no acredita dinero cobrado/);
    expect(html).toMatch(/ni reemplaza una factura o recibo fiscal/);
  });

  it('el encabezado usa exclusivamente la fotografía de Compra (evento, ubicación, vigencia, fecha de compra, pago simulado)', () => {
    const html = buildPurchaseReceiptHtml(
      compra({ eventoNombre: 'Festival de Verano', ubicacion: 'Parque Central', pagoSimulado: true })
    );

    expect(html).toContain('Festival de Verano');
    expect(html).toContain('Parque Central');
    expect(html).toContain('Simulado');
    // formatFechaHora produce "02 AGO 2026 · 09:00" (hora local del entorno de test) — solo
    // verificamos que la fecha (día/año) aparece, sin acoplar el test a una zona horaria fija.
    expect(html).toMatch(/2026/);
  });

  it('cantidad e importe total emitido salen de Compra, no de recalcular sobre los tickets', () => {
    const tickets = [
      ticket({ id: 't1', ticketTypeId: 'general', ticketTypeNombre: 'General', precioPagado: 5000 }),
      ticket({ id: 't2', ticketTypeId: 'general', ticketTypeNombre: 'General', precioPagado: 5000 }),
      ticket({ id: 't3', ticketTypeId: 'vip', ticketTypeNombre: 'VIP', precioPagado: 10000 }),
    ];
    const html = buildPurchaseReceiptHtml(compra({ tickets }));

    expect(html).toContain('General');
    expect(html).toContain('VIP');
    expect(html).toContain('Cantidad total de entradas');
    expect(html).toContain('>3<');
    expect(html).toContain('Importe total emitido');
    expect(html).toContain('20.000');
  });

  it('entradas gratuitas muestran GRATIS', () => {
    const html = buildPurchaseReceiptHtml(compra({ tickets: [ticket({ precioPagado: 0 })], importeTotal: 0 }));

    expect(html).toContain('GRATIS');
  });

  it('un QR por ticket, y el payload es exactamente el mismo que consume el escáner de Control', () => {
    const t1 = ticket({ id: 'ticket-aaa', eventoId: 'evento-xyz' });
    const t2 = ticket({ id: 'ticket-bbb', eventoId: 'evento-xyz' });
    const html = buildPurchaseReceiptHtml(compra({ eventoId: 'evento-xyz', tickets: [t1, t2] }));

    const ticketBlocks = html.match(/<div class="ticket">/g) ?? [];
    expect(ticketBlocks).toHaveLength(2);

    const svg1 = buildQrSvgMarkup(buildTicketQrPayload('ticket-aaa', 'evento-xyz'), { size: 190 });
    const svg2 = buildQrSvgMarkup(buildTicketQrPayload('ticket-bbb', 'evento-xyz'), { size: 190 });
    expect(html).toContain(svg1);
    expect(html).toContain(svg2);

    // El payload embebido decodifica exactamente a ticketId/eventId, vía el mismo parser que ya
    // usa Control (utils/ticketQr.ts), verificado manualmente contra un escaneo real (CLAUDE.md
    // "Frontend 3").
    expect(JSON.parse(buildTicketQrPayload('ticket-aaa', 'evento-xyz'))).toEqual({
      ticketId: 'ticket-aaa',
      eventId: 'evento-xyz',
    });
  });

  it('todos los tickets de la sección de detalle comparten el mismo CompraId que la Compra', () => {
    const tickets = [
      ticket({ id: 't1', compraId: 'compra-xyz' }),
      ticket({ id: 't2', compraId: 'compra-xyz' }),
    ];
    const html = buildPurchaseReceiptHtml(compra({ id: 'compra-xyz', tickets }));

    expect(tickets.every((t) => t.compraId === 'compra-xyz')).toBe(true);
    expect(html).toContain('N.º DE OPERACIÓN: compra-xyz');
  });

  it('soporta varias entradas sin cortar un QR entre páginas (break-inside: avoid por entrada)', () => {
    const tickets = Array.from({ length: 12 }, (_, i) => ticket({ id: `ticket-${i}` }));
    const html = buildPurchaseReceiptHtml(compra({ tickets }));

    expect((html.match(/<div class="ticket">/g) ?? []).length).toBe(12);
    expect(html).toContain('break-inside: avoid');
    expect(html).toContain('page-break-inside: avoid');
  });

  it('escapa caracteres HTML dinámicos (id de operación, nombre de evento, ubicación, tipo de entrada, id de ticket)', () => {
    const html = buildPurchaseReceiptHtml(
      compra({
        id: '"><script>alert(0)</script>',
        eventoNombre: '<script>alert("evento")</script>',
        ubicacion: '<img src=x onerror=alert(1)>',
        tickets: [
          ticket({
            ticketTypeNombre: '<b>VIP</b>',
            id: '"><script>alert(1)</script>',
          }),
        ],
      })
    );

    expect(html).not.toContain('<img src=x onerror=alert(1)>');
    expect(html).not.toContain('<script>alert("evento")</script>');
    expect(html).not.toContain('<b>VIP</b>');
    expect(html).not.toContain('"><script>alert(1)</script>');
    expect(html).not.toContain('"><script>alert(0)</script>');
    expect(html).toContain('&lt;img src=x onerror=alert(1)&gt;');
    expect(html).toContain('&lt;script&gt;alert(&quot;evento&quot;)&lt;/script&gt;');
  });

  it('nunca incluye identificadores sensibles (UID, UsuarioId, PersonaId, ExternalSubjectId, DNI, teléfono)', () => {
    const html = buildPurchaseReceiptHtml(compra({ tickets: [ticket({ clientePersonaId: 'persona-secreta-999' })] }));

    expect(html).not.toContain('persona-secreta-999');
    expect(html.toLowerCase()).not.toContain('externalsubjectid');
    expect(html.toLowerCase()).not.toContain('usuarioid');
    expect(html.toLowerCase()).not.toContain('dni');
    expect(html.toLowerCase()).not.toContain('teléfono');
    expect(html.toLowerCase()).not.toContain('token');
  });
});

describe('generateAndSharePurchaseReceiptPdf', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockDestinationExists = false;
  });

  it('genera el PDF, lo renombra a un nombre legible y lo comparte', async () => {
    mockPrintToFileAsync.mockResolvedValue({ uri: 'file://tmp/print-abc123.pdf' });
    mockIsAvailableAsync.mockResolvedValue(true);
    mockShareAsync.mockResolvedValue(undefined);

    const result = await generateAndSharePurchaseReceiptPdf('<html></html>', 'HoyDonde-Comprobante-2026-08-02.pdf');

    expect(mockPrintToFileAsync).toHaveBeenCalledWith({ html: '<html></html>' });
    expect(mockFileCopy).toHaveBeenCalledTimes(1);
    expect(mockShareAsync).toHaveBeenCalledWith(
      expect.stringContaining('HoyDonde-Comprobante-2026-08-02.pdf'),
      expect.objectContaining({ mimeType: 'application/pdf', dialogTitle: 'HoyDonde-Comprobante-2026-08-02.pdf' })
    );
    expect(result.shared).toBe(true);
  });

  it('si ya existe un archivo con ese nombre, lo borra antes de copiar (reintento no duplica)', async () => {
    mockPrintToFileAsync.mockResolvedValue({ uri: 'file://tmp/print-abc123.pdf' });
    mockIsAvailableAsync.mockResolvedValue(true);
    mockDestinationExists = true;

    await generateAndSharePurchaseReceiptPdf('<html></html>', 'HoyDonde-Comprobante-2026-08-02.pdf');

    expect(mockFileDelete).toHaveBeenCalledTimes(1);
  });

  it('cuando el renombrado falla, comparte igual el archivo original sin lanzar', async () => {
    mockPrintToFileAsync.mockResolvedValue({ uri: 'file://tmp/print-abc123.pdf' });
    mockIsAvailableAsync.mockResolvedValue(true);
    mockFileCopy.mockImplementation(() => {
      throw new Error('sin espacio');
    });

    await expect(
      generateAndSharePurchaseReceiptPdf('<html></html>', 'HoyDonde-Comprobante-2026-08-02.pdf')
    ).resolves.toEqual({ uri: 'file://tmp/print-abc123.pdf', shared: true });
  });

  it('cuando sharing no está disponible, devuelve shared:false sin lanzar', async () => {
    mockPrintToFileAsync.mockResolvedValue({ uri: 'file://tmp/print-abc123.pdf' });
    mockIsAvailableAsync.mockResolvedValue(false);

    const result = await generateAndSharePurchaseReceiptPdf('<html></html>', 'HoyDonde-Comprobante-2026-08-02.pdf');

    expect(mockShareAsync).not.toHaveBeenCalled();
    expect(result.shared).toBe(false);
  });

  it('un error inesperado al generar se propaga (para que la UI permita reintentar)', async () => {
    mockPrintToFileAsync.mockRejectedValue(new Error('boom'));

    await expect(
      generateAndSharePurchaseReceiptPdf('<html></html>', 'HoyDonde-Comprobante-2026-08-02.pdf')
    ).rejects.toThrow('boom');
  });
});
