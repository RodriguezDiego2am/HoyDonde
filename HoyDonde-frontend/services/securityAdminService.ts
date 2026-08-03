import { apiClient } from './APIService';

/** Espejo de RolResponseDto (HoyDonde.API/DTOs). `codigo` es la identidad del documento: nunca editable. */
export interface RolResponse {
  codigo: string;
  nombre: string;
  descripcion: string;
  activo: boolean;
  acciones: string[];
}

/** Espejo de AccionResponseDto — `descripcion` es el nombre legible, `codigo` el técnico. */
export interface AccionResponse {
  codigo: string;
  descripcion: string;
  activo: boolean;
}

/** Espejo de UsuarioResumenResponseDto — nunca incluye ExternalSubjectId. */
export interface UsuarioResumenResponse {
  usuarioId: string;
  personaId: string;
  email: string;
  activo: boolean;
  rolesActivos: string[];
}

/** Espejo de PermisosEfectivosResponseDto — resuelto en vivo, nunca cacheado en el backend. */
export interface PermisosEfectivosResponse {
  usuarioId: string | null;
  personaId: string | null;
  usuarioActivo: boolean;
  roles: string[];
  acciones: string[];
}

export interface CreateRolPayload {
  codigo: string;
  nombre: string;
  descripcion: string;
}

export interface UpdateRolPayload {
  nombre: string;
  descripcion: string;
}

/**
 * Administración de roles/acciones/usuarios (`/api/security`, API_Documentation.md §10). El actor
 * siempre se resuelve en el backend desde el token — ningún método de acá manda un actor en el body.
 */
export const securityAdminService = {
  /** GET /api/security/roles — Policy ROL_EDITAR. */
  listRoles: async (): Promise<RolResponse[]> => {
    const response = await apiClient.get<RolResponse[]>('/security/roles');
    return response.data;
  },

  /** GET /api/security/roles/{codigo} — Policy ROL_EDITAR. Incluye las acciones asignadas. */
  getRol: async (codigo: string): Promise<RolResponse> => {
    const response = await apiClient.get<RolResponse>(`/security/roles/${codigo}`);
    return response.data;
  },

  /** POST /api/security/roles — Policy ROL_CREAR. */
  createRol: async (payload: CreateRolPayload): Promise<RolResponse> => {
    const response = await apiClient.post<RolResponse>('/security/roles', payload);
    return response.data;
  },

  /** PUT /api/security/roles/{codigo} — Policy ROL_EDITAR. Solo nombre/descripción: el código es inmutable. */
  updateRol: async (codigo: string, payload: UpdateRolPayload): Promise<RolResponse> => {
    const response = await apiClient.put<RolResponse>(`/security/roles/${codigo}`, payload);
    return response.data;
  },

  /** POST /api/security/roles/{codigo}/activar o /desactivar — Policy ROL_ACTIVAR. Idempotente. */
  setRolActivo: async (codigo: string, activo: boolean): Promise<void> => {
    await apiClient.post(`/security/roles/${codigo}/${activo ? 'activar' : 'desactivar'}`);
  },

  /** DELETE /api/security/roles/{codigo} — Policy ROL_ELIMINAR. Baja física: solo un rol personalizado, inactivo, sin usuarios asignados. */
  deleteRol: async (codigo: string): Promise<void> => {
    await apiClient.delete(`/security/roles/${codigo}`);
  },

  /** GET /api/security/acciones — Policy ROL_ASIGNAR_ACCION. Catálogo completo, siempre las mismas 20. */
  listAcciones: async (): Promise<AccionResponse[]> => {
    const response = await apiClient.get<AccionResponse[]>('/security/acciones');
    return response.data;
  },

  /** POST /api/security/roles/{rolCodigo}/acciones/{accionCodigo} — Policy ROL_ASIGNAR_ACCION. Idempotente. */
  asignarAccion: async (rolCodigo: string, accionCodigo: string): Promise<void> => {
    await apiClient.post(`/security/roles/${rolCodigo}/acciones/${accionCodigo}`);
  },

  /** DELETE /api/security/roles/{rolCodigo}/acciones/{accionCodigo} — Policy ROL_QUITAR_ACCION. Idempotente. */
  quitarAccion: async (rolCodigo: string, accionCodigo: string): Promise<void> => {
    await apiClient.delete(`/security/roles/${rolCodigo}/acciones/${accionCodigo}`);
  },

  /** GET /api/security/usuarios — Policy USUARIO_VER_PERMISOS_EFECTIVOS. */
  listUsuarios: async (): Promise<UsuarioResumenResponse[]> => {
    const response = await apiClient.get<UsuarioResumenResponse[]>('/security/usuarios');
    return response.data;
  },

  /** GET /api/security/usuarios/{usuarioId}/permisos-efectivos — Policy USUARIO_VER_PERMISOS_EFECTIVOS. */
  getPermisosEfectivos: async (usuarioId: string): Promise<PermisosEfectivosResponse> => {
    const response = await apiClient.get<PermisosEfectivosResponse>(`/security/usuarios/${usuarioId}/permisos-efectivos`);
    return response.data;
  },

  /** POST /api/security/usuarios/{usuarioId}/roles/{rolCodigo} — Policy USUARIO_ASIGNAR_ROL. Idempotente. */
  asignarRol: async (usuarioId: string, rolCodigo: string): Promise<void> => {
    await apiClient.post(`/security/usuarios/${usuarioId}/roles/${rolCodigo}`);
  },

  /** DELETE /api/security/usuarios/{usuarioId}/roles/{rolCodigo} — Policy USUARIO_QUITAR_ROL. Mismo guard del último Administrador. */
  quitarRol: async (usuarioId: string, rolCodigo: string): Promise<void> => {
    await apiClient.delete(`/security/usuarios/${usuarioId}/roles/${rolCodigo}`);
  },

  /** POST /api/security/usuarios/{usuarioId}/activar o /desactivar — Policy USUARIO_DESACTIVAR. Mismo guard del último Administrador. */
  setUsuarioActivo: async (usuarioId: string, activo: boolean): Promise<void> => {
    await apiClient.post(`/security/usuarios/${usuarioId}/${activo ? 'activar' : 'desactivar'}`);
  },
};
