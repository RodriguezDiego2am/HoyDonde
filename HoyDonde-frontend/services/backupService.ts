import { apiClient } from './APIService';

/** Public history contract for native managed Firestore exports/imports. Provider error details and storage URIs are intentionally never exposed. */
export type FirestoreBackupKind = 'Manual' | 'AutomaticPreRestore' | 'Restore';
export type FirestoreBackupOperation = 'Export' | 'Import';
export type FirestoreBackupState = 'Pending' | 'Running' | 'Succeeded' | 'Failed';

export interface FirestoreBackupResponse {
  id: string;
  kind: FirestoreBackupKind;
  operation: FirestoreBackupOperation;
  state: FirestoreBackupState;
  createdAt: string;
  completedAt: string | null;
}

/** Administración de copias nativas de Firestore. El actor siempre se resuelve desde el token. */
export const backupService = {
  list: async (): Promise<FirestoreBackupResponse[]> => {
    const response = await apiClient.get<FirestoreBackupResponse[]>('/backups');
    return response.data;
  },

  createManual: async (): Promise<FirestoreBackupResponse> => {
    const response = await apiClient.post<FirestoreBackupResponse>('/backups');
    return response.data;
  },

  restore: async (backupId: string): Promise<FirestoreBackupResponse> => {
    const response = await apiClient.post<FirestoreBackupResponse>(`/backups/${backupId}/restore`);
    return response.data;
  },

  // The backend also validates this flag; a client cannot delete history by omitting confirmation.
  delete: async (backupId: string): Promise<void> => {
    await apiClient.delete(`/backups/${backupId}`, { data: { confirmarEliminacion: true } });
  },
};
