jest.mock('../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { apiClient } from './APIService';
// eslint-disable-next-line import/first
import { controlAsignacionService } from './controlAsignacionService';

describe('controlAsignacionService', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('assignExisting llama a POST /events/{eventId}/controls/{controlPersonaId} sin body', async () => {
    const postSpy = jest.spyOn(apiClient, 'post').mockResolvedValue({
      data: { controlPersonaId: 'persona-c1', eventId: 'evento-1', assignedByPersonaId: 'persona-org', createdAt: '2026-08-02T15:00:00Z' },
    } as any);

    const result = await controlAsignacionService.assignExisting('evento-1', 'persona-c1');

    expect(postSpy).toHaveBeenCalledWith('/events/evento-1/controls/persona-c1');
    expect(result.eventId).toBe('evento-1');
  });

  it('getMyControls llama a GET /events/organizer/controls', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({
      data: [{ controlPersonaId: 'persona-c1', userName: 'control_puerta_norte', activo: true }],
    } as any);

    const result = await controlAsignacionService.getMyControls();

    expect(getSpy).toHaveBeenCalledWith('/events/organizer/controls');
    expect(result).toHaveLength(1);
  });

  it('getEventControls llama a GET /events/{eventId}/controls', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [] } as any);

    await controlAsignacionService.getEventControls('evento-1');

    expect(getSpy).toHaveBeenCalledWith('/events/evento-1/controls');
  });

  it('getMyAssignedEvents llama a GET /events/control/me', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [] } as any);

    await controlAsignacionService.getMyAssignedEvents();

    expect(getSpy).toHaveBeenCalledWith('/events/control/me');
  });
});
