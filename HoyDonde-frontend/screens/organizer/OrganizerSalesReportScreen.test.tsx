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
import type { VentasReporteResponse } from '@/services/reportService';
// eslint-disable-next-line import/first
import OrganizerSalesReportScreen from './OrganizerSalesReportScreen';
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
    eventoConMayorImporte: { eventoId: 'event-1', eventoNombre: 'Festival de Verano', importeEmitido: 500, entradasEmitidas: 5 },
    eventoConMasEntradas: { eventoId: 'event-1', eventoNombre: 'Festival de Verano', importeEmitido: 500, entradasEmitidas: 5 },
  },
  serieTemporal: [
    { periodoDesde: '2026-01-05T00:00:00Z', periodoHasta: '2026-01-06T00:00:00Z', etiqueta: '05/01', cantidadCompras: 3, entradasEmitidas: 5, importeEmitido: 500 },
  ],
  topEventos: [{ eventoId: 'event-1', eventoNombre: 'Festival de Verano', cantidadCompras: 3, entradasEmitidas: 5, importeEmitido: 500, importePromedioCompra: 166.67 }],
  porCategoria: [{ categoria: 'Musica', cantidadCompras: 3, entradasEmitidas: 5, importeEmitido: 500, porcentajeDelImporteTotal: 100 }],
  porTipoEntrada: [],
  filtrosDisponibles: {
    eventos: [
      { id: 'event-1', nombre: 'Festival de Verano' },
      { id: 'event-2', nombre: 'Otro Evento' },
    ],
    tiposEntrada: [{ id: 'tipo-1', nombre: 'General' }],
  },
};

function mockApiRoutes(opts: { myEvents?: EventResponse[]; report?: VentasReporteResponse; reportError?: unknown } = {}) {
  jest.spyOn(apiClient, 'get').mockImplementation(async (url: string) => {
    if (url === '/events/organizer/me') return { data: opts.myEvents ?? [] };
    if (url === '/reports/organizer/sales') {
      if (opts.reportError) throw opts.reportError;
      return { data: opts.report ?? REPORT };
    }
    throw new Error(`unexpected url ${url}`);
  });
}

function fillValidRange(getByTestId: (id: string) => any) {
  fireEvent.changeText(getByTestId('ventas-fecha-desde-day'), '01012026');
  fireEvent.changeText(getByTestId('ventas-fecha-hasta-day'), '01022026');
}

