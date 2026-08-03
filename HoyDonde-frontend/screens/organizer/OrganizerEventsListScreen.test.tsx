import React from 'react';
import { fireEvent, render } from '@testing-library/react-native';

jest.mock('../../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

const mockPush = jest.fn();
const mockBack = jest.fn();
jest.mock('expo-router', () => ({
  router: {
    push: (...args: unknown[]) => mockPush(...args),
    back: (...args: unknown[]) => mockBack(...args),
  },
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { apiClient, ApiError, EventResponse } from '@/services/APIService';
// eslint-disable-next-line import/first
import OrganizerEventsListScreen from './OrganizerEventsListScreen';

function evento(overrides: Partial<EventResponse> = {}): EventResponse {
  return {
    id: 'evento-1',
    nombre: 'Festival de Verano',
    descripcion: '',
    fechaInicio: '2026-12-01T22:00:00Z',
    fechaFin: '2026-12-02T04:00:00Z',
    ubicacion: 'Parque Central',
    categoria: 'Musica',
    estado: 'Borrador',
    ticketGroups: [],
    ...overrides,
  };
}

describe('OrganizerEventsListScreen', () => {
  beforeEach(() => {
    jest.restoreAllMocks();
    mockPush.mockClear();
    mockBack.mockClear();
  });

  it('llama a GET /events/organizer/me y muestra los eventos propios con su estado', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [evento()] } as any);

    const { findByText } = render(<OrganizerEventsListScreen />);

    expect(await findByText('Festival de Verano')).toBeTruthy();
    expect(getSpy).toHaveBeenCalledWith('/events/organizer/me');
    expect(await findByText('BORRADOR')).toBeTruthy();
  });

  it('muestra un estado vacío cuando el organizador no tiene eventos', async () => {
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [] } as any);

    const { findByText } = render(<OrganizerEventsListScreen />);

    expect(await findByText('Todavía no creaste ningún evento.')).toBeTruthy();
  });

  it('muestra error con reintento ante una falla de red', async () => {
    jest.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('network down'));
    const { findByText } = render(<OrganizerEventsListScreen />);

    expect(await findByText('No se pudieron cargar tus eventos. Verificá tu conexión.')).toBeTruthy();

    jest.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: [evento()] } as any);
    fireEvent.press(await findByText('Reintentar'));

    expect(await findByText('Festival de Verano')).toBeTruthy();
  });

  it('mapea un 403 a un mensaje de permisos', async () => {
    jest
      .spyOn(apiClient, 'get')
      .mockRejectedValue(new ApiError({ code: 'FORBIDDEN', message: 'no autorizado', traceId: 't' }, 403));

    const { findByText } = render(<OrganizerEventsListScreen />);

    expect(await findByText('Tu cuenta no tiene permiso para ver tus eventos.')).toBeTruthy();
  });

  it('navega al detalle del evento al tocar la tarjeta', async () => {
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [evento()] } as any);
    const { findByLabelText } = render(<OrganizerEventsListScreen />);

    fireEvent.press(await findByLabelText('Ver detalle de Festival de Verano'));

    expect(mockPush).toHaveBeenCalledWith({ pathname: '/organizer/[id]', params: { id: 'evento-1' } });
  });

  it('navega a la creación de evento al tocar "Crear evento"', async () => {
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [] } as any);
    const { findByText } = render(<OrganizerEventsListScreen />);

    fireEvent.press(await findByText('+ Crear evento'));

    expect(mockPush).toHaveBeenCalledWith('/organizer/new');
  });
});
