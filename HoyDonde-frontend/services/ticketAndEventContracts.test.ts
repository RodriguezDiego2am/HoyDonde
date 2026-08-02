jest.mock('../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { apiClient, eventService, ticketService } from './APIService';

describe('eventService.search', () => {
  it('sends the pagination cursor and filters as query params, exactly as the backend expects', async () => {
    const getSpy = jest
      .spyOn(apiClient, 'get')
      .mockResolvedValue({ data: { data: [], hasNextPage: false } } as any);

    await eventService.search({ limit: 10, lastEventId: 'evento-9', categoria: 'Musica' });

    expect(getSpy).toHaveBeenCalledWith('/events', {
      params: { limit: 10, lastEventId: 'evento-9', categoria: 'Musica' },
    });
  });

  it('fetches a single event by id', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: { id: 'evento-1' } } as any);

    const result = await eventService.getById('evento-1');

    expect(getSpy).toHaveBeenCalledWith('/events/evento-1');
    expect(result).toEqual({ id: 'evento-1' });
  });
});

describe('ticketService.buy', () => {
  it('sends exactly { eventoId, ticketTypeId, cantidad } — never price, name, date, or PersonaId', async () => {
    const postSpy = jest.spyOn(apiClient, 'post').mockResolvedValue({ data: [{ id: 'ticket-1' }] } as any);

    await ticketService.buy({ eventoId: 'evento-1', ticketTypeId: 'tipo-1', cantidad: 2 });

    expect(postSpy).toHaveBeenCalledWith('/tickets/buy', {
      eventoId: 'evento-1',
      ticketTypeId: 'tipo-1',
      cantidad: 2,
    });
    const sentPayload = postSpy.mock.calls[0][1] as Record<string, unknown>;
    expect(Object.keys(sentPayload).sort()).toEqual(['cantidad', 'eventoId', 'ticketTypeId']);
  });
});

describe('ticketService.getMine', () => {
  it('reads the caller-scoped ticket list', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: [] } as any);

    await ticketService.getMine();

    expect(getSpy).toHaveBeenCalledWith('/tickets/me');
  });
});