describe('OrganizerSalesReportScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('exige el rango Desde/Hasta antes de llamar a la API', async () => {
    mockApiRoutes();
    const { getByText, findByText } = render(<OrganizerSalesReportScreen />);

    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText(/obligatorios para este reporte/)).toBeTruthy();
  });

  it('rechaza un rango invertido antes de llamar a la API', async () => {
    mockApiRoutes();
    const { getByTestId, getByText, findByText } = render(<OrganizerSalesReportScreen />);

    fireEvent.changeText(getByTestId('ventas-fecha-desde-day'), '01022026');
    fireEvent.changeText(getByTestId('ventas-fecha-hasta-day'), '01012026');

    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('"Desde" no puede ser posterior a "Hasta".')).toBeTruthy();
  });

  it('con un rango válido, llama a GET /reports/organizer/sales en UTC (Hasta exclusiva, día siguiente local) sin organizadorPersonaId', async () => {
    mockApiRoutes();
    const { getByTestId, getByText, findByText } = render(<OrganizerSalesReportScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('Importe emitido')).toBeTruthy();
    const getSpy = apiClient.get as jest.Mock;
    const reportCall = getSpy.mock.calls.find((c) => c[0] === '/reports/organizer/sales');
    expect(reportCall).toBeTruthy();
    expect(reportCall![1].params.fechaDesde).toBe(toUtcIso(startOfLocalDay(parseLocalDate('01/01/2026')!)));
    expect(reportCall![1].params.fechaHasta).toBe(toUtcIso(nextLocalDayExclusive(parseLocalDate('01/02/2026')!)));
    expect(reportCall![1].params).not.toHaveProperty('organizadorPersonaId');
  });

  it('muestra el resumen (lectura rápida) luego de aplicar filtros', async () => {
    mockApiRoutes();
    const { getByTestId, getByText, findByText } = render(<OrganizerSalesReportScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('Importe emitido')).toBeTruthy();
    expect(await findByText('Compras')).toBeTruthy();
    expect(await findByText('Clientes únicos')).toBeTruthy();
  });

  it('muestra error con reintento si la API falla', async () => {
    mockApiRoutes({ reportError: new Error('network down') });
    const { getByTestId, getByText, findByText } = render(<OrganizerSalesReportScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('No se pudo generar el reporte. Verificá tu conexión.')).toBeTruthy();
  });

  it('"Limpiar" resetea filtros y resultado', async () => {
    mockApiRoutes();
    const { getByTestId, getByText, findByText, queryByText } = render(<OrganizerSalesReportScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Importe emitido')).toBeTruthy();

    fireEvent.press(getByText('Limpiar'));

    expect(queryByText('Importe emitido')).toBeNull();
  });

  it('refresh conserva los filtros aplicados (misma llamada, con el mismo rango)', async () => {
    mockApiRoutes();
    const { getByTestId, getByText, getByLabelText, findByText } = render(<OrganizerSalesReportScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Importe emitido')).toBeTruthy();

    const getSpy = apiClient.get as jest.Mock;
    getSpy.mockClear();

    await act(async () => {
      fireEvent.press(getByLabelText('Actualizar'));
    });

    const reportCall = getSpy.mock.calls.find((c) => c[0] === '/reports/organizer/sales');
    expect(reportCall![1].params.fechaDesde).toBe(toUtcIso(startOfLocalDay(parseLocalDate('01/01/2026')!)));
  });

  it('elegir un evento no muestra tipos de entrada hasta aplicar (derivados del backend), y quitar el evento los oculta de nuevo', async () => {
    mockApiRoutes({ myEvents: [evento()] });
    const { getByTestId, getByText, findByText, getByLabelText, queryByText } = render(<OrganizerSalesReportScreen />);

    fireEvent.press(getByLabelText('Elegir evento'));
    fireEvent.press(await findByText('Festival de Verano'));

    // Recién elegido, sin reporte generado para ese evento todavía: sin chips de tipo, con hint.
    expect(queryByText('General')).toBeNull();
    expect(await findByText(/Aplicá el filtro de evento/)).toBeTruthy();

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    // Una vez aplicado, los tipos de entrada vienen del backend (filtrosDisponibles.tiposEntrada).
    expect(await findByText('General')).toBeTruthy();

    fireEvent.press(getByLabelText('Quitar evento seleccionado'));

    expect(queryByText('General')).toBeNull();
  });

  it('cambiar de evento limpia el tipo de entrada incompatible previamente seleccionado', async () => {
    const eventoDos = evento({ id: 'event-2', nombre: 'Otro Evento', ticketGroups: [{ id: 'tipo-2', nombre: 'VIP', precio: 200, cantidadDisponible: 5 }] });
    mockApiRoutes({ myEvents: [evento(), eventoDos] });
    const { getByTestId, getByText, findByText, getByLabelText, queryByText } = render(<OrganizerSalesReportScreen />);

    fireEvent.press(getByLabelText('Elegir evento'));
    fireEvent.press(await findByText('Festival de Verano'));
    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('General')).toBeTruthy();
    fireEvent.press(getByLabelText('General'));

    // Cambiar de evento: el tipo elegido para el evento anterior deja de mostrarse.
    fireEvent.press(getByLabelText('Elegir evento'));
    fireEvent.press(await findByText('Otro Evento'));

    expect(queryByText('General')).toBeNull();
    expect(await findByText(/Aplicá el filtro de evento/)).toBeTruthy();
  });

  it('antes del primer reporte, el selector usa GET /events/organizer/me (eventos propios)', async () => {
    mockApiRoutes({ myEvents: [evento()] });
    const { findByText, getByLabelText } = render(<OrganizerSalesReportScreen />);

    fireEvent.press(getByLabelText('Elegir evento'));

    expect(await findByText('Festival de Verano')).toBeTruthy();
    const getSpy = apiClient.get as jest.Mock;
    expect(getSpy.mock.calls.some((c) => c[0] === '/events/organizer/me')).toBe(true);
  });

  it('después de un reporte válido, el selector usa filtrosDisponibles.eventos (un evento propio sin ventas en el período no se ofrece)', async () => {
    const eventoSinVentas = evento({ id: 'event-3', nombre: 'Evento sin ventas' });
    mockApiRoutes({ myEvents: [evento(), eventoSinVentas] });
    const { getByTestId, getByText, findByText, getByLabelText, queryByText } = render(<OrganizerSalesReportScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Importe emitido')).toBeTruthy();

    fireEvent.press(getByLabelText('Elegir evento'));

    // "Otro Evento" viene en filtrosDisponibles.eventos (REPORT fixture) aunque no esté en myEvents;
    // "Evento sin ventas" está en myEvents pero nunca en filtrosDisponibles.eventos, así que no se ofrece.
    expect(await findByText('Otro Evento')).toBeTruthy();
    expect(queryByText('Evento sin ventas')).toBeNull();
  });

  it('opciones incompatibles: si el evento aplicado deja de estar en filtrosDisponibles.eventos, se limpia evento+tipo y se vuelve a consultar sin acotar', async () => {
    const respuestaSinEseEvento: VentasReporteResponse = {
      ...REPORT,
      resumen: { ...REPORT.resumen, cantidadCompras: 0, entradasEmitidas: 0, importeEmitido: 0, eventoConMayorImporte: null, eventoConMasEntradas: null },
      topEventos: [],
      filtrosDisponibles: { eventos: [{ id: 'event-2', nombre: 'Otro Evento' }], tiposEntrada: [] },
    };

    jest.spyOn(apiClient, 'get').mockImplementation(async (url: string, config?: any) => {
      if (url === '/events/organizer/me') return { data: [evento()] };
      if (url === '/reports/organizer/sales') {
        // Primera consulta (con eventId=event-1): el evento ya no aparece en filtrosDisponibles ->
        // la pantalla debe limpiar el filtro y reconsultar sin eventId, que sí devuelve REPORT.
        if (config?.params?.eventId === 'event-1') return { data: respuestaSinEseEvento };
        return { data: REPORT };
      }
      throw new Error(`unexpected url ${url}`);
    });

    const { getByTestId, getByText, findByText, getByLabelText, queryByLabelText } = render(<OrganizerSalesReportScreen />);

    fireEvent.press(getByLabelText('Elegir evento'));
    fireEvent.press(await findByText('Festival de Verano'));
    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    // El filtro de evento se limpió: el picker vuelve a mostrar "Todos mis eventos" y ya no hay
    // botón para quitar un evento seleccionado.
    expect(await findByText('Importe emitido')).toBeTruthy(); // reporte final = REPORT (sin acotar)
    expect(queryByLabelText('Quitar evento seleccionado')).toBeNull();

    const getSpy = apiClient.get as jest.Mock;
    const reportCalls = getSpy.mock.calls.filter((c) => c[0] === '/reports/organizer/sales');
    expect(reportCalls.length).toBe(2); // la consulta original + la reconsulta sin eventId
    expect(reportCalls[0][1].params.eventId).toBe('event-1');
    expect(reportCalls[1][1].params.eventId).toBeUndefined();
  });

  it('el reporte nunca muestra identificadores internos (eventoId, clientePersonaId)', async () => {
    mockApiRoutes();
    const { getByTestId, getByText, findByText, toJSON } = render(<OrganizerSalesReportScreen />);

    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    await findByText('Importe emitido');

    const serialized = JSON.stringify(toJSON());
    expect(serialized).not.toContain('event-1');
  });

  it('exporta el PDF con expo-print/expo-sharing incluyendo filtros y métricas', async () => {
    mockApiRoutes();
    mockPrintToFileAsync.mockResolvedValue({ uri: 'file://reporte.pdf' });
    mockIsAvailableAsync.mockResolvedValue(true);
    mockShareAsync.mockResolvedValue(undefined);

    const { getByTestId, getByText, findByText } = render(<OrganizerSalesReportScreen />);
    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Importe emitido')).toBeTruthy();

    await act(async () => {
      fireEvent.press(getByText('Generar y compartir PDF'));
    });

    expect(mockPrintToFileAsync).toHaveBeenCalledTimes(1);
    expect(mockShareAsync).toHaveBeenCalledTimes(1);
    const html = mockPrintToFileAsync.mock.calls[0][0].html as string;
    expect(html).toContain('Festival de Verano');
    expect(html).toContain('El MVP no procesa pagos reales.');
  });

  it('doble toque en "Generar y compartir PDF" no genera dos exportaciones simultáneas', async () => {
    mockApiRoutes();
    let resolvePrint: (value: { uri: string }) => void = () => {};
    mockPrintToFileAsync.mockImplementation(() => new Promise((resolve) => { resolvePrint = resolve; }));
    mockIsAvailableAsync.mockResolvedValue(true);
    mockShareAsync.mockResolvedValue(undefined);

    const { getByTestId, getByText, findByText, queryByText } = render(<OrganizerSalesReportScreen />);
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

  it('sin sharing disponible, avisa que el PDF quedó guardado localmente', async () => {
    mockApiRoutes();
    mockPrintToFileAsync.mockResolvedValue({ uri: 'file://reporte.pdf' });
    mockIsAvailableAsync.mockResolvedValue(false);
    const alertSpy = jest.spyOn(require('react-native').Alert, 'alert').mockImplementation(() => {});

    const { getByTestId, getByText, findByText } = render(<OrganizerSalesReportScreen />);
    fillValidRange(getByTestId);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Importe emitido')).toBeTruthy();

    await act(async () => {
      fireEvent.press(getByText('Generar y compartir PDF'));
    });

    expect(mockShareAsync).not.toHaveBeenCalled();
    expect(alertSpy).toHaveBeenCalledWith('PDF generado', expect.stringContaining('file://reporte.pdf'));
  });
});
