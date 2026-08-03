import { CONTROL_EMAIL_DOMAIN } from '@/utils/controlLoginEmail';

/**
 * Deriva un nombre visible ("CONTROL 1") del email sintético de una cuenta Control
 * ({userName}@control.hoydonde.com, ver utils/controlLoginEmail.ts) sin exponer jamás el email
 * completo en pantalla. Si el email no usa ese dominio (cuenta no-Control), cae al local-part
 * igual, nunca al email completo.
 */
export function controlDisplayName(email: string | null | undefined): string {
  if (!email) return 'CONTROL';

  const localPart = email.endsWith(CONTROL_EMAIL_DOMAIN)
    ? email.slice(0, -CONTROL_EMAIL_DOMAIN.length)
    : email.split('@')[0];

  const display = localPart.replace(/[_.-]+/g, ' ').trim().toUpperCase();
  return display || 'CONTROL';
}
