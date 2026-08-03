import { apiClient } from './APIService';
import { ApiError } from './apiError';

export interface TicketValidationRequest {
  ticketId: string;
  eventId: string;
}

export type TicketValidationOutcomeKind =
  | 'valid'
  | 'alreadyUsed'
  | 'anulado'
  | 'eventoCancelado'
  | 'eventoFinalizado'
  | 'notAuthorized'
  | 'notFound'
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
};

const GENERIC_NETWORK_MESSAGE = 'No se pudo conectar con el servidor. Verificá tu conexión.';

/**
 * POST /api/tickets/validate?ticketId=...&eventId=... — Policy TICKET_VALIDAR
 * (API_Documentation.md §9, TicketsController.ValidateTicket). La API decide todo: pertenencia
 * del Control al evento, vigencia del evento y estado del ticket — esta función nunca infiere
 * validez localmente, solo traduce la respuesta real a un resultado tipado.
 */
export async function validateTicket({ ticketId, eventId }: TicketValidationRequest): Promise<TicketValidationResult> {
  try {
    const response = await apiClient.post<{ valid: boolean; message: string }>('/tickets/validate', null, {
      params: { ticketId, eventId },
    });
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
