jest.mock('../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { apiClient } from './APIService';
// eslint-disable-next-line import/first
import { reportService } from './reportService';

describe('reportService', () => {
  afterEach(() => {
    jest.restoreAllMocks();
  });

  it('getOrganizerEventsReport llama a GET /reports/organizer/events con el filtro exacto como query params', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: {} } as any);
    const filter = {
      fechaDesde: '2026-01-01T00:00:00.000Z',
      fechaHasta: '2026-02-01T00:00:00.000Z',
      estado: 'Publicado' as const,
      categoria: 'Musica',
      eventId: 'event-1',
      ticketTypeId: 'tipo-1',
    };

    await reportService.getOrganizerEventsReport(filter);

    expect(getSpy).toHaveBeenCalledWith('/reports/organizer/events', { params: filter });
  });

  it('getOrganizerEventsReport nunca manda organizadorPersonaId (el organizador sale siempre del token en el backend)', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: {} } as any);

    await reportService.getOrganizerEventsReport({ fechaDesde: '2026-01-01T00:00:00.000Z', fechaHasta: '2026-02-01T00:00:00.000Z' });

    const config = getSpy.mock.calls[0][1]!;
    expect(config.params).not.toHaveProperty('organizadorPersonaId');
  });

  it('getAdminEventsReport llama a GET /reports/admin/events con el filtro exacto como query params', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: {} } as any);
    const filter = {
      fechaDesde: '2026-01-01T00:00:00.000Z',
      fechaHasta: '2026-02-01T00:00:00.000Z',
      estado: 'Publicado' as const,
      categoria: 'Musica',
      organizadorPersonaId: 'persona-1',
    };

    await reportService.getAdminEventsReport(filter);

    expect(getSpy).toHaveBeenCalledWith('/reports/admin/events', { params: filter });
  });

  it('getAdminEventsReport nunca manda eventId/ticketTypeId (no forman parte del contrato del reporte global)', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: {} } as any);

    await reportService.getAdminEventsReport({ fechaDesde: '2026-01-01T00:00:00.000Z', fechaHasta: '2026-02-01T00:00:00.000Z' });

    const config = getSpy.mock.calls[0][1]!;
    expect(config.params).not.toHaveProperty('eventId');
    expect(config.params).not.toHaveProperty('ticketTypeId');
  });

  it('getSecurityAuditsReport llama a GET /reports/admin/security-audits con el filtro exacto como query params', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: {} } as any);
    const filter = {
      fechaDesde: '2026-01-01T00:00:00.000Z',
      fechaHasta: '2026-02-01T00:00:00.000Z',
      operacion: 'ROL_ASIGNAR_ACCION',
      actorUsuarioId: 'usuario-1',
      targetTipo: 'UsuarioRol' as const,
      targetId: 'usuario-2/ORGANIZADOR',
    };

    await reportService.getSecurityAuditsReport(filter);

    expect(getSpy).toHaveBeenCalledWith('/reports/admin/security-audits', { params: filter });
  });

  it('getSecurityAuditsReport funciona sin ningún filtro (el backend aplica el default de 30 días)', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: {} } as any);

    await reportService.getSecurityAuditsReport({});

    expect(getSpy).toHaveBeenCalledWith('/reports/admin/security-audits', { params: {} });
  });

  it('getOrganizerSalesReport llama a GET /reports/organizer/sales con el filtro exacto como query params', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: {} } as any);
    const filter = {
      fechaDesde: '2026-01-01T00:00:00.000Z',
      fechaHasta: '2026-02-01T00:00:00.000Z',
      eventId: 'event-1',
      categoria: 'Musica',
      ticketTypeId: 'tipo-1',
    };

    await reportService.getOrganizerSalesReport(filter);

    expect(getSpy).toHaveBeenCalledWith('/reports/organizer/sales', { params: filter });
  });

  it('getOrganizerSalesReport nunca manda organizadorPersonaId', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: {} } as any);

    await reportService.getOrganizerSalesReport({ fechaDesde: '2026-01-01T00:00:00.000Z', fechaHasta: '2026-02-01T00:00:00.000Z' });

    const config = getSpy.mock.calls[0][1]!;
    expect(config.params).not.toHaveProperty('organizadorPersonaId');
  });

  it('getAdminSalesReport llama a GET /reports/admin/sales con el filtro exacto como query params', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: {} } as any);
    const filter = {
      fechaDesde: '2026-01-01T00:00:00.000Z',
      fechaHasta: '2026-02-01T00:00:00.000Z',
      organizadorPersonaId: 'persona-1',
      eventId: 'event-1',
      categoria: 'Deportes',
    };

    await reportService.getAdminSalesReport(filter);

    expect(getSpy).toHaveBeenCalledWith('/reports/admin/sales', { params: filter });
  });

  it('getAdminSalesReport nunca manda ticketTypeId (no forma parte del contrato del reporte de ventas del Admin)', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: {} } as any);

    await reportService.getAdminSalesReport({ fechaDesde: '2026-01-01T00:00:00.000Z', fechaHasta: '2026-02-01T00:00:00.000Z' });

    const config = getSpy.mock.calls[0][1]!;
    expect(config.params).not.toHaveProperty('ticketTypeId');
  });
});
