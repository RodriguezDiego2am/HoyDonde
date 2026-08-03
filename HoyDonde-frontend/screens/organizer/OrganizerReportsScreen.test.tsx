import React from 'react';
import { act, fireEvent, render } from '@testing-library/react-native';

jest.mock('../../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

const mockBack = jest.fn();
jest.mock('expo-router', () => ({
  router: { push: jest.fn(), back: (...args: unknown[]) => mockBack(...args) },
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
import type { EventResponse } from '@/services/APIService';
// eslint-disable-next-line import/first
import type { ReporteEventosResponse } from '@/services/reportService';
// eslint-disable-next-line import/first
import OrganizerReportsScreen from './OrganizerReportsScreen';
// eslint-disable-next-line import/first
import { nextLocalDayExclusive, parseLocalDate, startOfLocalDay, toUtcIso } from '@/utils/datetime';

function evento(overrides: Partial<EventResponse> = {}): EventResponse {
  return {
    id: 'event-1',
    nombre: 'Festival de Verano',
    descripcion: '',
    fechaInicio: '2026-06-01T22:00:00Z',
    fechaFin: '2026-06-02T04:00:00Z',
    ubicacion: 'Parque Central',
    categoria: 'Musica',
    estado: 'Publicado',
    ticketGroups: [{ id: 'tipo-1', nombre: 'General', precio: 100, cantidadDisponible: 10 }],
    ...overrides,
  };
}

const REPORT: ReporteEventosResponse = {
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
      nombre: 'Festival de Verano',
      ubicacion: 'Parque Central',
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
    },
  ],
};

function mockApiRoutes(opts: { myEvents?: EventResponse[]; report?: ReporteEventosResponse; reportError?: unknown } = {}) {
  jest.spyOn(apiClient, 'get').mockImplementation(async (url: string) => {
    if (url === '/events/organizer/me') return { data: opts.myEvents ?? [] };
    if (url === '/reports/organizer/events') {
      if (opts.reportError) throw opts.reportError;
      return { data: opts.report ?? REPORT };
    }
    throw new Error(`unexpected url ${url}`);
  });
}

function fillValidRange(getByTestId: (id: string) => any) {
  fireEvent.changeText(getByTestId('reporte-fecha-desde-day'), '01012026');
  fireEvent.changeText(getByTestId('reporte-fecha-hasta-day'), '01022026');
}

describe('OrganizerReportsScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('exige el rango Desde/Hasta antes de llamar a la API', async () => {
    mockApiRoutes();
    const { getByText, findByText } = render(<OrganizerReportsScreen />);

    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText(/obligatorios para este reporte/)).toBeTruthy();
  });

  it('rechaza un rango invertido antes de llamar a la API', async () => {
    mockApiRoutes();
    const { getByTestId, getByText, findByText } = render(<OrganizerReportsScreen />);

    fireEvent.changeText(getByTestId('reporte-fecha-desde-day'), '01022026');
    fireEvent.changeText(getByTestId('reporte-fecha-hasta-day'), '01012026');

    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('"Desde" no puede ser posterior a "Hasta".')).toBeTruthy();
  });

  it('con un rango válido, llama a GET /reports/organizer/events en UTC y muestra el resumen', async () => {
    mockApiRoutes();
    const { getByTestId, getByText, findByText } = render(<OrganizerReportsScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('Eventos (1)')).toBeTruthy();
    const getSpy = apiClient.get as jest.Mock;
    const reportCall = getSpy.mock.calls.find((c) => c[0] === '/reports/organizer/events');
    expect(reportCall).toBeTruthy();
    expect(reportCall![1].params.fechaDesde).toBe(toUtcIso(startOfLocalDay(parseLocalDate('01/01/2026')!)));
    expect(reportCall![1].params.fechaHasta).toBe(toUtcIso(nextLocalDayExclusive(parseLocalDate('01/02/2026')!)));
  });

  it('muestra error con reintento si la API falla', async () => {
    mockApiRoutes({ reportError: new Error('network down') });
    const { getByTestId, getByText, findByText } = render(<OrganizerReportsScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('No se pudo generar el reporte. Verificá tu conexión.')).toBeTruthy();
  });

  it('"Limpiar" resetea filtros y resultado', async () => {
    mockApiRoutes();
    const { getByTestId, getByText, findByText, queryByText } = render(<OrganizerReportsScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Eventos (1)')).toBeTruthy();

    fireEvent.press(getByText('Limpiar'));

    expect(queryByText('Eventos (1)')).toBeNull();
  });

  it('elegir un evento habilita el filtro de tipo de entrada, y quitarlo lo oculta de nuevo', async () => {
    mockApiRoutes({ myEvents: [evento()] });
    const { findByText, getByLabelText, queryByText } = render(<OrganizerReportsScreen />);

    fireEvent.press(getByLabelText('Elegir evento'));
    fireEvent.press(await findByText('Festival de Verano'));

    expect(await findByText('General')).toBeTruthy(); // chip del tipo de entrada

    fireEvent.press(getByLabelText('Quitar evento seleccionado'));

    expect(queryByText('General')).toBeNull();
  });

  it('exporta el PDF con expo-print/expo-sharing usando el reporte visible', async () => {
    mockApiRoutes();
    mockPrintToFileAsync.mockResolvedValue({ uri: 'file://reporte.pdf' });
    mockIsAvailableAsync.mockResolvedValue(true);
    mockShareAsync.mockResolvedValue(undefined);

    const { getByTestId, getByText, findByText } = render(<OrganizerReportsScreen />);
    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Eventos (1)')).toBeTruthy();

    await act(async () => {
      fireEvent.press(getByText('Generar y compartir PDF'));
    });

    expect(mockPrintToFileAsync).toHaveBeenCalledTimes(1);
    expect(mockShareAsync).toHaveBeenCalledTimes(1);
    const html = mockPrintToFileAsync.mock.calls[0][0].html as string;
    expect(html).toContain('Festival de Verano');
  });

  it('doble toque en "Generar y compartir PDF" no genera dos exportaciones simultáneas', async () => {
    mockApiRoutes();
    let resolvePrint: (value: { uri: string }) => void = () => {};
    mockPrintToFileAsync.mockImplementation(() => new Promise((resolve) => { resolvePrint = resolve; }));
    mockIsAvailableAsync.mockResolvedValue(true);
    mockShareAsync.mockResolvedValue(undefined);

    const { getByTestId, getByText, findByText, queryByText } = render(<OrganizerReportsScreen />);
    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Eventos (1)')).toBeTruthy();

    fireEvent.press(getByText('Generar y compartir PDF'));
    // Mientras la primera exportación está en vuelo, el botón pasa a estado "loading" (spinner,
    // sin el label): no queda ningún control con el mismo texto para un segundo toque.
    expect(queryByText('Generar y compartir PDF')).toBeNull();

    await act(async () => {
      resolvePrint({ uri: 'file://reporte.pdf' });
    });

    expect(mockPrintToFileAsync).toHaveBeenCalledTimes(1);
  });
});
