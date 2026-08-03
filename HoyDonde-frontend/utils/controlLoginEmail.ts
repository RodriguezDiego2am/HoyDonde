/**
 * Mismo dominio sintético que RegisterControlDto arma en el backend
 * (HoyDonde.API/Services/UserService.cs: `var email = $"{userName}{ControlEmailDomain}";`,
 * concatenación cruda — sin trim ni normalización de mayúsculas de ningún lado, ni al crear la
 * cuenta ni al pasarla a Firebase Admin SDK). No adivinar otra regla: es exactamente esta.
 */
export const CONTROL_EMAIL_DOMAIN = '@control.hoydonde.com';

/**
 * Resuelve lo que el usuario tipeó en "Email o usuario" al valor que Firebase Authentication
 * espera para iniciar sesión:
 * - Si contiene "@", se trata como email normal (solo se recorta espacios).
 * - Si no contiene "@", se asume nombre de usuario de Control y se arma el mismo email sintético
 *   que el backend generó al crear la cuenta (`{userName}@control.hoydonde.com`).
 *
 * El recorte de espacios es defensivo (evita un error de formato de email por un espacio
 * accidental al tipear) — el backend no lo aplica porque nunca recibe espacios: el organizador
 * los tipea una sola vez al crear el Control, y ese mismo string es el que hay que reproducir acá
 * tal cual para que ambos lados generen el mismo email.
 */
export function resolveLoginEmail(identifier: string): string {
  const trimmed = identifier.trim();
  if (!trimmed) return trimmed;
  return trimmed.includes('@') ? trimmed : `${trimmed}${CONTROL_EMAIL_DOMAIN}`;
}
