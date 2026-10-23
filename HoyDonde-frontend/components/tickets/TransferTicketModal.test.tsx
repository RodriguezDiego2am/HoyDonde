import React from 'react';
import { act, fireEvent, render, waitFor } from '@testing-library/react-native';

jest.mock('../../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { apiClient, ApiError } from '@/services/APIService';
// eslint-disable-next-line import/first
import { TransferTicketModal, transferErrorMessage } from './TransferTicketModal';

function renderModal(overrides: Partial<React.ComponentProps<typeof TransferTicketModal>> = {}) {
  const props = {
    visible: true,
    ticketId: 'ticket-1',
    eventoNombre: 'Festival de Verano',
    ticketTypeNombre: 'General',
    onClose: jest.fn(),
    onTransferred: jest.fn(),
    ...overrides,
  };
  return { props, ...render(<TransferTicketModal {...props} />) };
}

function apiError(code: string, status: number, message = 'x') {
  return new ApiError({ code, message, traceId: 't' }, status);
}

describe('TransferTicketModal', () => {
  beforeEach(() => {
    jest.restoreAllMocks();
  });

  it('rechaza un email con formato inválido sin llamar a la API ni pasar a la confirmación', () => {
    const postSpy = jest.spyOn(apiClient, 'post');
    const { getByLabelText, getByText, queryByText } = renderModal();

    fireEvent.changeText(getByLabelText('Email del destinatario'), 'no-es-un-email');
    fireEvent.press(getByText('Continuar'));

    expect(getByText('Ingresá un email válido.')).toBeTruthy();
    expect(queryByText('Transferir ahora')).toBeNull();
    expect(postSpy).not.toHaveBeenCalled();
  });

  it('la confirmación advierte que es inmediata e irreversible y permite volver sin enviar', () => {
    const postSpy = jest.spyOn(apiClient, 'post');
    const { getByLabelText, getByText } = renderModal();

    fireEvent.changeText(getByLabelText('Email del destinatario'), 'destino@example.com');
    fireEvent.press(getByText('Continuar'));

    expect(getByText(/inmediata e irreversible/)).toBeTruthy();
    expect(getByText('destino@example.com')).toBeTruthy();

    fireEvent.press(getByText('Volver'));
    expect(getByLabelText('Email del destinatario')).toBeTruthy();
    expect(postSpy).not.toHaveBeenCalled();
  });

  it('transfiere una sola vez aunque se toque dos veces el botón, y avisa al padre', async () => {
    let resolvePost: (value: unknown) => void = () => {};
    const postSpy = jest.spyOn(apiClient, 'post').mockReturnValue(
      new Promise((resolve) => {
        resolvePost = resolve;
      }) as any
    );
    const { props, getByLabelText, getByText, findByText } = renderModal();

    fireEvent.changeText(getByLabelText('Email del destinatario'), 'destino@example.com');
    fireEvent.press(getByText('Continuar'));
    fireEvent.press(getByText('Transferir ahora'));
    // While in flight the button is busy/disabled: a second tap cannot send a second request.
    fireEvent.press(getByLabelText('Transferir ahora, cargando'));

    expect(postSpy).toHaveBeenCalledTimes(1);

    await act(async () => {
      resolvePost({ data: { ticketId: 'ticket-1', transferredAt: '2026-10-22T10:00:00Z' } });
    });

    expect(await findByText('Entrada transferida')).toBeTruthy();
    expect(props.onTransferred).toHaveBeenCalledTimes(1);
    expect(props.onTransferred).toHaveBeenCalledWith('ticket-1');
  });

  it.each([
    ['TRANSFER_RECIPIENT_INVALID', 404, /no pudimos transferir la entrada a ese destinatario/i],
    ['TRANSFER_TO_SELF', 400, /no podés transferirte una entrada a vos mismo/i],
    ['TICKET_NOT_TRANSFERABLE', 409, /ya no se puede transferir/i],
    ['TRANSFER_RATE_LIMITED', 429, /límite de 10 transferencias por hora/i],
    ['TICKET_NOT_FOUND', 404, /no encontramos esta entrada/i],
  ])('error %s vuelve al formulario con un mensaje claro y no avisa al padre', async (code, status, expected) => {
    jest.spyOn(apiClient, 'post').mockRejectedValue(apiError(code, status));
    const { props, getByLabelText, getByText, findByText } = renderModal();

    fireEvent.changeText(getByLabelText('Email del destinatario'), 'secreto@example.com');
    fireEvent.press(getByText('Continuar'));
    fireEvent.press(getByText('Transferir ahora'));

    const message = await findByText(expected);
    expect(message).toBeTruthy();
    expect(props.onTransferred).not.toHaveBeenCalled();
    // The error text never echoes the typed email back.
    expect(message.props.children).not.toContain('secreto@example.com');
    // Still on the form, so the user can correct the email and retry.
    expect(getByLabelText('Email del destinatario')).toBeTruthy();
  });

  it('cerrar desde el éxito llama a onClose', async () => {
    jest.spyOn(apiClient, 'post').mockResolvedValue({ data: { ticketId: 'ticket-1', transferredAt: 'x' } } as any);
    const { props, getByLabelText, getByText, findByText } = renderModal();

    fireEvent.changeText(getByLabelText('Email del destinatario'), 'destino@example.com');
    fireEvent.press(getByText('Continuar'));
    fireEvent.press(getByText('Transferir ahora'));
    await findByText('Entrada transferida');

    fireEvent.press(getByText('Listo'));
    await waitFor(() => expect(props.onClose).toHaveBeenCalledTimes(1));
  });
});

describe('transferErrorMessage', () => {
  it('mapea 401/403 genéricos y errores de red sin exponer detalles', () => {
    expect(transferErrorMessage(apiError('X', 401))).toMatch(/sesión expiró/i);
    expect(transferErrorMessage(apiError('X', 403))).toMatch(/no tiene permiso/i);
    expect(transferErrorMessage(new Error('boom'))).toMatch(/verificá tu conexión/i);
  });
});
