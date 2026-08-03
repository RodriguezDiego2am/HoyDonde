import React from 'react';
import { fireEvent, render } from '@testing-library/react-native';

jest.mock('expo-router', () => ({
  router: { back: jest.fn() },
}));

const mockGetMyAssignedEvents = jest.fn();
jest.mock('@/services/controlAsignacionService', () => ({
  controlAsignacionService: { getMyAssignedEvents: (...args: unknown[]) => mockGetMyAssignedEvents(...args) },
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { ApiError } from '@/services/apiError';
// eslint-disable-next-line import/first
import ControlAssignedEventsScreen from './ControlAssignedEventsScreen';

describe('ControlAssignedEventsScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('estado de carga inicial', () => {
    mockGetMyAssignedEvents.mockReturnValue(new Promise(() => {}));
    const { queryByText } = render(<ControlAssignedEventsScreen />);
    expect(queryByText('Mis eventos')).toBeTruthy();
  });

  it('llama a GET /events/control/me y muestra los eventos asignados con su estado', async () => {
    mockGetMyAssignedEvents.mockResolvedValueOnce([
      {
        eventId: 'evento-1',
        nombre: 'Festival de Verano',
        ubicacion: 'Parque Central',
        fechaInicio: '2090-12-01T22:00:00Z',
        fechaFin: '2090-12-02T04:00:00Z',
        estado: 'Publicado',
      },
    ]);

    const { findByText } = render(<ControlAssignedEventsScreen />);

    expect(await findByText('Festival de Verano')).toBeTruthy();
    expect(await findByText('Parque Central')).toBeTruthy();
    expect(await findByText('PUBLICADO')).toBeTruthy();
    expect(mockGetMyAssignedEvents).toHaveBeenCalledTimes(1);
  });

  it('estado vacío cuando el Control no tiene eventos asignados', async () => {
    mockGetMyAssignedEvents.mockResolvedValueOnce([]);
    const { findByText } = render(<ControlAssignedEventsScreen />);

    expect(await findByText('Todavía no te asignaron a ningún evento.')).toBeTruthy();
  });

  it('estado de error con reintento', async () => {
    mockGetMyAssignedEvents.mockRejectedValueOnce(new Error('network down'));
    const { findByText } = render(<ControlAssignedEventsScreen />);

    expect(await findByText('No se pudieron cargar tus eventos asignados. Verificá tu conexión.')).toBeTruthy();

    mockGetMyAssignedEvents.mockResolvedValueOnce([]);
    fireEvent.press(await findByText('Reintentar'));

    expect(await findByText('Todavía no te asignaron a ningún evento.')).toBeTruthy();
  });

  it('mapea un 403 a un mensaje de permisos', async () => {
    mockGetMyAssignedEvents.mockRejectedValueOnce(new ApiError({ code: 'FORBIDDEN', message: 'no', traceId: 't' }, 403));
    const { findByText } = render(<ControlAssignedEventsScreen />);

    expect(await findByText('Tu cuenta no tiene permiso para ver eventos de Control.')).toBeTruthy();
  });

  it('no incluye ningún control de escaneo/validación de QR todavía', async () => {
    mockGetMyAssignedEvents.mockResolvedValueOnce([
      {
        eventId: 'evento-1',
        nombre: 'Festival de Verano',
        ubicacion: 'Parque Central',
        fechaInicio: '2090-12-01T22:00:00Z',
        fechaFin: '2090-12-02T04:00:00Z',
        estado: 'Publicado',
      },
    ]);

    const { findByText, queryByText } = render(<ControlAssignedEventsScreen />);
    await findByText('Festival de Verano');

    expect(queryByText(/escanear/i)).toBeNull();
    expect(queryByText(/validar/i)).toBeNull();
    expect(queryByText(/QR/i)).toBeNull();
  });
});
