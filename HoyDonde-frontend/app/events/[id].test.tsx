import React from 'react';
import { act, fireEvent, render, waitFor } from '@testing-library/react-native';

jest.mock('../../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

const mockGenerateAndShareReceipt = jest.fn();
const mockOpenUrl = jest.fn().mockResolvedValue(true);
jest.mock('expo-linking', () => ({ openURL: (...args: unknown[]) => mockOpenUrl(...args) }));
jest.mock('expo-print', () => ({ printToFileAsync: jest.fn() }));
jest.mock('expo-sharing', () => ({ isAvailableAsync: jest.fn(), shareAsync: jest.fn() }));
jest.mock('expo-file-system', () => ({
  Paths: { cache: 'CACHE_DIR' },
  File: class {
    uri = 'mock-file://x';
    exists = false;
    delete() {}
    copy() {}
  },
}));
jest.mock('@/utils/purchaseReceiptPdf', () => {
  const actual = jest.requireActual('@/utils/purchaseReceiptPdf');
  return {
    ...actual,
    generateAndSharePurchaseReceiptPdf: (...args: unknown[]) => mockGenerateAndShareReceipt(...args),
  };
});

const mockPush = jest.fn();
const mockBack = jest.fn();
const mockReplace = jest.fn();
let mockParams: { id?: string } = { id: 'evento-1' };
jest.mock('expo-router', () => ({
  router: {
    push: (...args: unknown[]) => mockPush(...args),
    back: (...args: unknown[]) => mockBack(...args),
    replace: (...args: unknown[]) => mockReplace(...args),
  },
  useLocalSearchParams: () => mockParams,
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
import { apiClient, ApiError, EventResponse } from '@/services/APIService';
// eslint-disable-next-line import/first
import EventDetailScreen from './[id]';

function evento(overrides: Partial<EventResponse> = {}): EventResponse {
  return {
    id: 'evento-1',
    nombre: 'Festival de Verano',
    descripcion: 'Un evento de ejemplo',
    fechaInicio: '2026-12-01T22:00:00Z',
    fechaFin: '2026-12-02T04:00:00Z',
    ubicacion: 'Parque Central',
    categoria: 'Musica',
    estado: 'Publicado',
    ticketGroups: [{ id: 'tipo-general', nombre: 'General', precio: 5000, cantidadDisponible: 3 }],
    ...overrides,
  };
}


describe('EventDetailScreen', () => {
  beforeEach(() => {
    jest.restoreAllMocks();
    mockPush.mockClear();
    mockBack.mockClear();
    mockReplace.mockClear();
    mockOpenUrl.mockClear();
    mockGenerateAndShareReceipt.mockReset();
    mockParams = { id: 'evento-1' };
    mockAuthValue = { user: null, initializing: false, hasAccion: () => false };
  });

  it('muestra el detalle público sin exigir sesión', async () => {
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento() } as any);

    const { findByText } = render(<EventDetailScreen />);

    expect(await findByText('Festival de Verano')).toBeTruthy();
    expect(await findByText('Parque Central')).toBeTruthy();
    expect(await findByText('General')).toBeTruthy();
    expect(await findByText('3 disponibles')).toBeTruthy();
  });

  it('muestra "no encontrado" en un 404, sin exponer el mensaje crudo de axios', async () => {
    jest
      .spyOn(apiClient, 'get')
      .mockRejectedValue(new ApiError({ code: 'EVENT_NOT_FOUND', message: 'no importa', traceId: 't' }, 404));

    const { findByText } = render(<EventDetailScreen />);

    expect(await findByText('Evento no encontrado')).toBeTruthy();
  });

  it('muestra error genérico con reintento ante una falla de red', async () => {
    jest.spyOn(apiClient, 'get').mockRejectedValueOnce(new Error('network down'));
    const { findByText } = render(<EventDetailScreen />);

    expect(await findByText('No pudimos cargar este evento')).toBeTruthy();

    jest.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: evento() } as any);
    fireEvent.press(await findByText('Reintentar'));

    expect(await findByText('Festival de Verano')).toBeTruthy();
  });

  it('usuario anónimo ve el CTA de login con un returnTo interno hacia este evento', async () => {
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento() } as any);
    const { findByText } = render(<EventDetailScreen />);

    fireEvent.press(await findByText('Iniciar sesión'));

    expect(mockPush).toHaveBeenCalledWith({ pathname: '/login', params: { returnTo: '/events/evento-1' } });
  });

  it('usuario autenticado sin TICKET_COMPRAR ve una explicación, no un botón de compra', async () => {
    mockAuthValue = { user: { uid: 'uid-1' }, initializing: false, hasAccion: () => false };
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento() } as any);

    const { findByText, queryByText } = render(<EventDetailScreen />);

    expect(await findByText('Tu cuenta no tiene habilitada la compra de entradas.')).toBeTruthy();
    expect(queryByText('Comprar entradas')).toBeNull();
  });

  it('inicia Checkout Pro, navega al estado pendiente y nunca trata el retorno como confirmación', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento() } as any);
    const postSpy = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: { id: 'purchase-1', estadoCompra: 'PendingPayment', estadoReserva: 'Active', checkoutState: 'Ready', checkoutUrl: 'https://checkout.example/purchase-1', refundStatus: null } } as any);
    const { findByText, getByText, queryByText } = render(<EventDetailScreen />);
    await findByText('Festival de Verano');
    fireEvent.press(getByText('Comprar entradas'));
    fireEvent.press(await findByText('Continuar al pago'));
    await waitFor(() => expect(postSpy).toHaveBeenCalledWith('/purchases', { eventoId: 'evento-1', ticketTypeId: 'tipo-general', cantidad: 1 }, expect.any(Object)));
    await waitFor(() => expect(mockOpenUrl).toHaveBeenCalledWith('https://checkout.example/purchase-1'));
    expect(mockReplace).toHaveBeenCalledWith('/purchases/purchase-1');
    expect(queryByText('Descargar comprobante PDF')).toBeNull();
  });

  it('evita el doble inicio mientras Checkout Pro se está preparando', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento() } as any);
    let resolveStart!: (value: unknown) => void;
    const postSpy = jest.spyOn(apiClient, 'post').mockReturnValue(new Promise((resolve) => { resolveStart = resolve; }) as any);
    const { findByText, getByText } = render(<EventDetailScreen />);
    await findByText('Festival de Verano'); fireEvent.press(getByText('Comprar entradas'));
    const button = await findByText('Continuar al pago'); fireEvent.press(button); fireEvent.press(button);
    expect(postSpy).toHaveBeenCalledTimes(1);
    await act(async () => { resolveStart({ data: { id: 'purchase-1', checkoutUrl: null } }); });
  });

  it('mapea EVENT_NOT_AVAILABLE_FOR_PURCHASE a un mensaje claro', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento() } as any);
    jest
      .spyOn(apiClient, 'post')
      .mockRejectedValue(
        new ApiError({ code: 'EVENT_NOT_AVAILABLE_FOR_PURCHASE', message: 'no disponible', traceId: 't' }, 409)
      );

    const { findByText, getByText } = render(<EventDetailScreen />);
    await findByText('Festival de Verano');

    fireEvent.press(getByText('Comprar entradas'));
    fireEvent.press(await findByText('Continuar al pago'));

    expect(await findByText(/ya no está disponible para la compra/)).toBeTruthy();
  });

  it('mapea TICKET_STOCK_INSUFFICIENT a un mensaje claro', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento() } as any);
    jest
      .spyOn(apiClient, 'post')
      .mockRejectedValue(new ApiError({ code: 'TICKET_STOCK_INSUFFICIENT', message: 'sin stock', traceId: 't' }, 409));

    const { findByText, getByText } = render(<EventDetailScreen />);
    await findByText('Festival de Verano');

    fireEvent.press(getByText('Comprar entradas'));
    fireEvent.press(await findByText('Continuar al pago'));

    expect(await findByText(/No queda stock suficiente/)).toBeTruthy();
  });

  it('mapea un 401 a un mensaje de sesión expirada', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento() } as any);
    jest
      .spyOn(apiClient, 'post')
      .mockRejectedValue(new ApiError({ code: 'UNAUTHORIZED', message: 'no autorizado', traceId: 't' }, 401));

    const { findByText, getByText } = render(<EventDetailScreen />);
    await findByText('Festival de Verano');

    fireEvent.press(getByText('Comprar entradas'));
    fireEvent.press(await findByText('Continuar al pago'));

    expect(await findByText(/Tu sesión expiró/)).toBeTruthy();
  });

  it('muestra el aviso de compra de demostración para una entrada paga, antes de confirmar', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento() } as any);

    const { findByText } = render(<EventDetailScreen />);
    await findByText('Festival de Verano');

    // Aparece en el resumen, antes de tocar "Comprar entradas"/"Confirmar compra".
    expect(await findByText('Serás redirigido a Mercado Pago para completar el pago.')).toBeTruthy();
  });

  it('no incluye ningún campo de pago (tarjeta, pasarela) en el flujo de compra', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento() } as any);

    const { findByText, getByText, queryByText, queryByPlaceholderText } = render(<EventDetailScreen />);
    await findByText('Festival de Verano');
    fireEvent.press(getByText('Comprar entradas'));
    await findByText('Continuar al pago');

    expect(queryByText(/tarjeta/i)).toBeNull();
    expect(queryByText(/cvv/i)).toBeNull();
    expect(queryByText(/vencimiento/i)).toBeNull();
    expect(queryByText(/pasarela/i)).toBeNull();
    expect(queryByPlaceholderText(/número de tarjeta/i)).toBeNull();
  });

  it('reintenta un resultado ambiguo con la misma clave de idempotencia', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({ data: evento() } as any);
    const postSpy = jest.spyOn(apiClient, 'post')
      .mockRejectedValueOnce(new Error('network ambiguous'))
      .mockResolvedValueOnce({ data: { id: 'purchase-1', checkoutUrl: null } } as any);
    const { findByText, getByText } = render(<EventDetailScreen />);

    await findByText('Festival de Verano');
    fireEvent.press(getByText('Comprar entradas'));
    fireEvent.press(await findByText('Continuar al pago'));
    await findByText('No se pudo completar la compra. Intentá de nuevo.');
    fireEvent.press(getByText('Continuar al pago'));

    await waitFor(() => expect(postSpy).toHaveBeenCalledTimes(2));
    const firstKey = (postSpy.mock.calls[0][2] as { headers: { 'Idempotency-Key': string } }).headers['Idempotency-Key'];
    const retryKey = (postSpy.mock.calls[1][2] as { headers: { 'Idempotency-Key': string } }).headers['Idempotency-Key'];
    expect(retryKey).toBe(firstKey);
  });

  it('entrada gratuita confirma directamente con el flujo existente y nunca abre Checkout Pro', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({
      data: evento({ ticketGroups: [{ id: 'tipo-free', nombre: 'Entrada libre', precio: 0, cantidadDisponible: 5 }] }),
    } as any);
    const postSpy = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: { id: 'compra-free', tickets: [] } } as any);
    const { findByText, getByText } = render(<EventDetailScreen />);

    await findByText('Festival de Verano');
    fireEvent.press(getByText('Obtener entrada'));
    fireEvent.press(getByText('Continuar'));

    await waitFor(() => expect(postSpy).toHaveBeenCalledWith('/tickets/buy', { eventoId: 'evento-1', ticketTypeId: 'tipo-free', cantidad: 1 }));
    expect(mockOpenUrl).not.toHaveBeenCalled();
  });

  it('entrada gratuita: el botón dice "Obtener entrada" y el texto aclara que no requiere pago', async () => {
    mockAuthValue = { user: { uid: 'cliente-1' }, initializing: false, hasAccion: () => true };
    jest.spyOn(apiClient, 'get').mockResolvedValue({
      data: evento({ ticketGroups: [{ id: 'tipo-free', nombre: 'Entrada libre', precio: 0, cantidadDisponible: 5 }] }),
    } as any);

    const { findByText, getByText, queryByText } = render(<EventDetailScreen />);
    await findByText('Festival de Verano');

    expect(await findByText('Entrada gratuita: no requiere pago.')).toBeTruthy();
    expect(queryByText('Comprar entradas')).toBeNull();
    expect(queryByText('Serás redirigido a Mercado Pago para completar el pago.')).toBeNull();

    fireEvent.press(getByText('Obtener entrada'));

    expect(getByText('Continuar')).toBeTruthy();
  });
});
