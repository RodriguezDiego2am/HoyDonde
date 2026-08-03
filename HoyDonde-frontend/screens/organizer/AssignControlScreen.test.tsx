import React from 'react';
import { fireEvent, render, waitFor } from '@testing-library/react-native';

const mockBack = jest.fn();
let mockParams: { id?: string } = { id: 'evento-1' };
jest.mock('expo-router', () => ({
  router: { back: (...args: unknown[]) => mockBack(...args) },
  useLocalSearchParams: () => mockParams,
}));

const mockGetMyControls = jest.fn();
const mockAssignExisting = jest.fn();
jest.mock('@/services/controlAsignacionService', () => ({
  controlAsignacionService: {
    getMyControls: (...args: unknown[]) => mockGetMyControls(...args),
    assignExisting: (...args: unknown[]) => mockAssignExisting(...args),
  },
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { ApiError } from '@/services/apiError';
// eslint-disable-next-line import/first
import AssignControlScreen from './AssignControlScreen';

describe('AssignControlScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockParams = { id: 'evento-1' };
  });

  it('carga los Controles del organizador (GET organizer/controls) y los lista sin pedir ids a mano', async () => {
    mockGetMyControls.mockResolvedValueOnce([
      { controlPersonaId: 'persona-c1', userName: 'control_puerta_norte', activo: true },
      { controlPersonaId: 'persona-c2', userName: 'control_puerta_sur', activo: false },
    ]);

    const { findByText, queryByPlaceholderText } = render(<AssignControlScreen />);

    expect(await findByText('control_puerta_norte')).toBeTruthy();
    expect(await findByText('control_puerta_sur')).toBeTruthy();
    expect(mockGetMyControls).toHaveBeenCalledTimes(1);
    // Nunca se le pide al organizador que copie/pegue un PersonaId.
    expect(queryByPlaceholderText(/persona/i)).toBeNull();
  });

  it('muestra un estado vacío cuando el organizador no tiene Controles', async () => {
    mockGetMyControls.mockResolvedValueOnce([]);
    const { findByText } = render(<AssignControlScreen />);

    expect(await findByText('Todavía no creaste ningún Control.')).toBeTruthy();
  });

  it('muestra error con reintento si falla la carga', async () => {
    mockGetMyControls.mockRejectedValueOnce(new Error('network down'));
    const { findByText } = render(<AssignControlScreen />);

    expect(await findByText('No se pudieron cargar tus Controles. Verificá tu conexión.')).toBeTruthy();

    mockGetMyControls.mockResolvedValueOnce([{ controlPersonaId: 'persona-c1', userName: 'control_puerta_norte', activo: true }]);
    fireEvent.press(await findByText('Reintentar'));

    expect(await findByText('control_puerta_norte')).toBeTruthy();
  });

  it('asigna el Control seleccionado con el eventId de la ruta y muestra "Asignado"', async () => {
    mockGetMyControls.mockResolvedValueOnce([
      { controlPersonaId: 'persona-c1', userName: 'control_puerta_norte', activo: true },
    ]);
    mockAssignExisting.mockResolvedValueOnce({
      controlPersonaId: 'persona-c1',
      eventId: 'evento-1',
      assignedByPersonaId: 'persona-org',
      createdAt: '2026-08-02T15:00:00Z',
    });

    const { findByText, getByText } = render(<AssignControlScreen />);
    await findByText('control_puerta_norte');

    fireEvent.press(getByText('Asignar'));

    await waitFor(() => expect(mockAssignExisting).toHaveBeenCalledWith('evento-1', 'persona-c1'));
    expect(await findByText('ASIGNADO')).toBeTruthy();
  });

  it('repetir la asignación (idempotencia) vuelve a mostrar éxito, no un error', async () => {
    mockGetMyControls.mockResolvedValueOnce([
      { controlPersonaId: 'persona-c1', userName: 'control_puerta_norte', activo: true },
    ]);
    mockAssignExisting.mockResolvedValue({
      controlPersonaId: 'persona-c1',
      eventId: 'evento-1',
      assignedByPersonaId: 'persona-org',
      createdAt: '2026-08-02T15:00:00Z',
    });

    const { findByText, getByText, queryByText } = render(<AssignControlScreen />);
    await findByText('control_puerta_norte');

    fireEvent.press(getByText('Asignar'));
    expect(await findByText('ASIGNADO')).toBeTruthy();
    expect(queryByText('No se pudo asignar el Control.')).toBeNull();
  });

  it('mapea CONTROL_FOREIGN a un mensaje claro', async () => {
    mockGetMyControls.mockResolvedValueOnce([
      { controlPersonaId: 'persona-c1', userName: 'control_puerta_norte', activo: true },
    ]);
    mockAssignExisting.mockRejectedValueOnce(new ApiError({ code: 'CONTROL_FOREIGN', message: 'ajeno', traceId: 't' }, 403));

    const { findByText, getByText } = render(<AssignControlScreen />);
    await findByText('control_puerta_norte');

    fireEvent.press(getByText('Asignar'));

    expect(await findByText('Este Control pertenece a otro organizador.')).toBeTruthy();
  });

  it('vuelve al evento al presionar "Volver al evento"', async () => {
    mockGetMyControls.mockResolvedValueOnce([]);
    const { findByText } = render(<AssignControlScreen />);

    fireEvent.press(await findByText('Volver al evento'));
    expect(mockBack).toHaveBeenCalledTimes(1);
  });
});
