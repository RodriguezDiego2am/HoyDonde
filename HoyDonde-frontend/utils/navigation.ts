/**
 * Un returnTo válido es una ruta interna (empieza con "/" único, sin esquema
 * ni protocolo) para evitar que un query param navegue fuera de la app.
 */
export function isSafeReturnTo(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    value.length > 0 &&
    value.startsWith('/') &&
    !value.startsWith('//') &&
    !value.includes('://')
  );
}
