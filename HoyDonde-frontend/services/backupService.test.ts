jest.mock('../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

// eslint-disable-next-line import/first
import { apiClient } from './APIService';
// eslint-disable-next-line import/first
import { backupService } from './backupService';

describe('backupService', () => {
  afterEach(() => jest.restoreAllMocks());

  it('crea una exportación manual nativa sin enviar datos de documentos', async () => {
    const post = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: {} } as any);

    await backupService.createManual();

    expect(post).toHaveBeenCalledWith('/backups');
  });

  it('solicita la restauración para la copia seleccionada', async () => {
    const post = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: {} } as any);

    await backupService.restore('backup-1');

    expect(post).toHaveBeenCalledWith('/backups/backup-1/restore');
  });

  it('manda la confirmación explícita exigida por el backend al eliminar historial', async () => {
    const remove = jest.spyOn(apiClient, 'delete').mockResolvedValue({ data: {} } as any);

    await backupService.delete('backup-1');

    expect(remove).toHaveBeenCalledWith('/backups/backup-1', { data: { confirmarEliminacion: true } });
  });
});
