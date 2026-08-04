import React from 'react';
import { act, fireEvent, render } from '@testing-library/react-native';

jest.mock('../../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

const mockBack = jest.fn();
jest.mock('expo-router', () => ({
  router: { push: jest.fn(), back: (...args: unknown[]) => mockBack(...args) },
}));

let mockHasAccion: (accion: string) => boolean = () => true;
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => ({ hasAccion: (accion: string) => mockHasAccion(accion) }),
}));

const mockPrintToFileAsync = jest.fn();
const mockIsAvailableAsync = jest.fn();
const mockShareAsync = jest.fn();
jest.mock('expo-print', () => ({ printToFileAsync: (...args: unknown[]) => mockPrintToFileAsync(...args) }));
jest.mock('expo-sharing', () => ({
  isAvailableAsync: (...args: unknown[]) => mockIsAvailableAsync(...args),
  shareAsync: (...args: unknown[]) => mockShareAsync(...args),
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { apiClient } from '@/services/APIService';
// eslint-disable-next-line import/first
import type { UsuarioResumenResponse } from '@/services/securityAdminService';
// eslint-disable-next-line import/first
import type { VentasReporteResponse } from '@/services/reportService';
// eslint-disable-next-line import/first
import AdminSalesReportScreen from './AdminSalesReportScreen';
// eslint-disable-next-line import/first
import { nextLocalDayExclusive, parseLocalDate, startOfLocalDay, toUtcIso } from '@/utils/datetime';

function organizador(overrides: Partial<UsuarioResumenResponse> = {}): UsuarioResumenResponse {
  return {
    usuarioId: 'usuario-org-1',
    personaId: 'persona-org-1',
    email: 'organizador@test.com',
    activo: true,
    rolesActivos: ['ORGANIZADOR'],
    ...overrides,
  };
}

const REPORT: VentasReporteResponse = {
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
    eventoConMayorImporte: { eventoId: 'event-1', eventoNombre: 'Festival Global', importeEmitido: 500, entradasEmitidas: 5 },
    eventoConMasEntradas: { eventoId: 'event-1', eventoNombre: 'Festival Global', importeEmitido: 500, entradasEmitidas: 5 },
  },
  serieTemporal: [
    { periodoDesde: '2026-01-05T00:00:00Z', periodoHasta: '2026-01-06T00:00:00Z', etiqueta: '05/01', cantidadCompras: 3, entradasEmitidas: 5, importeEmitido: 500 },
  ],
  topEventos: [{ eventoId: 'event-1', eventoNombre: 'Festival Global', cantidadCompras: 3, entradasEmitidas: 5, importeEmitido: 500, importePromedioCompra: 166.67 }],
  porCategoria: [{ categoria: 'Musica', cantidadCompras: 3, entradasEmitidas: 5, importeEmitido: 500, porcentajeDelImporteTotal: 100 }],
  porTipoEntrada: [],
  filtrosDisponibles: {
    eventos: [
      { id: 'event-1', nombre: 'Festival Global' },
      { id: 'event-2', nombre: 'Otro Festival' },
    ],
    tiposEntrada: [],
  },
};

function mockApiRoutes(opts: { usuarios?: UsuarioResumenResponse[]; report?: VentasReporteResponse; reportError?: unknown } = {}) {
  jest.spyOn(apiClient, 'get').mockImplementation(async (url: string) => {
    if (url === '/security/usuarios') return { data: opts.usuarios ?? [] };
    if (url === '/reports/admin/sales') {
      if (opts.reportError) throw opts.reportError;
      return { data: opts.report ?? REPORT };
    }
    throw new Error(`unexpected url ${url}`);
  });
}

function fillValidRange(getByTestId: (id: string) => any) {
  fireEvent.changeText(getByTestId('admin-ventas-fecha-desde-day'), '01012026');
  fireEvent.changeText(getByTestId('admin-ventas-fecha-hasta-day'), '01022026');
}

describe('AdminSalesReportScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockHasAccion = () => true;
  });

  it('exige el rango Desde/Hasta antes de llamar a la API', async () => {
    mockApiRoutes();
    const { getByText, findByText } = render(<AdminSalesReportScreen />);

    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText(/obligatorios para este reporte/)).toBeTruthy();
  });

  it('con un rango válido, llama a GET /reports/admin/sales en UTC sin organizadorPersonaId por defecto', async () => {
    mockApiRoutes();
    const { getByTestId, getByText, findByText } = render(<AdminSalesReportScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('Importe emitido')).toBeTruthy();
    const getSpy = apiClient.get as jest.Mock;
    const reportCall = getSpy.mock.calls.find((c) => c[0] === '/reports/admin/sales');
    expect(reportCall![1].params.fechaDesde).toBe(toUtcIso(startOfLocalDay(parseLocalDate('01/01/2026')!)));
    expect(reportCall![1].params.fechaHasta).toBe(toUtcIso(nextLocalDayExclusive(parseLocalDate('01/02/2026')!)));
    expect(reportCall![1].params.organizadorPersonaId).toBeUndefined();
  });

  it('elegir un organizador manda su personaId como filtro y lo muestra por email en la UI', async () => {
    mockApiRoutes({ usuarios: [organizador()] });
    const { getByTestId, getByText, findByText, getByLabelText } = render(<AdminSalesReportScreen />);

    fireEvent.press(getByLabelText('Elegir organizador'));
    fireEvent.press(await findByText('organizador@test.com'));

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('Importe emitido')).toBeTruthy();
    const getSpy = apiClient.get as jest.Mock;
    const reportCall = getSpy.mock.calls.find((c) => c[0] === '/reports/admin/sales');
    expect(reportCall![1].params.organizadorPersonaId).toBe('persona-org-1');
  });

  it('oculta el selector de organizador cuando la sesión no tiene USUARIO_VER_PERMISOS_EFECTIVOS', async () => {
    mockHasAccion = (accion) => accion === 'REPORTE_VER_GLOBAL';
    mockApiRoutes();
    const { queryByLabelText } = render(<AdminSalesReportScreen />);

    expect(queryByLabelText('Elegir organizador')).toBeNull();
  });

  it('muestra error con reintento si la API falla', async () => {
    mockApiRoutes({ reportError: new Error('network down') });
    const { getByTestId, getByText, findByText } = render(<AdminSalesReportScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('No se pudo generar el reporte. Verificá tu conexión.')).toBeTruthy();
  });

  it('tocar un evento del Top eventos acota el reporte a ese evento (eventId), y se puede quitar', async () => {
    mockApiRoutes();
    const { getByTestId, getByText, findByText, getByLabelText, queryByLabelText, findAllByText } = render(<AdminSalesReportScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Importe emitido')).toBeTruthy();

    await act(async () => {
      fireEvent.press(getByLabelText(/Ver solo este evento/));
    });

    const getSpy = apiClient.get as jest.Mock;
    const reportCall = getSpy.mock.calls.filter((c) => c[0] === '/reports/admin/sales').pop();
    expect(reportCall![1].params.eventId).toBe('event-1');
    expect((await findAllByText(/Festival Global/)).length).toBeGreaterThan(0);

    await act(async () => {
      fireEvent.press(getByLabelText('Quitar filtro de evento'));
    });

    const lastCall = (apiClient.get as jest.Mock).mock.calls.filter((c) => c[0] === '/reports/admin/sales').pop();
    expect(lastCall![1].params.eventId).toBeUndefined();
    expect(queryByLabelText('Quitar filtro de evento')).toBeNull();
  });

  it('el selector de evento está deshabilitado (con hint) antes de generar el primer reporte', async () => {
    mockApiRoutes();
    const { getByLabelText, findByText } = render(<AdminSalesReportScreen />);

    expect(await findByText('Generá el reporte para elegir un evento')).toBeTruthy();

    // Tocarlo sin reporte no abre nada útil: no hay opciones que listar.
    fireEvent.press(getByLabelText('Elegir evento'));
  });

  it('el selector de evento permite elegir un evento fuera del Top 5 (event-2 no está en topEventos)', async () => {
    mockApiRoutes();
    const { getByTestId, getByText, findByText, getByLabelText, queryByText } = render(<AdminSalesReportScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Importe emitido')).toBeTruthy();

    // event-2 ("Otro Festival") viene en filtrosDisponibles.eventos pero nunca en topEventos.
    expect(queryByText('Otro Festival')).toBeNull();

    fireEvent.press(getByLabelText('Elegir evento'));
    await act(async () => {
      fireEvent.press(await findByText('Otro Festival'));
    });

    const getSpy = apiClient.get as jest.Mock;
    const reportCall = getSpy.mock.calls.filter((c) => c[0] === '/reports/admin/sales').pop();
    expect(reportCall![1].params.eventId).toBe('event-2');
  });

  it('tocar una barra del Top 5 y usar el selector para el mismo evento producen el mismo request', async () => {
    mockApiRoutes();
    const { getByTestId, getByText, findByText, findAllByText, getByLabelText } = render(<AdminSalesReportScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Importe emitido')).toBeTruthy();

    await act(async () => {
      fireEvent.press(getByLabelText(/Ver solo este evento/));
    });
    const getSpy = apiClient.get as jest.Mock;
    const desdeTop5 = getSpy.mock.calls.filter((c) => c[0] === '/reports/admin/sales').pop()![1].params;

    await act(async () => {
      fireEvent.press(getByLabelText('Quitar filtro de evento'));
    });

    fireEvent.press(getByLabelText('Elegir evento'));
    await act(async () => {
      const matches = await findAllByText('Festival Global');
      fireEvent.press(matches[matches.length - 1]); // la fila del picker es la última coincidencia en el árbol
    });
    const desdeSelector = getSpy.mock.calls.filter((c) => c[0] === '/reports/admin/sales').pop()![1].params;

    expect(desdeSelector).toEqual(desdeTop5);
  });

  it('el reporte y sus selectores nunca muestran identificadores internos (eventoId, organizadorPersonaId)', async () => {
    mockApiRoutes({ usuarios: [organizador()] });
    const { getByTestId, getByText, findByText, getByLabelText, toJSON } = render(<AdminSalesReportScreen />);

    fireEvent.press(getByLabelText('Elegir organizador'));
    fireEvent.press(await findByText('organizador@test.com'));

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Importe emitido')).toBeTruthy();

    fireEvent.press(getByLabelText('Elegir evento'));
    expect(await findByText('Otro Festival')).toBeTruthy();

    const serialized = JSON.stringify(toJSON());
    expect(serialized).not.toContain('event-1');
    expect(serialized).not.toContain('event-2');
    expect(serialized).not.toContain('persona-org-1');
  });

  it('exporta el PDF con expo-print/expo-sharing incluyendo el organizador', async () => {
    mockApiRoutes({ usuarios: [organizador()] });
    mockPrintToFileAsync.mockResolvedValue({ uri: 'file://reporte.pdf' });
    mockIsAvailableAsync.mockResolvedValue(true);
    mockShareAsync.mockResolvedValue(undefined);

    const { getByTestId, getByText, findByText, getByLabelText } = render(<AdminSalesReportScreen />);
    fireEvent.press(getByLabelText('Elegir organizador'));
    fireEvent.press(await findByText('organizador@test.com'));

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Importe emitido')).toBeTruthy();

    await act(async () => {
      fireEvent.press(getByText('Generar y compartir PDF'));
    });

    expect(mockShareAsync).toHaveBeenCalledTimes(1);
    const html = mockPrintToFileAsync.mock.calls[0][0].html as string;
    expect(html).toContain('Festival Global');
    expect(html).toContain('organizador@test.com');
  });

  it('doble toque en "Generar y compartir PDF" no genera dos exportaciones simultáneas', async () => {
    mockApiRoutes();
    let resolvePrint: (value: { uri: string }) => void = () => {};
    mockPrintToFileAsync.mockImplementation(() => new Promise((resolve) => { resolvePrint = resolve; }));
    mockIsAvailableAsync.mockResolvedValue(true);
    mockShareAsync.mockResolvedValue(undefined);

    const { getByTestId, getByText, findByText, queryByText } = render(<AdminSalesReportScreen />);
    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Importe emitido')).toBeTruthy();

    fireEvent.press(getByText('Generar y compartir PDF'));
    expect(queryByText('Generar y compartir PDF')).toBeNull();

    await act(async () => {
      resolvePrint({ uri: 'file://reporte.pdf' });
    });

    expect(mockPrintToFileAsync).toHaveBeenCalledTimes(1);
  });
});
