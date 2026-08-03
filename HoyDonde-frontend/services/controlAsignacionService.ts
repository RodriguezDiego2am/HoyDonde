import { apiClient } from './APIService';
import type { EventEstado } from './APIService';

/** Espejo de ControlAsignacionResponseDto — nunca expone UsuarioId ni el UID de Firebase. */
export interface ControlAsignacionResponse {
  controlPersonaId: string;
  eventId: string;
  assignedByPersonaId: string;
  createdAt: string;
}

/** Espejo de ControlResumenResponseDto (GET /events/organizer/controls). */
export interface ControlResumenResponse {
  controlPersonaId: string;
  userName: string;
  activo: boolean;
}

/** Espejo de ControlAsignadoResponseDto (GET /events/{eventId}/controls). */
export interface ControlAsignadoResponse {
  controlPersonaId: string;
  userName: string;
  activo: boolean;
  assignedByPersonaId: string;
  createdAt: string;
}

/** Espejo de EventoAsignadoResponseDto (GET /events/control/me) — nunca incluye tipos de ticket, precio ni stock. */
export interface EventoAsignadoResponse {
  eventId: string;
  nombre: string;
  ubicacion: string;
  fechaInicio: string;
  fechaFin: string;
  estado: EventEstado;
}

/** Alta/consulta de Control (API-MVP 3 y 5, API_Documentation.md §8): controlPersonaId nunca se pide al usuario a mano, siempre se elige de una lista real. */
export const controlAsignacionService = {
  /** POST /api/events/{eventId}/controls/{controlPersonaId} — Policy CONTROL_CREAR. Idempotente: repetir el mismo par es un no-op exitoso. */
  assignExisting: async (eventId: string, controlPersonaId: string): Promise<ControlAsignacionResponse> => {
    const response = await apiClient.post<ControlAsignacionResponse>(`/events/${eventId}/controls/${controlPersonaId}`);
    return response.data;
  },

  /** GET /api/events/organizer/controls — Controles que el organizador autenticado asignó alguna vez, sin duplicados. */
  getMyControls: async (): Promise<ControlResumenResponse[]> => {
    const response = await apiClient.get<ControlResumenResponse[]>('/events/organizer/controls');
    return response.data;
  },

  /** GET /api/events/{eventId}/controls — Controles asignados a un evento propio. */
  getEventControls: async (eventId: string): Promise<ControlAsignadoResponse[]> => {
    const response = await apiClient.get<ControlAsignadoResponse[]>(`/events/${eventId}/controls`);
    return response.data;
  },

  /** GET /api/events/control/me — Policy TICKET_VALIDAR. Eventos a los que el Control autenticado fue asignado. */
  getMyAssignedEvents: async (): Promise<EventoAsignadoResponse[]> => {
    const response = await apiClient.get<EventoAsignadoResponse[]>('/events/control/me');
    return response.data;
  },
};
