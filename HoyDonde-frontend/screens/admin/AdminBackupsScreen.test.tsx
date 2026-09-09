import React from 'react';
import { fireEvent, render, waitFor } from '@testing-library/react-native';

let mockHasAccion: (accion: string) => boolean = () => false;
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => ({ hasAccion: (accion: string) => mockHasAccion(accion) }),
}));

jest.mock('@/services/APIService', () => ({
  ApiError: class ApiError extends Error {
    isUnauthorized = false;
    isForbidden = false;
  },
}));

const mockList = jest.fn();
const mockCreateManual = jest.fn();
const mockRestore = jest.fn();
const mockDelete = jest.fn();
jest.mock('@/services/backupService', () => ({
  backupService: {
    list: (...args: unknown[]) => mockList(...args),
    createManual: (...args: unknown[]) => mockCreateManual(...args),
    restore: (...args: unknown[]) => mockRestore(...args),
    delete: (...args: unknown[]) => mockDelete(...args),
  },
}));

// eslint-disable-next-line import/first
import AdminBackupsScreen from './AdminBackupsScreen';

const successfulExport = {
  id: 'backup-1',
  kind: 'Manual',
  operation: 'Export',
  state: 'Succeeded',
  createdAt: '2026-06-01T12:00:00.000Z',
  completedAt: '2026-06-01T12:01:00.000Z',
};

describe('AdminBackupsScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockHasAccion = () => false;
    mockList.mockResolvedValue([successfulExport]);
    mockCreateManual.mockResolvedValue(successfulExport);
    mockRestore.mockResolvedValue({ ...successfulExport, id: 'restore-1', operation: 'Import', kind: 'Restore', state: 'Pending' });
    mockDelete.mockResolvedValue(undefined);
  });

  it('no muestra los controles de copia sin BACKUP_ADMINISTRAR', () => {
    const { getByText, queryByText } = render(<AdminBackupsScreen />);

    expect(getByText('Tu cuenta no tiene permiso para administrar copias.')).toBeTruthy();
    expect(queryByText('Crear copia manual')).toBeNull();
  });

  it('crea una copia manual solo con la acción dedicada', async () => {
    mockHasAccion = (accion) => accion === 'BACKUP_ADMINISTRAR';
    const { findByText, getByText } = render(<AdminBackupsScreen />);

    await findByText('Copia manual de Firestore');
    fireEvent.press(getByText('Crear copia manual'));

    await waitFor(() => expect(mockCreateManual).toHaveBeenCalledTimes(1));
  });

  it('pide confirmación antes de iniciar una restauración', async () => {
    mockHasAccion = (accion) => accion === 'BACKUP_ADMINISTRAR';
    const { findByText, getByText } = render(<AdminBackupsScreen />);

    await findByText('Copia manual de Firestore');
    fireEvent.press(getByText('Restaurar'));
    expect(getByText('Antes de importar se creará una copia automática del estado actual. La importación sobrescribe los documentos incluidos en la copia elegida, pero no elimina documentos creados después.')).toBeTruthy();
    fireEvent.press(getByText('Crear copia y restaurar'));

    await waitFor(() => expect(mockRestore).toHaveBeenCalledWith('backup-1'));
  });

  it('advierte que la eliminación borra los archivos en Cloud Storage antes de confirmar', async () => {
    mockHasAccion = (accion) => accion === 'BACKUP_ADMINISTRAR';
    const { findByText, getAllByText, getByText } = render(<AdminBackupsScreen />);

    await findByText('Copia manual de Firestore');
    fireEvent.press(getAllByText('Eliminar copia')[0]);

    // La copia deja de existir: el aviso nunca puede decir que Cloud Storage no se toca.
    expect(
      getByText(
        'Esta acción elimina primero los archivos de la copia en Cloud Storage y, solo si eso termina bien, su registro de historial. La copia deja de existir y no se puede recuperar.',
      ),
    ).toBeTruthy();
    fireEvent.press(getByText('Eliminar definitivamente'));

    await waitFor(() => expect(mockDelete).toHaveBeenCalledWith('backup-1'));
  });
});
