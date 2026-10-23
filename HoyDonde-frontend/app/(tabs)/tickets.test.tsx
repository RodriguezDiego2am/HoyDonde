import React from 'react';
import { fireEvent, render, waitFor } from '@testing-library/react-native';

jest.mock('react-native-qrcode-svg', () => {
  const ReactActual = require('react');
  const { Text } = require('react-native');
  return function MockQRCode({ value }: { value: string }) {
    return ReactActual.createElement(Text, { testID: 'qr-value' }, value);
  };
});

jest.mock('../../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

const mockPush = jest.fn();
jest.mock('expo-router', () => ({
  router: { push: (...args: unknown[]) => mockPush(...args) },
}));

let mockAuthValue: {
  user: { uid: string } | null;
  initializing: boolean;
  hasAccion: (accion: string) => boolean;
} = { user: null, initializing: false, hasAccion: () => false };
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => mockAuthValue,
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { apiClient, ApiError, TicketResponse } from '@/services/APIService';
// eslint-disable-next-line import/first
import MisEntradasScreen from './tickets';

function ticket(overrides: Partial<TicketResponse> = {}): TicketResponse {
  return {
    id: 'ticket-1',
    compraId: 'compra-1',
    eventoId: 'evento-1',
    ticketTypeId: 'tipo-1',
    clientePersonaId: 'persona-1',
    fechaCompra: '2026-08-01T12:00:00Z',
    estado: 'Emitido',
    utilizable: true,
    motivoNoUtilizable: null,
    eventoNombre: 'Festival de Verano',
    ticketTypeNombre: 'General',
    precioPagado: 5000,
    qrToken: 'AbCdEfGhIjKlMnOpQrStUv',
    fechaInicio: '2026-12-01T22:00:00Z',
    fechaFin: '2026-12-02T04:00:00Z',
    ...overrides,
  };
}

describe('MisEntradasScreen', () => {
  beforeEach(() => {
    jest.restoreAllMocks();
    mockPush.mockClear();
    mockAuthValue = { user: null, initializing: false, hasAccion: () => false };
  });

  it('usuario anónimo ve el CTA de login con returnTo hacia esta pestaña, y no dispara ningún fetch', async () => {
    const getSpy = jest.spyOn(apiClient, 'get');
    const { findByText } = render(<MisEntradasScreen />);

    fireEvent.press(await findByText('Iniciar sesión'));

    expect(mockPush).toHaveBeenCalledWith({ pathname: '/login', params: { returnTo: '/(tabs)/tickets' } });
    expect(getSpy).not.toHaveBeenCalled();
  });

  it('usuario autenticado sin TICKET_VER_PROPIO ve una explicación y no llama a la API', async () => {
    mockAuthValue = { user: { uid: 'uid-1' }, initializing: false, hasAccion: () => false };
    const getSpy = jest.spyOn(apiClient, 'get');

    const { findByText } = render(<MisEntradasScreen />);

    expect(await findByText('Tu cuenta no tiene permiso para ver entradas.')).toBeTruthy();
    expect(getSpy).not.toHaveBeenCalled();
  });

  it('Cliente habilitado ve sus entradas con los campos del DTO', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [ticket()] } as any);

    const { findByText } = render(<MisEntradasScreen />);

    expect(await findByText('Festival de Verano')).toBeTruthy();
    expect(await findByText('General')).toBeTruthy();
    expect(await findByText('UTILIZABLE')).toBeTruthy();
  });

  it('diferencia visualmente Usado, Anulado, evento cancelado y evento finalizado con texto explícito', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({
      data: [
        ticket({ id: 't-usado', estado: 'Usado', utilizable: false, motivoNoUtilizable: 'Usado' }),
        ticket({ id: 't-anulado', estado: 'Anulado', utilizable: false, motivoNoUtilizable: 'Anulado' }),
        ticket({ id: 't-cancelado', utilizable: false, motivoNoUtilizable: 'EventoCancelado' }),
        ticket({ id: 't-finalizado', utilizable: false, motivoNoUtilizable: 'EventoFinalizado' }),
      ],
    } as any);

    const { findByText } = render(<MisEntradasScreen />);

    // StatusStamp muestra el label en mayúsculas (nunca depende solo del color).
    expect(await findByText('USADO')).toBeTruthy();
    expect(await findByText('ANULADO')).toBeTruthy();
    expect(await findByText('EVENTO CANCELADO')).toBeTruthy();
    expect(await findByText('EVENTO FINALIZADO')).toBeTruthy();
  });

  it('etiqueta la compra simulada histórica sin confundirla con una compra real confirmada', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [ticket({ pagoSimulado: true })] } as any);

    const { findByText } = render(<MisEntradasScreen />);

    expect(await findByText('COMPRA SIMULADA HISTÓRICA')).toBeTruthy();
  });

  it('no etiqueta como simulada una entrada sin CompraId cuando no hay semántica histórica explícita', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [ticket({ compraId: null, pagoSimulado: false })] } as any);

    const { findByText, queryByText } = render(<MisEntradasScreen />);

    expect(await findByText('Festival de Verano')).toBeTruthy();
    expect(queryByText('COMPRA SIMULADA HISTÓRICA')).toBeNull();
  });

  it('un ticket legacy sin CompraId (previo a esta etapa) se sigue mostrando normalmente', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [ticket({ id: 'ticket-legacy', compraId: null })] } as any);

    const { findByText } = render(<MisEntradasScreen />);

    expect(await findByText('Festival de Verano')).toBeTruthy();
    expect(await findByText('General')).toBeTruthy();
    expect(await findByText('UTILIZABLE')).toBeTruthy();
  });

  it('muestra el estado vacío cuando el Cliente no compró ninguna entrada', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [] } as any);

    const { findByText } = render(<MisEntradasScreen />);

    expect(await findByText('Todavía no compraste ninguna entrada.')).toBeTruthy();
  });

  it('muestra error y permite reintentar ante una falla genérica', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    const getSpy = jest.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('network down'));

    const { findByText } = render(<MisEntradasScreen />);
    expect(await findByText('No se pudieron cargar tus entradas. Verificá tu conexión.')).toBeTruthy();

    getSpy.mockResolvedValueOnce({ data: [ticket()] } as any);
    fireEvent.press(await findByText('Reintentar'));

    expect(await findByText('Festival de Verano')).toBeTruthy();
  });

  it('mapea un 401 a un mensaje de sesión expirada', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest
      .spyOn(apiClient, 'get')
      .mockRejectedValue(new ApiError({ code: 'UNAUTHORIZED', message: 'no autorizado', traceId: 't' }, 401));

    const { findByText } = render(<MisEntradasScreen />);

    expect(await findByText('Tu sesión expiró. Iniciá sesión de nuevo para ver tus entradas.')).toBeTruthy();
  });

  it('mapea un 403 a un mensaje de falta de permiso', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest
      .spyOn(apiClient, 'get')
      .mockRejectedValue(new ApiError({ code: 'FORBIDDEN', message: 'no autorizado', traceId: 't' }, 403));

    const { findByText } = render(<MisEntradasScreen />);

    expect(await findByText('Tu cuenta no tiene permiso para ver entradas.')).toBeTruthy();
  });

  it('abre el QR del ticket desde la lista, sin mostrar todos los QR a la vez', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({
      data: [ticket(), ticket({ id: 'ticket-2', eventoNombre: 'Otro evento' })],
    } as any);

    const { findByText, getAllByText, queryAllByTestId } = render(<MisEntradasScreen />);
    await findByText('Festival de Verano');

    expect(queryAllByTestId('qr-value')).toHaveLength(0);

    fireEvent.press(getAllByText('Ver QR')[0]);

    await waitFor(() => expect(queryAllByTestId('qr-value')).toHaveLength(1));
    // The QR encodes exactly the backend token; no ids are shown.
    expect(queryAllByTestId('qr-value')[0].props.children).toBe('AbCdEfGhIjKlMnOpQrStUv');
  });

  it('un ticket recibido por transferencia no muestra precio ni fecha de compra', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({
      data: [ticket({ precioPagado: null, compraId: null })],
    } as any);

    const { findByText, queryByText } = render(<MisEntradasScreen />);

    expect(await findByText('RECIBIDA')).toBeTruthy();
    expect(await findByText('Recibida por transferencia')).toBeTruthy();
    expect(queryByText(/Comprada el/)).toBeNull();
  });

  describe('transferencia', () => {
    const enableTransfer = () => {
      mockAuthValue = {
        user: { uid: 'cliente-1' },
        initializing: false,
        hasAccion: (accion) => accion === 'TICKET_VER_PROPIO' || accion === 'TICKET_TRANSFERIR',
      };
    };

    it('sin la acción TICKET_TRANSFERIR no ofrece Transferir', async () => {
      mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: (a) => a === 'TICKET_VER_PROPIO' };
      jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [ticket()] } as any);

      const { findByText, queryByText } = render(<MisEntradasScreen />);
      await findByText('Festival de Verano');

      expect(queryByText('Transferir')).toBeNull();
    });

    it('solo ofrece Transferir en entradas utilizables', async () => {
      enableTransfer();
      jest.spyOn(apiClient, 'get').mockResolvedValue({
        data: [ticket({ id: 't-usado', estado: 'Usado', utilizable: false, motivoNoUtilizable: 'Usado', qrToken: null })],
      } as any);

      const { findByText, queryByText } = render(<MisEntradasScreen />);
      await findByText('USADO');

      expect(queryByText('Transferir')).toBeNull();
    });

    it('flujo completo: email, confirmación explícita, POST y la entrada sale de la lista', async () => {
      enableTransfer();
      jest.spyOn(apiClient, 'get').mockResolvedValue({
        data: [ticket(), ticket({ id: 'ticket-2', eventoNombre: 'Otro evento' })],
      } as any);
      const postSpy = jest
        .spyOn(apiClient, 'post')
        .mockResolvedValue({ data: { ticketId: 'ticket-1', transferredAt: '2026-10-22T10:00:00Z' } } as any);

      const { findByText, getAllByText, getByText, getByLabelText, queryByText } = render(<MisEntradasScreen />);
      await findByText('Festival de Verano');

      fireEvent.press(getAllByText('Transferir')[0]);
      fireEvent.changeText(getByLabelText('Email del destinatario'), '  destino@example.com ');
      fireEvent.press(getByText('Continuar'));

      // The confirmation states that it is immediate and irreversible; nothing was sent yet.
      expect(await findByText(/inmediata e irreversible/)).toBeTruthy();
      expect(postSpy).not.toHaveBeenCalled();

      fireEvent.press(getByText('Transferir ahora'));

      await findByText('Entrada transferida');
      expect(postSpy).toHaveBeenCalledTimes(1);
      expect(postSpy).toHaveBeenCalledWith('/tickets/ticket-1/transfer', { recipientEmail: 'destino@example.com' });

      fireEvent.press(getByText('Listo'));

      await waitFor(() => expect(queryByText('Festival de Verano')).toBeNull());
      expect(getByText('Otro evento')).toBeTruthy();
      expect(getByText('Entrada transferida. Tu QR anterior dejó de valer.')).toBeTruthy();
    });
  });
});
