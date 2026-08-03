jest.mock('../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { apiClient } from './APIService';
// eslint-disable-next-line import/first
import { userProvisioningService } from './userProvisioningService';

describe('userProvisioningService', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('registerAdmin envía exactamente {email, password} a /users/admin', async () => {
    const postSpy = jest
      .spyOn(apiClient, 'post')
      .mockResolvedValue({ data: { message: 'Administrador creado exitosamente.', usuarioId: 'u-0', personaId: 'p-0' } } as any);

    const result = await userProvisioningService.registerAdmin({
      email: 'admin@hoydonde.com',
      password: 'segura123',
    });

    expect(postSpy).toHaveBeenCalledWith('/users/admin', {
      email: 'admin@hoydonde.com',
      password: 'segura123',
    });
    expect(result.message).toBe('Administrador creado exitosamente.');
  });

  it('registerOrganizador envía exactamente {email, password} a /users/organizador', async () => {
    const postSpy = jest
      .spyOn(apiClient, 'post')
      .mockResolvedValue({ data: { message: 'Organizador creado exitosamente.', usuarioId: 'u-1', personaId: 'p-1' } } as any);

    const result = await userProvisioningService.registerOrganizador({
      email: 'organizador@hoydonde.com',
      password: 'segura123',
    });

    expect(postSpy).toHaveBeenCalledWith('/users/organizador', {
      email: 'organizador@hoydonde.com',
      password: 'segura123',
    });
    expect(result.message).toBe('Organizador creado exitosamente.');
  });

  it('registerControl envía exactamente {userName, password, eventId} a /users/control', async () => {
    const postSpy = jest
      .spyOn(apiClient, 'post')
      .mockResolvedValue({ data: { message: 'Control creado exitosamente.', usuarioId: 'u-2', personaId: 'p-2' } } as any);

    await userProvisioningService.registerControl({
      userName: 'control_puerta_norte',
      password: 'segura123',
      eventId: 'evento-1',
    });

    expect(postSpy).toHaveBeenCalledWith('/users/control', {
      userName: 'control_puerta_norte',
      password: 'segura123',
      eventId: 'evento-1',
    });
  });
});
