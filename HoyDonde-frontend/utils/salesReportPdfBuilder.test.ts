import { buildVentasBodyHtml } from './salesReportPdfBuilder';
import type { VentasReporteResponse } from '@/services/reportService';

function buildReport(overrides: Partial<VentasReporteResponse> = {}): VentasReporteResponse {
  return {
    fechaDesde: '2026-01-01T00:00:00Z',
    fechaHasta: '2026-02-01T00:00:00Z',
    aclaracionImporte: 'El MVP no procesa pagos reales.',
    resumen: {
      cantidadCompras: 3,
      entradasEmitidas: 5,
      importeEmitido: 500,
      importePromedioPorCompra: 166.67,
      precioPromedioEntrada: 100,
      clientesUnicos: 2,
      eventoConMayorImporte: { eventoId: 'event-1', eventoNombre: 'Festival <script>', importeEmitido: 500, entradasEmitidas: 5 },
      eventoConMasEntradas: null,
    },
    serieTemporal: [
      { periodoDesde: '2026-01-05T00:00:00Z', periodoHasta: '2026-01-06T00:00:00Z', etiqueta: '05/01', cantidadCompras: 3, entradasEmitidas: 5, importeEmitido: 500 },
    ],
    topEventos: [{ eventoId: 'event-1', eventoNombre: 'Festival <script>', cantidadCompras: 3, entradasEmitidas: 5, importeEmitido: 500, importePromedioCompra: 166.67 }],
    porCategoria: [{ categoria: 'Musica', cantidadCompras: 3, entradasEmitidas: 5, importeEmitido: 500, porcentajeDelImporteTotal: 100 }],
    porTipoEntrada: [],
    filtrosDisponibles: { eventos: [], tiposEntrada: [] },
    ...overrides,
  };
}

describe('buildVentasBodyHtml', () => {
  it('incluye la lectura rápida (resumen)', () => {
    const html = buildVentasBodyHtml(buildReport());

    expect(html).toContain('Lectura rápida');
    expect(html).toContain('3'); // compras
    expect(html).toContain('5'); // entradas
  });

  it('escapa el nombre dinámico del evento destacado y del top de eventos', () => {
    const html = buildVentasBodyHtml(buildReport());

    expect(html).not.toContain('<script>');
    expect(html).toContain('&lt;script&gt;');
  });

  it('incluye el gráfico temporal en HTML/CSS (sin canvas ni script remoto)', () => {
    const html = buildVentasBodyHtml(buildReport());

    expect(html).toContain('Evolución temporal');
    expect(html).toContain('chart-bar-v');
    expect(html).not.toContain('<canvas');
    expect(html).not.toContain('<script src=');
  });

  it('sin serie temporal, muestra el estado vacío', () => {
    const html = buildVentasBodyHtml(buildReport({ serieTemporal: [] }));

    expect(html).toContain('Sin datos para el período elegido.');
  });

  it('incluye el Top 5 con barras horizontales', () => {
    const html = buildVentasBodyHtml(buildReport());

    expect(html).toContain('Top eventos por importe emitido');
    expect(html).toContain('hbar-fill');
  });

  it('incluye el desglose por categoría', () => {
    const html = buildVentasBodyHtml(buildReport());

    expect(html).toContain('Por categoría');
    expect(html).toContain('Musica');
  });

  it('sin desglose por tipo de entrada, no agrega esa sección', () => {
    const html = buildVentasBodyHtml(buildReport({ porTipoEntrada: [] }));

    expect(html).not.toContain('Por tipo de entrada');
  });

  it('con desglose por tipo de entrada, lo incluye', () => {
    const html = buildVentasBodyHtml(
      buildReport({ porTipoEntrada: [{ ticketTypeId: 'tipo-1', ticketTypeNombre: 'General', cantidadComprasDistintas: 3, entradasEmitidas: 5, importeEmitido: 500, porcentajeDelImporteTotal: 100 }] })
    );

    expect(html).toContain('Por tipo de entrada');
    expect(html).toContain('General');
  });

  it('nunca expone identificadores internos (eventoId, clientePersonaId)', () => {
    const html = buildVentasBodyHtml(buildReport());

    expect(html).not.toContain('event-1');
    expect(html).not.toContain('clientePersonaId');
  });
});
