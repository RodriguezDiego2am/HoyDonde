import axios, { AxiosError, InternalAxiosRequestConfig } from 'axios';
import { signOut } from 'firebase/auth';

import { resolveApiUrl } from '../config/apiEnv';
import { auth } from '../config/firebase';
import { ApiError, isApiErrorBody } from './apiError';

export { ApiError } from './apiError';
export type { ApiErrorBody } from './apiError';

type RetryableConfig = InternalAxiosRequestConfig & { _retried?: boolean };

export const apiClient = axios.create({
  baseURL: resolveApiUrl(),
  headers: {
    'Content-Type': 'application/json',
  },
});

apiClient.interceptors.request.use(async (config) => {
  const currentUser = auth.currentUser;
  if (currentUser) {
    const token = await currentUser.getIdToken();
    config.headers.set('Authorization', `Bearer ${token}`);
  }
  return config;
});

function toApiError(error: AxiosError): ApiError {
  const status = error.response?.status;
  const data = error.response?.data;

  if (isApiErrorBody(data)) {
    return new ApiError(data, status);
  }

  return new ApiError(
    {
      code: status === 403 ? 'FORBIDDEN' : status === 401 ? 'UNAUTHORIZED' : 'NETWORK_ERROR',
      message: error.message || 'No se pudo completar la solicitud.',
      traceId: '',
    },
    status
  );
}

apiClient.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const status = error.response?.status;
    const original = error.config as RetryableConfig | undefined;

    if (status === 401) {
      if (original && !original._retried) {
        original._retried = true;
        try {
          const currentUser = auth.currentUser;
          if (!currentUser) {
            throw new Error('No hay sesión activa para refrescar.');
          }
          const freshToken = await currentUser.getIdToken(true);
          original.headers.set('Authorization', `Bearer ${freshToken}`);
          return await apiClient.request(original);
        } catch {
          await signOut(auth).catch(() => {});
          return Promise.reject(toApiError(error));
        }
      }

      // Ya se reintentó una vez (o no hay config para reintentar): la sesión no es válida.
      await signOut(auth).catch(() => {});
    }

    // Un 403 conserva la sesión: la UI decide qué mostrar con ApiError.isForbidden.
    return Promise.reject(toApiError(error));
  }
);

export interface SyncUserPayload {
  fullName?: string;
  dni?: string;
  phoneNumber?: string;
}

export interface SyncUserResponse {
  usuarioId: string;
  personaId: string;
  roles: string[];
  acciones: string[];
}

export const authApi = {
  sync: async (payload?: SyncUserPayload): Promise<SyncUserResponse> => {
    const response = await apiClient.post<SyncUserResponse>('/auth/sync', payload ?? {});
    return response.data;
  },
};

export type EventEstado = 'Borrador' | 'Publicado' | 'Cancelado' | 'Finalizado';

export interface TicketTypeResponse {
  id: string;
  nombre: string;
  precio: number;
  cantidadDisponible: number;
}

export interface EventResponse {
  id: string;
  nombre: string;
  descripcion: string;
  fechaInicio: string;
  fechaFin: string;
  ubicacion: string;
  categoria: string;
  estado: EventEstado;
  ticketGroups: TicketTypeResponse[];
}

export interface PagedResponse<T> {
  data: T[];
  lastDocumentId?: string;
  hasNextPage: boolean;
}

export interface EventSearchFilter {
  categoria?: string;
  ubicacion?: string;
  fechaInicio?: string;
  limit?: number;
  lastEventId?: string;
}

/** Espejo de Event.EventCategory (HoyDonde.API/Models/Event.cs) — únicos valores que la API acepta en `categoria`. */
export type EventCategory = 'Musica' | 'Deportes' | 'Tecnologia' | 'Arte' | 'Otros';

/** Espejo de TicketGroupDto (solo entrada — API_Documentation.md §7.1): nunca lleva id, el servidor lo genera siempre. */
export interface TicketGroupInput {
  nombre: string;
  precio: number;
  cantidadDisponible: number;
}

/** Espejo de EventCreateRequest/EventUpdateRequest — fechaInicio/fechaFin deben viajar en UTC (ISO 8601). */
export interface EventWriteRequest {
  nombre: string;
  descripcion: string;
  fechaInicio: string;
  fechaFin: string;
  ubicacion: string;
  categoria: EventCategory;
  ticketGroups: TicketGroupInput[];
}

export type EventCreateRequest = EventWriteRequest;
export type EventUpdateRequest = EventWriteRequest;

export const eventService = {
  search: async (filter: EventSearchFilter = {}): Promise<PagedResponse<EventResponse>> => {
    const response = await apiClient.get<PagedResponse<EventResponse>>('/events', { params: filter });
    return response.data;
  },

  getById: async (id: string): Promise<EventResponse> => {
    const response = await apiClient.get<EventResponse>(`/events/${id}`);
    return response.data;
  },

  create: async (payload: EventCreateRequest): Promise<EventResponse> => {
    const response = await apiClient.post<EventResponse>('/events', payload);
    return response.data;
  },

  update: async (eventId: string, payload: EventUpdateRequest): Promise<EventResponse> => {
    const response = await apiClient.put<EventResponse>(`/events/${eventId}`, payload);
    return response.data;
  },

  publish: async (eventId: string): Promise<void> => {
    await apiClient.post(`/events/${eventId}/publish`);
  },

  cancel: async (eventId: string): Promise<void> => {
    await apiClient.post(`/events/${eventId}/cancel`);
  },

  /** GET /api/events/organizer/me — todos los eventos propios, cualquier estado. Nunca paginado (EventsController.GetMyEvents). */
  getMyEvents: async (): Promise<EventResponse[]> => {
    const response = await apiClient.get<EventResponse[]>('/events/organizer/me');
    return response.data;
  },

  /** GET /api/events/organizer/{id} — detalle de un evento propio en cualquier estado. */
  getOwnedById: async (eventId: string): Promise<EventResponse> => {
    const response = await apiClient.get<EventResponse>(`/events/organizer/${eventId}`);
    return response.data;
  },
};

/** Estado histórico/persistido del ticket (nunca reescrito por una cancelación de evento). */
export type TicketEstado = 'Emitido' | 'Usado' | 'Anulado';

/** Motivo por el que un ticket utilizable == false; null cuando utilizable == true. */
export type TicketMotivoNoUtilizable = 'Usado' | 'Anulado' | 'EventoCancelado' | 'EventoFinalizado' | null;

export interface TicketResponse {
  id: string;
  eventoId: string;
  ticketTypeId: string;
  clientePersonaId: string;
  fechaCompra: string;
  estado: TicketEstado;
  utilizable: boolean;
  motivoNoUtilizable: TicketMotivoNoUtilizable;
  eventoNombre: string;
  ticketTypeNombre: string;
  precioPagado: number;
  fechaInicio: string;
  fechaFin: string;
}

export interface TicketBuyRequest {
  eventoId: string;
  ticketTypeId: string;
  cantidad: number;
}

export const ticketService = {
  buy: async (payload: TicketBuyRequest): Promise<TicketResponse[]> => {
    const response = await apiClient.post<TicketResponse[]>('/tickets/buy', payload);
    return response.data;
  },

  getMine: async (): Promise<TicketResponse[]> => {
    const response = await apiClient.get<TicketResponse[]>('/tickets/me');
    return response.data;
  },
};

export default apiClient;
