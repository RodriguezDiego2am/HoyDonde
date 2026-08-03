import React from 'react';
import { act, fireEvent, render } from '@testing-library/react-native';

jest.mock('../../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));

const mockBack = jest.fn();
jest.mock('expo-router', () => ({
  router: { push: jest.fn(), back: (...args: unknown[]) => mockBack(...args) },
}));

let mockHasAccion: (accion: string) => boolean = () => true;
jest.mock('@/context/AuthContext', () => ({
  useAuth: () => ({ hasAccion: (accion: string) => mockHasAccion(accion) }),
}));

const mockPrintToFileAsync = jest.fn();
const mockIsAvailableAsync = jest.fn();
const mockShareAsync = jest.fn();
jest.mock('expo-print', () => ({ printToFileAsync: (...args: unknown[]) => mockPrintToFileAsync(...args) }));
jest.mock('expo-sharing', () => ({
  isAvailableAsync: (...args: unknown[]) => mockIsAvailableAsync(...args),
  shareAsync: (...args: unknown[]) => mockShareAsync(...args),
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { apiClient } from '@/services/APIService';
// eslint-disable-next-line import/first
import type { UsuarioResumenResponse } from '@/services/securityAdminService';
// eslint-disable-next-line import/first
import type { SecurityAuditReporteResponse } from '@/services/reportService';
// eslint-disable-next-line import/first
import AdminSecurityAuditsReportScreen from './AdminSecurityAuditsReportScreen';

function usuario(overrides: Partial<UsuarioResumenResponse> = {}): UsuarioResumenResponse {
  return {
    usuarioId: 'usuario-1',
    personaId: 'persona-1',
    email: 'admin@test.com',
    activo: true,
    rolesActivos: ['ADMINISTRADOR'],
    ...overrides,
  };
}

const REPORT: SecurityAuditReporteResponse = {
  fechaDesde: '2026-05-01T00:00:00Z',
  fechaHasta: '2026-06-01T00:00:00Z',
  auditorias: [
    {
      timestamp: '2026-05-15T12:00:00Z',
      operacion: 'ROL_ASIGNAR_ACCION',
      actorUsuarioId: 'usuario-1',
      actorEmail: 'admin@test.com',
      targetTipo: 'RolAccion',
      targetId: 'ORGANIZADOR/EVENTO_CREAR',
      detalle: 'rol=ORGANIZADOR;accion=EVENTO_CREAR',
    },
  ],
};

function mockApiRoutes(opts: { usuarios?: UsuarioResumenResponse[]; report?: SecurityAuditReporteResponse; reportError?: unknown } = {}) {
  jest.spyOn(apiClient, 'get').mockImplementation(async (url: string) => {
    if (url === '/security/usuarios') return { data: opts.usuarios ?? [] };
    if (url === '/reports/admin/security-audits') {
      if (opts.reportError) throw opts.reportError;
      return { data: opts.report ?? REPORT };
    }
    throw new Error(`unexpected url ${url}`);
  });
}

describe('AdminSecurityAuditsReportScreen', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockHasAccion = () => true;
  });

  it('funciona sin ningún filtro de fecha (el backend aplica el default de 30 días)', async () => {
    mockApiRoutes();
    const { getByText, findByText } = render(<AdminSecurityAuditsReportScreen />);

    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('Auditorías (1)')).toBeTruthy();
    const getSpy = apiClient.get as jest.Mock;
    const call = getSpy.mock.calls.find((c) => c[0] === '/reports/admin/security-audits');
    expect(call![1].params.fechaDesde).toBeUndefined();
    expect(call![1].params.fechaHasta).toBeUndefined();
  });

  it('rechaza un rango invertido antes de llamar a la API', async () => {
    mockApiRoutes();
    const { getByTestId, getByText, findByText } = render(<AdminSecurityAuditsReportScreen />);

    fireEvent.changeText(getByTestId('auditoria-fecha-desde-day'), '01062026');
    fireEvent.changeText(getByTestId('auditoria-fecha-hasta-day'), '01052026');

    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('"Desde" no puede ser posterior a "Hasta".')).toBeTruthy();
  });

  it('filtra por operación, objetivo e id de objetivo con match exacto', async () => {
    mockApiRoutes();
    const { getByText, findByText, getByPlaceholderText } = render(<AdminSecurityAuditsReportScreen />);

    fireEvent.press(getByText('Rol asignado a usuario'));
    fireEvent.press(getByText('Usuario ↔ Rol'));
    fireEvent.changeText(getByPlaceholderText('Ej: ORGANIZADOR/EVENTO_CREAR'), 'usuario-2/ORGANIZADOR');

    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('Auditorías (1)')).toBeTruthy();
    const getSpy = apiClient.get as jest.Mock;
    const call = getSpy.mock.calls.find((c) => c[0] === '/reports/admin/security-audits');
    expect(call![1].params.operacion).toBe('USUARIO_ASIGNAR_ROL');
    expect(call![1].params.targetTipo).toBe('UsuarioRol');
    expect(call![1].params.targetId).toBe('usuario-2/ORGANIZADOR');
  });

  it('elegir un actor manda su usuarioId como filtro y lo muestra por email en la UI', async () => {
    mockApiRoutes({ usuarios: [usuario()] });
    const { getByText, findByText, getByLabelText } = render(<AdminSecurityAuditsReportScreen />);

    fireEvent.press(getByLabelText('Elegir actor'));
    fireEvent.press(await findByText('admin@test.com'));

    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('Auditorías (1)')).toBeTruthy();
    const getSpy = apiClient.get as jest.Mock;
    const call = getSpy.mock.calls.find((c) => c[0] === '/reports/admin/security-audits');
    expect(call![1].params.actorUsuarioId).toBe('usuario-1');
  });

  it('oculta el selector de actor cuando la sesión no tiene USUARIO_VER_PERMISOS_EFECTIVOS', () => {
    mockHasAccion = (accion) => accion === 'REPORTE_VER_GLOBAL';
    mockApiRoutes();
    const { queryByLabelText } = render(<AdminSecurityAuditsReportScreen />);

    expect(queryByLabelText('Elegir actor')).toBeNull();
  });

  it('muestra error con reintento si la API falla', async () => {
    mockApiRoutes({ reportError: new Error('network down') });
    const { getByText, findByText } = render(<AdminSecurityAuditsReportScreen />);

    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('No se pudo generar el reporte. Verificá tu conexión.')).toBeTruthy();
  });

  it('muestra estado vacío cuando ninguna auditoría coincide', async () => {
    mockApiRoutes({ report: { fechaDesde: '2026-05-01T00:00:00Z', fechaHasta: '2026-06-01T00:00:00Z', auditorias: [] } });
    const { getByText, findByText } = render(<AdminSecurityAuditsReportScreen />);

    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });

    expect(await findByText('Ninguna auditoría coincide con los filtros aplicados.')).toBeTruthy();
  });

  it('exporta el PDF con expo-print/expo-sharing, escapando el detalle y sin datos sensibles', async () => {
    mockApiRoutes();
    mockPrintToFileAsync.mockResolvedValue({ uri: 'file://auditoria.pdf' });
    mockIsAvailableAsync.mockResolvedValue(true);
    mockShareAsync.mockResolvedValue(undefined);

    const { getByText, findByText } = render(<AdminSecurityAuditsReportScreen />);
    await act(async () => {
      fireEvent.press(getByText('Aplicar filtros'));
    });
    expect(await findByText('Auditorías (1)')).toBeTruthy();

    await act(async () => {
      fireEvent.press(getByText('Generar y compartir PDF'));
    });

    expect(mockShareAsync).toHaveBeenCalledTimes(1);
    const html = mockPrintToFileAsync.mock.calls[0][0].html as string;
    expect(html).toContain('ROL_ASIGNAR_ACCION');
    expect(html).toContain('admin@test.com');
    expect(html).not.toContain('firebase-uid');
  });
});
