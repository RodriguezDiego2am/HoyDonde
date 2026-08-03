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
import type { ReporteAdminEventosResponse } from '@/services/reportService';
// eslint-disable-next-line import/first
import AdminEventsReportScreen from './AdminEventsReportScreen';
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

const REPORT: ReporteAdminEventosResponse = {
  fechaDesde: '2026-01-01T00:00:00Z',
  fechaHasta: '2026-02-01T00:00:00Z',
  aclaracionImporte: 'El MVP no procesa pagos reales.',
  resumen: {
    cantidadEventos: 1,
    capacidadInicial: 10,
    stockDisponible: 5,
    entradasEmitidas: 5,
    entradasUsadas: 2,
    entradasAnuladas: 0,
    entradasPendientes: 3,
    porcentajeOcupacion: 50,
    porcentajeAsistencia: 40,
    porcentajeUtilizacion: 20,
    importeEmitido: 500,
  },
  eventos: [
    {
      eventId: 'event-1',
      nombre: 'Festival Global',
      ubicacion: 'Buenos Aires',
      categoria: 'Musica',
      estado: 'Publicado',
      fechaInicio: '2026-01-10T22:00:00Z',
      fechaFin: '2026-01-11T04:00:00Z',
      capacidadInicial: 10,
      stockDisponible: 5,
      entradasEmitidas: 5,
      entradasUsadas: 2,
      entradasAnuladas: 0,
      entradasPendientes: 3,
      porcentajeOcupacion: 50,
      porcentajeAsistencia: 40,
      porcentajeUtilizacion: 20,
      importeEmitido: 500,
      tiposDeEntrada: [],
      organizadorPersonaId: 'persona-org-1',
    },
  ],
};

function mockApiRoutes(opts: { usuarios?: UsuarioResumenResponse[]; report?: ReporteAdminEventosResponse; reportError?: unknown } = {}) {
  jest.spyOn(apiClient, 'get').mockImplementation(async (url: string) => {
    if (url === '/security/usuarios') return { data: opts.usuarios ?? [] };
    if (url === '/reports/admin/events') {
      if (opts.reportError) throw opts.reportError;
      return { data: opts.report ?? REPORT };
    }
    throw new Error(`unexpected url ${url}`);
  });
}

function fillValidRange(getByTestId: (id: string) => any) {
  fireEvent.changeText(getByTestId('admin-reporte-fecha-desde-day'), '01012026');
  fireEvent.changeText(getByTestId('admin-reporte-fecha-hasta-day'), '01022026');
}

describe('AdminEventsReportScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockHasAccion = () => true;
  });

  it('exige el rango Desde/Hasta antes de llamar a la API', async () => {
    mockApiRoutes();
    const { getByText, findByText } = render(<AdminEventsReportScreen />);

    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText(/obligatorios para este reporte/)).toBeTruthy();
  });

  it('con un rango válido, llama a GET /reports/admin/events en UTC y muestra el resumen', async () => {
    mockApiRoutes();
    const { getByTestId, getByText, findByText } = render(<AdminEventsReportScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('Eventos (1)')).toBeTruthy();
    const getSpy = apiClient.get as jest.Mock;
    const reportCall = getSpy.mock.calls.find((c) => c[0] === '/reports/admin/events');
    expect(reportCall![1].params.fechaDesde).toBe(toUtcIso(startOfLocalDay(parseLocalDate('01/01/2026')!)));
    expect(reportCall![1].params.fechaHasta).toBe(toUtcIso(nextLocalDayExclusive(parseLocalDate('01/02/2026')!)));
    expect(reportCall![1].params.organizadorPersonaId).toBeUndefined();
  });

  it('elegir un organizador manda su personaId como filtro y lo muestra por email en la UI', async () => {
    mockApiRoutes({ usuarios: [organizador()] });
    const { getByTestId, getByText, findByText, getByLabelText } = render(<AdminEventsReportScreen />);

    fireEvent.press(getByLabelText('Elegir organizador'));
    fireEvent.press(await findByText('organizador@test.com'));

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('Eventos (1)')).toBeTruthy();
    const getSpy = apiClient.get as jest.Mock;
    const reportCall = getSpy.mock.calls.find((c) => c[0] === '/reports/admin/events');
    expect(reportCall![1].params.organizadorPersonaId).toBe('persona-org-1');
  });

  it('oculta el selector de organizador cuando la sesión no tiene USUARIO_VER_PERMISOS_EFECTIVOS', async () => {
    mockHasAccion = (accion) => accion === 'REPORTE_VER_GLOBAL';
    mockApiRoutes();
    const { queryByLabelText } = render(<AdminEventsReportScreen />);

    expect(queryByLabelText('Elegir organizador')).toBeNull();
  });

  it('muestra error con reintento si la API falla', async () => {
    mockApiRoutes({ reportError: new Error('network down') });
    const { getByTestId, getByText, findByText } = render(<AdminEventsReportScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('No se pudo generar el reporte. Verificá tu conexión.')).toBeTruthy();
  });

  it('exporta el PDF con expo-print/expo-sharing incluyendo el evento y el organizador', async () => {
    mockApiRoutes({ usuarios: [organizador()] });
    mockPrintToFileAsync.mockResolvedValue({ uri: 'file://reporte.pdf' });
    mockIsAvailableAsync.mockResolvedValue(true);
    mockShareAsync.mockResolvedValue(undefined);

    const { getByTestId, getByText, findByText } = render(<AdminEventsReportScreen />);
    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Eventos (1)')).toBeTruthy();

    await act(async () => {
      fireEvent.press(getByText('Generar y compartir PDF'));
    });

    expect(mockShareAsync).toHaveBeenCalledTimes(1);
    const html = mockPrintToFileAsync.mock.calls[0][0].html as string;
    expect(html).toContain('Festival Global');
    expect(html).toContain('organizador@test.com');
  });
});
