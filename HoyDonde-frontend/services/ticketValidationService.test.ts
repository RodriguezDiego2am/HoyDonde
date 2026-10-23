jest.mock('../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { apiClient, ApiError } from './APIService';
// eslint-disable-next-line import/first
import { validateTicket } from './ticketValidationService';

// jest.spyOn(apiClient, 'post') reemplaza el método entero: los interceptors de APIService.ts
// (que normalmente convierten el AxiosError real en ApiError, ver toApiError) nunca corren.
// Por eso acá se simula directamente el ApiError que el interceptor ya construiría — mismo
// patrón que controlAsignacionService.test.ts, adaptado porque este endpoint sí puede rechazar.
function ticketValidationError(status: number, message: string): ApiError {
  return new ApiError({ code: 'TICKET_VALIDATION_RESULT', message, traceId: '' }, status);
}

const TOKEN = 'AbCdEfGhIjKlMnOpQrStUv';

describe('validateTicket', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('POST /tickets/validate con el qrToken en el body (nunca en la URL)', async () => {
    const postSpy = jest
      .spyOn(apiClient, 'post')
      .mockResolvedValue({ data: { valid: true, message: 'Ticket validado para este evento.' } } as any);

    const result = await validateTicket({ qrToken: TOKEN });

    expect(postSpy).toHaveBeenCalledWith('/tickets/validate', { qrToken: TOKEN });
    expect(result).toEqual({ kind: 'valid', message: 'Ticket validado para este evento.' });
  });

  it('mapea AlreadyUsed (409) al kind y mensaje reales del backend', async () => {
    jest.spyOn(apiClient, 'post').mockRejectedValue(ticketValidationError(409, 'El ticket ya fue utilizado.'));

    const result = await validateTicket({ qrToken: TOKEN });
    expect(result).toEqual({ kind: 'alreadyUsed', message: 'El ticket ya fue utilizado.' });
  });

  it('mapea Anulado (409) correctamente', async () => {
    jest.spyOn(apiClient, 'post').mockRejectedValue(ticketValidationError(409, 'El ticket fue anulado.'));
    const result = await validateTicket({ qrToken: TOKEN });
    expect(result).toEqual({ kind: 'anulado', message: 'El ticket fue anulado.' });
  });

  it('mapea EventoCancelado (409) correctamente', async () => {
    jest.spyOn(apiClient, 'post').mockRejectedValue(ticketValidationError(409, 'El evento fue cancelado.'));
    const result = await validateTicket({ qrToken: TOKEN });
    expect(result).toEqual({ kind: 'eventoCancelado', message: 'El evento fue cancelado.' });
  });

  it('mapea EventoFinalizado (409) correctamente', async () => {
    jest.spyOn(apiClient, 'post').mockRejectedValue(ticketValidationError(409, 'El evento ya finalizó.'));
    const result = await validateTicket({ qrToken: TOKEN });
    expect(result).toEqual({ kind: 'eventoFinalizado', message: 'El evento ya finalizó.' });
  });

  it('mapea NotAuthorized (403) correctamente', async () => {
    jest
      .spyOn(apiClient, 'post')
      .mockRejectedValue(ticketValidationError(403, 'No autorizado para validar tickets de este evento.'));
    const result = await validateTicket({ qrToken: TOKEN });
    expect(result).toEqual({ kind: 'notAuthorized', message: 'No autorizado para validar tickets de este evento.' });
  });

  it('mapea NotFound (404) correctamente', async () => {
    jest.spyOn(apiClient, 'post').mockRejectedValue(ticketValidationError(404, 'Ticket no encontrado.'));
    const result = await validateTicket({ qrToken: TOKEN });
    expect(result).toEqual({ kind: 'notFound', message: 'Ticket no encontrado.' });
  });

  it('mapea QR desconocido o reemplazado por una transferencia (409) al kind qrInvalido', async () => {
    jest
      .spyOn(apiClient, 'post')
      .mockRejectedValue(ticketValidationError(409, 'El código QR no es válido o fue reemplazado.'));
    const result = await validateTicket({ qrToken: TOKEN });
    expect(result).toEqual({ kind: 'qrInvalido', message: 'El código QR no es válido o fue reemplazado.' });
  });

  it('un error de red (code NETWORK_ERROR, sin status) da kind network con mensaje genérico', async () => {
    jest
      .spyOn(apiClient, 'post')
      .mockRejectedValue(new ApiError({ code: 'NETWORK_ERROR', message: 'Network Error', traceId: '' }, undefined));

    const result = await validateTicket({ qrToken: TOKEN });
    expect(result.kind).toBe('network');
    expect(result.message).toBeTruthy();
  });

  it('un 500 con el contrato uniforme de error da kind unexpected con traceId, sin exponer datos internos', async () => {
    jest.spyOn(apiClient, 'post').mockRejectedValue(
      new ApiError(
        {
          code: 'UNEXPECTED_ERROR',
          message: 'Ocurrió un error inesperado. Contactá al soporte indicando el RequestId.',
          traceId: 'trace-abc',
        },
        500
      )
    );

    const result = await validateTicket({ qrToken: TOKEN });
    expect(result.kind).toBe('unexpected');
    expect(result.traceId).toBe('trace-abc');
    expect(result.message).not.toMatch(/uid|usuarioid|personaid|externalsubjectid/i);
  });

  it('un rechazo que no es ApiError (defensivo) igual resuelve como network, nunca lanza', async () => {
    jest.spyOn(apiClient, 'post').mockRejectedValue(new Error('boom'));
    const result = await validateTicket({ qrToken: TOKEN });
    expect(result.kind).toBe('network');
  });
});
