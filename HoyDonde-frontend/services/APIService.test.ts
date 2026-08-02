import type { AxiosError } from 'axios';

jest.mock('../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));

// El import de "./APIService" más abajo se hoistea (por ser un import ESM) por
// encima de este "const"; la factory de jest.mock debe leer mockAuth de forma
// diferida (getter) para no capturar un valor undefined al ejecutarse antes de
// tiempo.
const mockAuth: { currentUser: { getIdToken: jest.Mock } | null } = { currentUser: null };
jest.mock('../config/firebase', () => ({
  get auth() {
    return mockAuth;
  },
}));

const mockSignOut = jest.fn().mockResolvedValue(undefined);
jest.mock('firebase/auth', () => ({ signOut: (...args: unknown[]) => (mockSignOut as any)(...args) }));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { apiClient } from './APIService';

function buildAxiosError(status: number, data?: unknown): AxiosError {
  return {
    isAxiosError: true,
    name: 'AxiosError',
    message: 'Request failed',
    toJSON: () => ({}),
    config: { headers: { set: jest.fn() } } as any,
    response: { status, data, statusText: '', headers: {}, config: {} as any } as any,
  } as AxiosError;
}

function getResponseRejectedHandler() {
  return (
    apiClient.interceptors.response as unknown as {
      handlers: { rejected: (error: AxiosError) => unknown }[];
    }
  ).handlers[0].rejected;
}

describe('apiClient response interceptor', () => {
  beforeEach(() => {
    jest.clearAllMocks();
    mockAuth.currentUser = null;
  });

  it('refreshes the token once and retries the request on 401', async () => {
    mockAuth.currentUser = { getIdToken: jest.fn().mockResolvedValue('fresh-token') };
    const requestSpy = jest.spyOn(apiClient, 'request').mockResolvedValueOnce({ data: 'ok' } as any);

    const rejected = getResponseRejectedHandler();
    const result = await rejected(buildAxiosError(401));

    expect(mockAuth.currentUser.getIdToken).toHaveBeenCalledWith(true);
    expect(requestSpy).toHaveBeenCalledTimes(1);
    expect(result).toEqual({ data: 'ok' });
    expect(mockSignOut).not.toHaveBeenCalled();
  });

  it('does not retry twice and signs out when the retried request also fails', async () => {
    mockAuth.currentUser = { getIdToken: jest.fn().mockResolvedValue('fresh-token') };
    const requestSpy = jest.spyOn(apiClient, 'request').mockRejectedValueOnce(new Error('still unauthorized'));

    const rejected = getResponseRejectedHandler();
    await expect(rejected(buildAxiosError(401))).rejects.toMatchObject({ code: 'UNAUTHORIZED', status: 401 });

    expect(requestSpy).toHaveBeenCalledTimes(1);
    expect(mockSignOut).toHaveBeenCalledTimes(1);
  });

  it('signs out immediately on 401 when there is no session to refresh', async () => {
    mockAuth.currentUser = null;
    const rejected = getResponseRejectedHandler();

    await expect(rejected(buildAxiosError(401))).rejects.toMatchObject({ code: 'UNAUTHORIZED' });
    expect(mockSignOut).toHaveBeenCalledTimes(1);
  });

  it('preserves the session and returns an identifiable ApiError on 403', async () => {
    const rejected = getResponseRejectedHandler();
    const error = buildAxiosError(403, { code: 'EVENT_OWNERSHIP', message: 'No autorizado', traceId: 'trace-1' });

    await expect(rejected(error)).rejects.toMatchObject({
      code: 'EVENT_OWNERSHIP',
      isForbidden: true,
      traceId: 'trace-1',
    });
    expect(mockSignOut).not.toHaveBeenCalled();
  });
});
