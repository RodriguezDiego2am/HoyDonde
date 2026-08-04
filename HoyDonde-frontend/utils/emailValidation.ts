const EMAIL_FORMAT = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Validación de formato únicamente (sin verificar existencia): usuario@dominio.tld. */
export function isValidEmailFormat(value: string): boolean {
  return EMAIL_FORMAT.test(value.trim());
}
