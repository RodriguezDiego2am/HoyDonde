jest.mock('../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { apiClient } from './APIService';
// eslint-disable-next-line import/first
import { securityAdminService } from './securityAdminService';

describe('securityAdminService', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('listRoles llama a GET /security/roles', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [] } as any);
    await securityAdminService.listRoles();
    expect(getSpy).toHaveBeenCalledWith('/security/roles');
  });

  it('getRol llama a GET /security/roles/{codigo}', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: {} } as any);
    await securityAdminService.getRol('ORGANIZADOR');
    expect(getSpy).toHaveBeenCalledWith('/security/roles/ORGANIZADOR');
  });

  it('createRol envía exactamente {codigo, nombre, descripcion} a POST /security/roles', async () => {
    const postSpy = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: {} } as any);
    await securityAdminService.createRol({ codigo: 'SOPORTE', nombre: 'Soporte', descripcion: 'Atiende reclamos.' });
    expect(postSpy).toHaveBeenCalledWith('/security/roles', {
      codigo: 'SOPORTE',
      nombre: 'Soporte',
      descripcion: 'Atiende reclamos.',
    });
  });

  it('updateRol envía exactamente {nombre, descripcion} a PUT /security/roles/{codigo}, nunca el código', async () => {
    const putSpy = jest.spyOn(apiClient, 'put').mockResolvedValue({ data: {} } as any);
    await securityAdminService.updateRol('SOPORTE', { nombre: 'Soporte técnico', descripcion: 'Nueva descripción' });
    expect(putSpy).toHaveBeenCalledWith('/security/roles/SOPORTE', {
      nombre: 'Soporte técnico',
      descripcion: 'Nueva descripción',
    });
  });

  it('setRolActivo(true) llama a POST /security/roles/{codigo}/activar', async () => {
    const postSpy = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: {} } as any);
    await securityAdminService.setRolActivo('SOPORTE', true);
    expect(postSpy).toHaveBeenCalledWith('/security/roles/SOPORTE/activar');
  });

  it('setRolActivo(false) llama a POST /security/roles/{codigo}/desactivar', async () => {
    const postSpy = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: {} } as any);
    await securityAdminService.setRolActivo('SOPORTE', false);
    expect(postSpy).toHaveBeenCalledWith('/security/roles/SOPORTE/desactivar');
  });

  it('listAcciones llama a GET /security/acciones', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [] } as any);
    await securityAdminService.listAcciones();
    expect(getSpy).toHaveBeenCalledWith('/security/acciones');
  });

  it('asignarAccion llama a POST /security/roles/{rolCodigo}/acciones/{accionCodigo}', async () => {
    const postSpy = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: {} } as any);
    await securityAdminService.asignarAccion('ORGANIZADOR', 'EVENTO_CREAR');
    expect(postSpy).toHaveBeenCalledWith('/security/roles/ORGANIZADOR/acciones/EVENTO_CREAR');
  });

  it('quitarAccion llama a DELETE /security/roles/{rolCodigo}/acciones/{accionCodigo}', async () => {
    const deleteSpy = jest.spyOn(apiClient, 'delete').mockResolvedValue({ data: {} } as any);
    await securityAdminService.quitarAccion('ORGANIZADOR', 'EVENTO_CREAR');
    expect(deleteSpy).toHaveBeenCalledWith('/security/roles/ORGANIZADOR/acciones/EVENTO_CREAR');
  });

  it('listUsuarios llama a GET /security/usuarios', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [] } as any);
    await securityAdminService.listUsuarios();
    expect(getSpy).toHaveBeenCalledWith('/security/usuarios');
  });

  it('getPermisosEfectivos llama a GET /security/usuarios/{usuarioId}/permisos-efectivos', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: {} } as any);
    await securityAdminService.getPermisosEfectivos('usuario-1');
    expect(getSpy).toHaveBeenCalledWith('/security/usuarios/usuario-1/permisos-efectivos');
  });

  it('asignarRol llama a POST /security/usuarios/{usuarioId}/roles/{rolCodigo}', async () => {
    const postSpy = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: {} } as any);
    await securityAdminService.asignarRol('usuario-1', 'CONTROL');
    expect(postSpy).toHaveBeenCalledWith('/security/usuarios/usuario-1/roles/CONTROL');
  });

  it('quitarRol llama a DELETE /security/usuarios/{usuarioId}/roles/{rolCodigo}', async () => {
    const deleteSpy = jest.spyOn(apiClient, 'delete').mockResolvedValue({ data: {} } as any);
    await securityAdminService.quitarRol('usuario-1', 'CONTROL');
    expect(deleteSpy).toHaveBeenCalledWith('/security/usuarios/usuario-1/roles/CONTROL');
  });

  it('setUsuarioActivo(true) llama a POST /security/usuarios/{usuarioId}/activar', async () => {
    const postSpy = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: {} } as any);
    await securityAdminService.setUsuarioActivo('usuario-1', true);
    expect(postSpy).toHaveBeenCalledWith('/security/usuarios/usuario-1/activar');
  });

  it('setUsuarioActivo(false) llama a POST /security/usuarios/{usuarioId}/desactivar', async () => {
    const postSpy = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: {} } as any);
    await securityAdminService.setUsuarioActivo('usuario-1', false);
    expect(postSpy).toHaveBeenCalledWith('/security/usuarios/usuario-1/desactivar');
  });
});
