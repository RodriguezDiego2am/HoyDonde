import React from 'react';
import { fireEvent, render, waitFor } from '@testing-library/react-native';

jest.mock('../../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

const mockPush = jest.fn();
const mockBack = jest.fn();
let mockParams: { id?: string } = { id: 'evento-1' };
jest.mock('expo-router', () => ({
  router: {
    push: (...args: unknown[]) => mockPush(...args),
    back: (...args: unknown[]) => mockBack(...args),
  },
  useLocalSearchParams: () => mockParams,
}));

let mockHasAccion: (accion: string) => boolean = () => false;
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => ({ hasAccion: (accion: string) => mockHasAccion(accion) }),
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { apiClient, ApiError, EventResponse } from '@/services/APIService';
// eslint-disable-next-line import/first
import OrganizerEventDetailScreen from './OrganizerEventDetailScreen';

function evento(overrides: Partial<EventResponse> = {}): EventResponse {
  return {
    id: 'evento-1',
    nombre: 'Festival de Verano',
    descripcion: 'Un evento de ejemplo',
    fechaInicio: '2090-12-01T22:00:00Z',
    fechaFin: '2090-12-02T04:00:00Z',
    ubicacion: 'Parque Central',
    categoria: 'Musica',
    estado: 'Borrador',
    ticketGroups: [{ id: 'tipo-general', nombre: 'General', precio: 5000, cantidadDisponible: 100 }],
    ...overrides,
  };
}

describe('OrganizerEventDetailScreen', () => {
  beforeEach(() => {
    jest.restoreAllMocks();
    mockPush.mockClear();
    mockBack.mockClear();
    mockParams = { id: 'evento-1' };
    mockHasAccion = () => false;
  });

  it('muestra el detalle usando GET /events/organizer/{id}', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento() } as any);

    const { findByText } = render(<OrganizerEventDetailScreen />);

    expect(await findByText('Festival de Verano')).toBeTruthy();
    expect(getSpy).toHaveBeenCalledWith('/events/organizer/evento-1');
  });

  it('en Borrador muestra Editar, Publicar y Cancelar', async () => {
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento({ estado: 'Borrador' }) } as any);

    const { findByText } = render(<OrganizerEventDetailScreen />);
    await findByText('Festival de Verano');

    expect(await findByText('Editar')).toBeTruthy();
    expect(await findByText('Publicar')).toBeTruthy();
    expect(await findByText('Cancelar evento')).toBeTruthy();
  });

  it('en Publicado (no finalizado) solo permite Cancelar, no Editar ni Publicar', async () => {
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento({ estado: 'Publicado' }) } as any);

    const { findByText, queryByText } = render(<OrganizerEventDetailScreen />);
    await findByText('Festival de Verano');

    expect(queryByText('Editar')).toBeNull();
    expect(queryByText('Publicar')).toBeNull();
    expect(await findByText('Cancelar evento')).toBeTruthy();
  });

  it('en Finalizado no permite ninguna acción de ciclo de vida', async () => {
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento({ estado: 'Finalizado' }) } as any);

    const { findByText, queryByText } = render(<OrganizerEventDetailScreen />);
    await findByText('Festival de Verano');

    expect(queryByText('Editar')).toBeNull();
    expect(queryByText('Publicar')).toBeNull();
    expect(queryByText('Cancelar evento')).toBeNull();
    expect(await findByText('Este evento ya no admite más acciones.')).toBeTruthy();
  });

  it('publicar pide confirmación antes de llamar a POST /publish', async () => {
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento({ estado: 'Borrador' }) } as any);
    const postSpy = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: { message: 'ok' } } as any);

    const { findByText, getByText } = render(<OrganizerEventDetailScreen />);
    await findByText('Festival de Verano');

    fireEvent.press(getByText('Publicar'));
    expect(postSpy).not.toHaveBeenCalled();
    expect(
      await findByText('Una vez publicado, el evento queda visible en la cartelera y no se puede volver a Borrador.')
    ).toBeTruthy();
  });

  it('confirmar Publicar llama a POST /events/{id}/publish y recarga el evento', async () => {
    const getSpy = jest
      .spyOn(apiClient, 'get')
      .mockResolvedValueOnce({ data: evento({ estado: 'Borrador' }) } as any)
      .mockResolvedValueOnce({ data: evento({ estado: 'Publicado' }) } as any);
    const postSpy = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: { message: 'ok' } } as any);

    const { findByText, getByText, getAllByText } = render(<OrganizerEventDetailScreen />);
    await findByText('Festival de Verano');

    fireEvent.press(getByText('Publicar'));
    const confirmButtons = getAllByText('Publicar');
    fireEvent.press(confirmButtons[confirmButtons.length - 1]);

    await waitFor(() => expect(postSpy).toHaveBeenCalledWith('/events/evento-1/publish'));
    await waitFor(() => expect(getSpy).toHaveBeenCalledTimes(2));
  });

  it('confirmar Cancelar llama a POST /events/{id}/cancel', async () => {
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento({ estado: 'Borrador' }) } as any);
    const postSpy = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: { message: 'ok' } } as any);

    const { findByText, getByText } = render(<OrganizerEventDetailScreen />);
    await findByText('Festival de Verano');

    fireEvent.press(getByText('Cancelar evento'));
    fireEvent.press(await findByText('Sí, cancelar'));

    await waitFor(() => expect(postSpy).toHaveBeenCalledWith('/events/evento-1/cancel'));
  });

  it('mapea EVENT_INVALID_TRANSITION a un mensaje claro sin cerrar el detalle', async () => {
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento({ estado: 'Borrador' }) } as any);
    jest
      .spyOn(apiClient, 'post')
      .mockRejectedValue(new ApiError({ code: 'EVENT_INVALID_TRANSITION', message: 'no', traceId: 't' }, 409));

    const { findByText, getByText, getAllByText } = render(<OrganizerEventDetailScreen />);
    await findByText('Festival de Verano');

    fireEvent.press(getByText('Publicar'));
    const confirmButtons = getAllByText('Publicar');
    fireEvent.press(confirmButtons[confirmButtons.length - 1]);

    expect(await findByText('Ese cambio de estado ya no es válido para este evento.')).toBeTruthy();
    expect(getByText('Festival de Verano')).toBeTruthy();
  });

  it('no muestra la sección de Control sin CONTROL_CREAR', async () => {
    mockHasAccion = () => false;
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento() } as any);

    const { findByText, queryByText } = render(<OrganizerEventDetailScreen />);
    await findByText('Festival de Verano');

    expect(queryByText('Crear Control nuevo')).toBeNull();
    expect(queryByText('Asignar Control existente')).toBeNull();
  });

  it('con CONTROL_CREAR carga y muestra los Controles asignados, sin exponer ids internos', async () => {
    mockHasAccion = (accion) => accion === 'CONTROL_CREAR';
    const getSpy = jest.spyOn(apiClient, 'get').mockImplementation(((url: string) => {
      if (url === '/events/organizer/evento-1') return Promise.resolve({ data: evento() });
      if (url === '/events/evento-1/controls') {
        return Promise.resolve({
          data: [
            {
              controlPersonaId: 'persona-control-secreta',
              userName: 'control_puerta_norte',
              activo: true,
              assignedByPersonaId: 'persona-org',
              createdAt: '2026-08-02T15:00:00Z',
            },
          ],
        });
      }
      return Promise.reject(new Error('unexpected url ' + url));
    }) as any);

    const { findByText, queryByText } = render(<OrganizerEventDetailScreen />);
    await findByText('Festival de Verano');

    expect(await findByText('control_puerta_norte')).toBeTruthy();
    expect(getSpy).toHaveBeenCalledWith('/events/evento-1/controls');
    expect(queryByText('persona-control-secreta')).toBeNull();
    expect(await findByText('Crear Control nuevo')).toBeTruthy();
    expect(await findByText('Asignar Control existente')).toBeTruthy();
  });

  it('navega a crear/asignar Control al presionar los botones correspondientes', async () => {
    mockHasAccion = (accion) => accion === 'CONTROL_CREAR';
    jest.spyOn(apiClient, 'get').mockImplementation(((url: string) => {
      if (url === '/events/organizer/evento-1') return Promise.resolve({ data: evento() });
      return Promise.resolve({ data: [] });
    }) as any);

    const { findByText } = render(<OrganizerEventDetailScreen />);
    await findByText('Festival de Verano');

    fireEvent.press(await findByText('Crear Control nuevo'));
    expect(mockPush).toHaveBeenCalledWith({ pathname: '/organizer/[id]/control-new', params: { id: 'evento-1' } });

    fireEvent.press(await findByText('Asignar Control existente'));
    expect(mockPush).toHaveBeenCalledWith({ pathname: '/organizer/[id]/control-assign', params: { id: 'evento-1' } });
  });
});
