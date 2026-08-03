import React from 'react';
import { fireEvent, render, waitFor } from '@testing-library/react-native';

jest.mock('../../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

const mockReplace = jest.fn();
const mockBack = jest.fn();
let mockParams: { id?: string } = {};
jest.mock('expo-router', () => ({
  router: {
    replace: (...args: unknown[]) => mockReplace(...args),
    back: (...args: unknown[]) => mockBack(...args),
  },
  useLocalSearchParams: () => mockParams,
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { apiClient, ApiError, EventResponse } from '@/services/APIService';
// eslint-disable-next-line import/first
import { parseLocalDateTime, splitIsoToLocalParts, toUtcIso } from '@/utils/datetime';
// eslint-disable-next-line import/first
import OrganizerEventFormScreen from './OrganizerEventFormScreen';

function evento(overrides: Partial<EventResponse> = {}): EventResponse {
  return {
    id: 'evento-1',
    nombre: 'Festival de Verano',
    descripcion: 'Un evento de ejemplo',
    fechaInicio: '2090-12-01T22:00:00.000Z',
    fechaFin: '2090-12-02T04:00:00.000Z',
    ubicacion: 'Parque Central',
    categoria: 'Musica',
    estado: 'Borrador',
    ticketGroups: [{ id: 'tipo-general', nombre: 'General', precio: 5000, cantidadDisponible: 100 }],
    ...overrides,
  };
}

const FUTURE_INICIO_ISO = toUtcIso(parseLocalDateTime('01/01/2090', '22:00')!);
const FUTURE_FIN_ISO = toUtcIso(parseLocalDateTime('02/01/2090', '04:00')!);

/** Completa los segmentos de fecha/hora del componente SegmentedDateTimeField por su testIDPrefix. */
function fillDateTime(getByTestId: ReturnType<typeof render>['getByTestId'], prefix: string, ddmmyyyy: string, hhmm: string) {
  const [dd, mm, yyyy] = ddmmyyyy.split('/');
  const [hh, min] = hhmm.split(':');
  fireEvent.changeText(getByTestId(`${prefix}-day`), dd);
  fireEvent.changeText(getByTestId(`${prefix}-month`), mm);
  fireEvent.changeText(getByTestId(`${prefix}-year`), yyyy);
  fireEvent.changeText(getByTestId(`${prefix}-hour`), hh);
  fireEvent.changeText(getByTestId(`${prefix}-minute`), min);
}

describe('OrganizerEventFormScreen — crear evento', () => {
  beforeEach(() => {
    jest.restoreAllMocks();
    mockReplace.mockClear();
    mockBack.mockClear();
    mockParams = {};
  });

  it('exige nombre, ubicación, fechas y al menos un tipo de ticket', () => {
    const { getByText } = render(<OrganizerEventFormScreen />);

    fireEvent.press(getByText('Crear evento'));

    expect(getByText('El nombre del evento es obligatorio.')).toBeTruthy();
    expect(getByText('La ubicación del evento es obligatoria.')).toBeTruthy();
    expect(getByText('Ingresá una fecha y hora de inicio válidas (DD/MM/AAAA y HH:MM).')).toBeTruthy();
    expect(getByText('Ingresá una fecha y hora de fin válidas (DD/MM/AAAA y HH:MM).')).toBeTruthy();
    // La fila de ticket por defecto está vacía: cae en errores por fila, no en el genérico.
    expect(getByText('El nombre del tipo de ticket es obligatorio.')).toBeTruthy();
  });

  it('rechaza una fecha de inicio que no es futura', () => {
    const { getByText, getByTestId } = render(<OrganizerEventFormScreen />);

    fillDateTime(getByTestId, 'fecha-inicio', '01/01/2020', '10:00');

    fireEvent.press(getByText('Crear evento'));

    expect(getByText('La fecha de inicio debe ser futura.')).toBeTruthy();
  });

  it('crea el evento con fechas convertidas a UTC y el tipo de ticket cargado', async () => {
    const postSpy = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: evento({ id: 'evento-nuevo' }) } as any);

    const { getByText, getByPlaceholderText, getAllByPlaceholderText, getByLabelText, getByTestId } = render(
      <OrganizerEventFormScreen />
    );

    fireEvent.changeText(getByPlaceholderText('Nombre del evento'), 'Festival de Verano');
    fireEvent.changeText(getByPlaceholderText('Dirección o lugar'), 'Parque Central');
    fireEvent.press(getByLabelText('Categoría Música'));

    fillDateTime(getByTestId, 'fecha-inicio', '01/01/2090', '22:00');
    fillDateTime(getByTestId, 'fecha-fin', '02/01/2090', '04:00');

    fireEvent.changeText(getByPlaceholderText('General, VIP...'), 'General');
    const numericInputs = getAllByPlaceholderText('0');
    fireEvent.changeText(numericInputs[0], '5000');
    fireEvent.changeText(numericInputs[1], '100');

    fireEvent.press(getByText('Crear evento'));

    await waitFor(() =>
      expect(postSpy).toHaveBeenCalledWith('/events', {
        nombre: 'Festival de Verano',
        descripcion: '',
        ubicacion: 'Parque Central',
        categoria: 'Musica',
        fechaInicio: FUTURE_INICIO_ISO,
        fechaFin: FUTURE_FIN_ISO,
        ticketGroups: [{ nombre: 'General', precio: 5000, cantidadDisponible: 100 }],
      })
    );

    await waitFor(() =>
      expect(mockReplace).toHaveBeenCalledWith({ pathname: '/organizer/[id]', params: { id: 'evento-nuevo' } })
    );
  });

  it('evita el doble envío mientras la creación está en curso', async () => {
    let resolveCreate!: (value: unknown) => void;
    const postSpy = jest.spyOn(apiClient, 'post').mockReturnValue(
      new Promise((resolve) => {
        resolveCreate = resolve;
      }) as any
    );

    const { getByText, getByPlaceholderText, getAllByPlaceholderText, getByTestId } = render(
      <OrganizerEventFormScreen />
    );

    fireEvent.changeText(getByPlaceholderText('Nombre del evento'), 'Festival de Verano');
    fireEvent.changeText(getByPlaceholderText('Dirección o lugar'), 'Parque Central');
    fillDateTime(getByTestId, 'fecha-inicio', '01/01/2090', '22:00');
    fillDateTime(getByTestId, 'fecha-fin', '02/01/2090', '04:00');
    fireEvent.changeText(getByPlaceholderText('General, VIP...'), 'General');
    const numericInputs = getAllByPlaceholderText('0');
    fireEvent.changeText(numericInputs[0], '5000');
    fireEvent.changeText(numericInputs[1], '100');

    const submitButton = getByText('Crear evento');
    fireEvent.press(submitButton);
    fireEvent.press(submitButton);
    fireEvent.press(submitButton);

    expect(postSpy).toHaveBeenCalledTimes(1);
    resolveCreate({ data: evento() });
    await waitFor(() => expect(mockReplace).toHaveBeenCalledTimes(1));
  });

  it('mapea VALIDATION_ERROR a un mensaje de campo claro', async () => {
    jest
      .spyOn(apiClient, 'post')
      .mockRejectedValue(
        new ApiError(
          { code: 'VALIDATION_ERROR', message: 'inválido', traceId: 't', errors: { Ubicacion: ['La ubicación del evento es obligatoria.'] } },
          400
        )
      );

    const { getByText, getByPlaceholderText, getAllByPlaceholderText, getByTestId, findByText } = render(
      <OrganizerEventFormScreen />
    );

    fireEvent.changeText(getByPlaceholderText('Nombre del evento'), 'Festival de Verano');
    fireEvent.changeText(getByPlaceholderText('Dirección o lugar'), 'Parque Central');
    fillDateTime(getByTestId, 'fecha-inicio', '01/01/2090', '22:00');
    fillDateTime(getByTestId, 'fecha-fin', '02/01/2090', '04:00');
    fireEvent.changeText(getByPlaceholderText('General, VIP...'), 'General');
    const numericInputs = getAllByPlaceholderText('0');
    fireEvent.changeText(numericInputs[0], '5000');
    fireEvent.changeText(numericInputs[1], '100');

    fireEvent.press(getByText('Crear evento'));

    expect(await findByText('La ubicación del evento es obligatoria.')).toBeTruthy();
  });
});

describe('OrganizerEventFormScreen — editar evento', () => {
  beforeEach(() => {
    jest.restoreAllMocks();
    mockReplace.mockClear();
    mockBack.mockClear();
    mockParams = { id: 'evento-1' };
  });

  it('precarga los datos del evento cuando está en Borrador, incluidos los segmentos de fecha/hora', async () => {
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento() } as any);

    const { findByDisplayValue, findByTestId } = render(<OrganizerEventFormScreen />);

    expect(await findByDisplayValue('Festival de Verano')).toBeTruthy();
    expect(await findByDisplayValue('Parque Central')).toBeTruthy();
    expect(await findByDisplayValue('General')).toBeTruthy();
    expect(await findByDisplayValue('5000')).toBeTruthy();

    // splitIsoToLocalParts convierte a hora LOCAL del entorno que corre el test: se compara
    // contra la misma función, no contra el string UTC crudo del fixture.
    const { date, time } = splitIsoToLocalParts('2090-12-01T22:00:00.000Z');
    const [expectedDay, expectedMonth, expectedYear] = date.split('/');
    const [expectedHour, expectedMinute] = time.split(':');

    expect((await findByTestId('fecha-inicio-day')).props.value).toBe(expectedDay);
    expect((await findByTestId('fecha-inicio-month')).props.value).toBe(expectedMonth);
    expect((await findByTestId('fecha-inicio-year')).props.value).toBe(expectedYear);
    expect((await findByTestId('fecha-inicio-hour')).props.value).toBe(expectedHour);
    expect((await findByTestId('fecha-inicio-minute')).props.value).toBe(expectedMinute);
  });

  it('bloquea la edición cuando el evento ya no está en Borrador', async () => {
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento({ estado: 'Publicado' }) } as any);

    const { findByText, queryByText } = render(<OrganizerEventFormScreen />);

    expect(await findByText('Este evento ya no está en Borrador: no se puede editar.')).toBeTruthy();
    expect(queryByText('Guardar cambios')).toBeNull();
  });

  it('guarda los cambios con PUT /events/{id} reemplazando la colección de tickets', async () => {
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento() } as any);
    const putSpy = jest.spyOn(apiClient, 'put').mockResolvedValue({ data: evento() } as any);

    const { findByDisplayValue, getByText, getByPlaceholderText } = render(<OrganizerEventFormScreen />);
    await findByDisplayValue('Festival de Verano');

    fireEvent.changeText(getByPlaceholderText('Nombre del evento'), 'Festival de Verano (editado)');
    fireEvent.press(getByText('Guardar cambios'));

    await waitFor(() =>
      expect(putSpy).toHaveBeenCalledWith(
        '/events/evento-1',
        expect.objectContaining({ nombre: 'Festival de Verano (editado)' })
      )
    );
    expect(mockReplace).toHaveBeenCalledWith({ pathname: '/organizer/[id]', params: { id: 'evento-1' } });
  });

  it('muestra error con reintento si falla la carga del evento a editar', async () => {
    jest.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('network down'));
    const { findByText } = render(<OrganizerEventFormScreen />);

    expect(await findByText('No se pudo cargar el evento. Verificá tu conexión.')).toBeTruthy();

    jest.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: evento() } as any);
    fireEvent.press(await findByText('Reintentar'));

    expect(await findByText('Guardar cambios')).toBeTruthy();
  });
});
