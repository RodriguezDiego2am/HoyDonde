function extractCode(error: unknown): string {
  if (typeof error === 'object' && error !== null && 'code' in error) {
    return String((error as { code: unknown }).code);
  }
  return '';
}

/** Traduce códigos de error de Firebase Auth a mensajes en español para la UI. */
export function describeFirebaseAuthError(error: unknown): string {
  switch (extractCode(error)) {
    case 'auth/invalid-email':
      return 'El email ingresado no es válido.';
    case 'auth/user-disabled':
      return 'Esta cuenta fue deshabilitada.';
    case 'auth/user-not-found':
    case 'auth/wrong-password':
    case 'auth/invalid-credential':
      return 'Email o contraseña incorrectos.';
    case 'auth/email-already-in-use':
      return 'Ya existe una cuenta con este email.';
    case 'auth/weak-password':
      return 'La contraseña debe tener al menos 6 caracteres.';
    case 'auth/network-request-failed':
      return 'No se pudo conectar. Revisá tu conexión a internet.';
    case 'auth/too-many-requests':
      return 'Demasiados intentos. Probá de nuevo en unos minutos.';
    default:
      return 'Ocurrió un error inesperado. Intentá nuevamente.';
  }
}
