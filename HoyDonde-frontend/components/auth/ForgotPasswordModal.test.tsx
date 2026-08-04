import React from 'react';
import { fireEvent, render, waitFor } from '@testing-library/react-native';

const mockSendPasswordResetEmail = jest.fn();
jest.mock('firebase/auth', () => ({
  sendPasswordResetEmail: (...args: unknown[]) => (mockSendPasswordResetEmail as any)(...args),
}));
jest.mock('@/config/firebase', () => ({ auth: {} }));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { ForgotPasswordModal } from './ForgotPasswordModal';

const PRUDENT_MESSAGE = 'Si existe una cuenta asociada, recibirás instrucciones para restablecerla.';
const CONTROL_NOTICE =
  'Si ingresás como Control y olvidaste tu contraseña, solicitá al Administrador un enlace de recuperación.';

describe('ForgotPasswordModal', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('shows the Control notice even before submitting anything', () => {
    const { getByText } = render(<ForgotPasswordModal visible onClose={jest.fn()} />);

    expect(getByText(CONTROL_NOTICE)).toBeTruthy();
  });

  it('rejects an invalid email format without calling Firebase', () => {
    const { getByPlaceholderText, getByText } = render(<ForgotPasswordModal visible onClose={jest.fn()} />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'no-es-un-email');
    fireEvent.press(getByText('Enviar instrucciones'));

    expect(getByText('Ingresá un email válido.')).toBeTruthy();
    expect(mockSendPasswordResetEmail).not.toHaveBeenCalled();
  });

  it('shows the prudent generic message on success, for a valid email', async () => {
    mockSendPasswordResetEmail.mockResolvedValueOnce(undefined);
    const { getByPlaceholderText, getByText } = render(<ForgotPasswordModal visible onClose={jest.fn()} />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'cliente@hoydonde.com');
    fireEvent.press(getByText('Enviar instrucciones'));

    await waitFor(() => expect(getByText(PRUDENT_MESSAGE)).toBeTruthy());
    expect(mockSendPasswordResetEmail).toHaveBeenCalledWith({}, 'cliente@hoydonde.com');
  });

  it('never reveals whether the email exists: auth/user-not-found shows the same prudent message as success', async () => {
    mockSendPasswordResetEmail.mockRejectedValueOnce({ code: 'auth/user-not-found' });
    const { getByPlaceholderText, getByText, queryByText } = render(<ForgotPasswordModal visible onClose={jest.fn()} />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'inexistente@hoydonde.com');
    fireEvent.press(getByText('Enviar instrucciones'));

    await waitFor(() => expect(getByText(PRUDENT_MESSAGE)).toBeTruthy());
    expect(queryByText('inexistente@hoydonde.com')).toBeNull();
  });

  it('shows a distinct, safe error for a network/provider failure, and allows retrying', async () => {
    mockSendPasswordResetEmail.mockRejectedValueOnce({ code: 'auth/network-request-failed' });
    const { getByPlaceholderText, getByText, queryByText } = render(<ForgotPasswordModal visible onClose={jest.fn()} />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'cliente@hoydonde.com');
    fireEvent.press(getByText('Enviar instrucciones'));

    await waitFor(() => expect(getByText('No pudimos procesar tu pedido. Probá de nuevo.')).toBeTruthy());
    expect(queryByText(PRUDENT_MESSAGE)).toBeNull();

    mockSendPasswordResetEmail.mockResolvedValueOnce(undefined);
    fireEvent.press(getByText('Enviar instrucciones'));

    await waitFor(() => expect(getByText(PRUDENT_MESSAGE)).toBeTruthy());
  });

  it('blocks a double submit while a request is in flight', async () => {
    let resolveSend!: () => void;
    mockSendPasswordResetEmail.mockReturnValueOnce(
      new Promise<void>((resolve) => {
        resolveSend = resolve;
      })
    );
    const { getByPlaceholderText, getByText, queryByText } = render(<ForgotPasswordModal visible onClose={jest.fn()} />);

    fireEvent.changeText(getByPlaceholderText('ejemplo@correo.com'), 'cliente@hoydonde.com');
    fireEvent.press(getByText('Enviar instrucciones'));

    // El botón pasa a estado de carga (sin texto presionable): un segundo toque no puede
    // disparar una segunda solicitud mientras la primera sigue en vuelo.
    expect(queryByText('Enviar instrucciones')).toBeNull();
    expect(mockSendPasswordResetEmail).toHaveBeenCalledTimes(1);

    resolveSend();
    await waitFor(() => expect(getByText(PRUDENT_MESSAGE)).toBeTruthy());
    expect(mockSendPasswordResetEmail).toHaveBeenCalledTimes(1);
  });
});
