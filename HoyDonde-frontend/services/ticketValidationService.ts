import { apiClient } from './APIService';
import { ApiError } from './apiError';

export interface TicketValidationRequest {
  /** Contenido del QR: el token opaco emitido por el backend. El evento lo deduce la API del propio ticket. */
  qrToken: string;
}

export type TicketValidationOutcomeKind =
  | 'valid'
  | 'alreadyUsed'
  | 'anulado'
  | 'eventoCancelado'
  | 'eventoFinalizado'
  | 'notAuthorized'
  | 'notFound'
  | 'qrInvalido'
  | 'network'
  | 'unexpected';

export interface TicketValidationResult {
  kind: TicketValidationOutcomeKind;
  message: string;
  traceId?: string;
}

/** Espejo exacto de los mensajes públicos de POST /api/tickets/validate (API_Documentation.md §9). */
const KNOWN_MESSAGE_KIND: Record<string, TicketValidationOutcomeKind> = {
  'El ticket ya fue utilizado.': 'alreadyUsed',
  'El ticket fue anulado.': 'anulado',
  'El evento fue cancelado.': 'eventoCancelado',
  'El evento ya finalizó.': 'eventoFinalizado',
  'No autorizado para validar tickets de este evento.': 'notAuthorized',
  'Ticket no encontrado.': 'notFound',
  'El código QR no es válido o fue reemplazado.': 'qrInvalido',
};

const GENERIC_NETWORK_MESSAGE = 'No se pudo conectar con el servidor. Verificá tu conexión.';

/**
 * POST /api/tickets/validate con body { qrToken } — Policy TICKET_VALIDAR
 * (API_Documentation.md §9, TicketsController.ValidateTicket). La API decide todo: pertenencia
 * del Control al evento, vigencia del evento y estado del ticket — esta función nunca infiere
 * validez localmente, solo traduce la respuesta real a un resultado tipado.
 */
export async function validateTicket({ qrToken }: TicketValidationRequest): Promise<TicketValidationResult> {
  try {
    // El token viaja en el body (nunca en la URL) para que no quede en logs de acceso.
    const response = await apiClient.post<{ valid: boolean; message: string }>('/tickets/validate', { qrToken });
    return { kind: 'valid', message: response.data.message };
  } catch (error) {
    if (error instanceof ApiError) {
      if (error.code === 'TICKET_VALIDATION_RESULT') {
        const kind =
          KNOWN_MESSAGE_KIND[error.message] ??
          (error.status === 403 ? 'notAuthorized' : error.status === 404 ? 'notFound' : 'unexpected');
        return { kind, message: error.message };
      }

      if (error.code === 'NETWORK_ERROR') {
        return { kind: 'network', message: GENERIC_NETWORK_MESSAGE };
      }

      return { kind: 'unexpected', message: error.message, traceId: error.traceId || undefined };
    }

    return { kind: 'network', message: GENERIC_NETWORK_MESSAGE };
  }
}
