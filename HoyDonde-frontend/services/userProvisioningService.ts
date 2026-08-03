import { apiClient } from './APIService';

/** Espejo de UsuarioProvisioningResponseDto (HoyDonde.API/DTOs). El UsuarioId/PersonaId nunca se muestran en la UI (CLAUDE.md: la app solo confirma el alta, no expone identificadores internos). */
export interface UsuarioProvisioningResponse {
  message: string;
  usuarioId: string;
  personaId: string;
}

export interface RegisterAdminPayload {
  email: string;
  password: string;
}

export interface RegisterOrganizadorPayload {
  email: string;
  password: string;
}

export interface RegisterControlPayload {
  userName: string;
  password: string;
  eventId: string;
}

/** Altas privilegiadas (Admin/Organizador/Control), nunca autoregistro (API_Documentation.md §6). */
export const userProvisioningService = {
  /** POST /api/users/admin — Policy USUARIO_CREAR_ADMIN. Solo un Administrador. */
  registerAdmin: async (payload: RegisterAdminPayload): Promise<UsuarioProvisioningResponse> => {
    const response = await apiClient.post<UsuarioProvisioningResponse>('/users/admin', payload);
    return response.data;
  },

  /** POST /api/users/organizador — Policy USUARIO_CREAR_ORGANIZADOR. Solo un Administrador. */
  registerOrganizador: async (payload: RegisterOrganizadorPayload): Promise<UsuarioProvisioningResponse> => {
    const response = await apiClient.post<UsuarioProvisioningResponse>('/users/organizador', payload);
    return response.data;
  },

  /** POST /api/users/control — Policy CONTROL_CREAR. Solo el Organizador dueño de `eventId`. */
  registerControl: async (payload: RegisterControlPayload): Promise<UsuarioProvisioningResponse> => {
    const response = await apiClient.post<UsuarioProvisioningResponse>('/users/control', payload);
    return response.data;
  },
};
