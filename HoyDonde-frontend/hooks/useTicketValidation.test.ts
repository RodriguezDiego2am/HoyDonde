import { act, renderHook } from '@testing-library/react-native';

jest.mock('expo-haptics', () => ({
  notificationAsync: jest.fn().mockResolvedValue(undefined),
  NotificationFeedbackType: { Success: 'success', Error: 'error' },
}));

const mockValidateTicket = jest.fn();
jest.mock('@/services/ticketValidationService', () => ({
  validateTicket: (...args: unknown[]) => mockValidateTicket(...args),
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import * as Haptics from 'expo-haptics';
// eslint-disable-next-line import/first
import { useTicketValidation } from './useTicketValidation';

describe('useTicketValidation', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('llama a validateTicket una sola vez con los ids exactos', async () => {
    mockValidateTicket.mockResolvedValue({ kind: 'valid', message: 'Ticket validado para este evento.' });
    const { result } = renderHook(() => useTicketValidation());

    await act(async () => {
      await result.current.validate('ticket-1', 'evento-1');
    });

    expect(mockValidateTicket).toHaveBeenCalledTimes(1);
    expect(mockValidateTicket).toHaveBeenCalledWith({ ticketId: 'ticket-1', eventId: 'evento-1' });
    expect(result.current.result).toEqual({ kind: 'valid', message: 'Ticket validado para este evento.' });
  });

  it('el lock ignora llamadas concurrentes (varios frames del mismo QR) mientras la primera sigue en vuelo', async () => {
    let resolveFirst: (value: unknown) => void = () => {};
    mockValidateTicket.mockReturnValueOnce(
      new Promise((resolve) => {
        resolveFirst = resolve;
      })
    );
    const { result } = renderHook(() => useTicketValidation());

    let firstCall!: Promise<void>;
    act(() => {
      firstCall = result.current.validate('ticket-1', 'evento-1');
      // Segundo y tercer frame detectando el mismo QR antes de que la primera llamada resuelva.
      result.current.validate('ticket-1', 'evento-1');
      result.current.validate('ticket-1', 'evento-1');
    });

    expect(mockValidateTicket).toHaveBeenCalledTimes(1);

    await act(async () => {
      resolveFirst({ kind: 'valid', message: 'Ticket validado para este evento.' });
      await firstCall;
    });

    expect(mockValidateTicket).toHaveBeenCalledTimes(1);
  });

  it('reset() limpia el resultado y el lock, permitiendo una nueva validación', async () => {
    mockValidateTicket.mockResolvedValueOnce({ kind: 'notFound', message: 'Ticket no encontrado.' });
    const { result } = renderHook(() => useTicketValidation());

    await act(async () => {
      await result.current.validate('t1', 'e1');
    });
    expect(result.current.result?.kind).toBe('notFound');

    act(() => {
      result.current.reset();
    });
    expect(result.current.result).toBeNull();

    mockValidateTicket.mockResolvedValueOnce({ kind: 'valid', message: 'ok' });
    await act(async () => {
      await result.current.validate('t2', 'e2');
    });

    expect(mockValidateTicket).toHaveBeenCalledTimes(2);
    expect(mockValidateTicket).toHaveBeenLastCalledWith({ ticketId: 't2', eventId: 'e2' });
    expect(result.current.result?.kind).toBe('valid');
  });

  it('sin reset(), una nueva llamada a validate no dispara una segunda request', async () => {
    mockValidateTicket.mockResolvedValueOnce({ kind: 'valid', message: 'ok' });
    const { result } = renderHook(() => useTicketValidation());

    await act(async () => {
      await result.current.validate('t1', 'e1');
    });
    await act(async () => {
      await result.current.validate('t1', 'e1');
    });

    expect(mockValidateTicket).toHaveBeenCalledTimes(1);
  });

  it('dispara haptics de éxito en un resultado válido', async () => {
    mockValidateTicket.mockResolvedValueOnce({ kind: 'valid', message: 'ok' });
    const { result } = renderHook(() => useTicketValidation());

    await act(async () => {
      await result.current.validate('t1', 'e1');
    });

    expect(Haptics.notificationAsync).toHaveBeenCalledWith('success');
  });

  it('dispara haptics de error en cualquier resultado que no sea válido', async () => {
    mockValidateTicket.mockResolvedValueOnce({ kind: 'alreadyUsed', message: 'El ticket ya fue utilizado.' });
    const { result } = renderHook(() => useTicketValidation());

    await act(async () => {
      await result.current.validate('t1', 'e1');
    });

    expect(Haptics.notificationAsync).toHaveBeenCalledWith('error');
  });
});
