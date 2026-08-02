export interface ApiErrorBody {
  code: string;
  message: string;
  traceId: string;
  errors?: Record<string, string[]>;
  detail?: string;
}

export function isApiErrorBody(data: unknown): data is ApiErrorBody {
  return (
    typeof data === 'object' &&
    data !== null &&
    typeof (data as Record<string, unknown>).code === 'string' &&
    typeof (data as Record<string, unknown>).message === 'string'
  );
}

/**
 * Espejo tipado del contrato uniforme de error de la API
 * (`{ code, message, traceId, errors?, detail? }`, ver API_Documentation.md §11).
 * `status` puede faltar ante un error de red sin respuesta HTTP.
 */
export class ApiError extends Error {
  readonly code: string;
  readonly traceId: string;
  readonly errors?: Record<string, string[]>;
  readonly detail?: string;
  readonly status?: number;
  readonly isForbidden: boolean;
  readonly isUnauthorized: boolean;

  constructor(body: ApiErrorBody, status?: number) {
    super(body.message);
    this.name = 'ApiError';
    this.code = body.code;
    this.traceId = body.traceId;
    this.errors = body.errors;
    this.detail = body.detail;
    this.status = status;
    this.isForbidden = status === 403;
    this.isUnauthorized = status === 401;
  }
}
