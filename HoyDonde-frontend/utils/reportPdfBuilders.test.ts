import { buildEventosSectionHtml, buildResumenTableHtml, buildSecurityAuditSectionHtml } from './reportPdfBuilders';
import type { ReporteAdminEventoDetalle, ReporteEventoDetalle, ReporteResumen, SecurityAuditReporteItem } from '@/services/reportService';

const RESUMEN: ReporteResumen = {
  cantidadEventos: 2,
  capacidadInicial: 100,
  stockDisponible: 40,
  entradasEmitidas: 60,
  entradasUsadas: 30,
  entradasAnuladas: 0,
  entradasPendientes: 30,
  porcentajeOcupacion: 60,
  porcentajeAsistencia: 50,
  porcentajeUtilizacion: 30,
  importeEmitido: 1234.5,
};

function buildEvento(overrides: Partial<ReporteEventoDetalle> = {}): ReporteEventoDetalle {
  return {
    eventId: 'event-1',
    nombre: 'Festival <script>alert(1)</script>',
    ubicacion: 'Buenos Aires',
    categoria: 'Musica',
    estado: 'Publicado',
    fechaInicio: '2026-06-01T20:00:00.000Z',
    fechaFin: '2026-06-01T23:00:00.000Z',
    capacidadInicial: 50,
    stockDisponible: 10,
    entradasEmitidas: 40,
    entradasUsadas: 20,
    entradasAnuladas: 0,
    entradasPendientes: 20,
    porcentajeOcupacion: 80,
    porcentajeAsistencia: 50,
    porcentajeUtilizacion: 40,
    importeEmitido: 400,
    tiposDeEntrada: [],
    ...overrides,
  };
}

describe('buildResumenTableHtml', () => {
  it('incluye las cifras del resumen', () => {
    const html = buildResumenTableHtml(RESUMEN);

    expect(html).toContain('60'); // entradas emitidas
    expect(html).toContain('30'); // entradas usadas
  });
});

describe('buildEventosSectionHtml', () => {
  it('escapa el nombre del evento antes de interpolarlo', () => {
    const html = buildEventosSectionHtml([buildEvento()]);

    expect(html).not.toContain('<script>alert(1)</script>');
    expect(html).toContain('&lt;script&gt;alert(1)&lt;/script&gt;');
  });

  it('sin eventos, muestra el mensaje vacío en vez de una tabla', () => {
    const html = buildEventosSectionHtml([]);

    expect(html).toContain('Ningún evento coincide con los filtros aplicados.');
  });

  it('reporte Admin: agrega la columna Organizador resuelta por nombre', () => {
    const eventoAdmin: ReporteAdminEventoDetalle = { ...buildEvento(), organizadorPersonaId: 'persona-1' };

    const html = buildEventosSectionHtml([eventoAdmin], { organizadorNombrePorPersonaId: { 'persona-1': 'organizador@test.com' } });

    expect(html).toContain('organizador@test.com');
    expect(html).toContain('<th>Organizador</th>');
  });

  it('reporte Organizador: nunca agrega la columna Organizador', () => {
    const html = buildEventosSectionHtml([buildEvento()]);

    expect(html).not.toContain('<th>Organizador</th>');
  });

  it('incluye el desglose por tipo de entrada cuando el evento tiene tipos', () => {
    const evento = buildEvento({
      tiposDeEntrada: [
        {
          ticketTypeId: 'tipo-1',
          nombre: 'General <b>VIP</b>',
          capacidadInicial: 50,
          stockDisponible: 10,
          entradasEmitidas: 40,
          entradasUsadas: 20,
          entradasAnuladas: 0,
          entradasPendientes: 20,
          porcentajeOcupacion: 80,
          porcentajeAsistencia: 50,
          porcentajeUtilizacion: 40,
          importeEmitido: 400,
        },
      ],
    });

    const html = buildEventosSectionHtml([evento]);

    expect(html).toContain('tipos de entrada');
    expect(html).toContain('&lt;b&gt;VIP&lt;/b&gt;');
    expect(html).not.toContain('<b>VIP</b>');
  });
});

describe('buildSecurityAuditSectionHtml', () => {
  const AUDIT: SecurityAuditReporteItem = {
    timestamp: '2026-06-01T12:00:00.000Z',
    operacion: 'ROL_ASIGNAR_ACCION',
    actorUsuarioId: 'usuario-1',
    actorEmail: 'admin@test.com',
    targetTipo: 'RolAccion',
    targetId: 'ORGANIZADOR/EVENTO_CREAR',
    detalle: '<script>alert(1)</script>',
  };

  it('escapa el detalle antes de interpolarlo', () => {
    const html = buildSecurityAuditSectionHtml([AUDIT]);

    expect(html).not.toContain('<script>alert(1)</script>');
    expect(html).toContain('&lt;script&gt;alert(1)&lt;/script&gt;');
  });

  it('usa el email del actor cuando está resuelto', () => {
    const html = buildSecurityAuditSectionHtml([AUDIT]);

    expect(html).toContain('admin@test.com');
  });

  it('usa el usuarioId cuando el actor ya no existe (actorEmail null)', () => {
    const html = buildSecurityAuditSectionHtml([{ ...AUDIT, actorEmail: null }]);

    expect(html).toContain('usuario-1');
  });

  it('sin auditorías, muestra el mensaje vacío en vez de una tabla', () => {
    const html = buildSecurityAuditSectionHtml([]);

    expect(html).toContain('Ninguna auditoría coincide con los filtros aplicados.');
  });
});
