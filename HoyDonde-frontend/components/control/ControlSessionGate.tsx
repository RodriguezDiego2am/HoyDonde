import { useEffect } from 'react';
import { router, useSegments } from 'expo-router';

import { useAuth } from '@/context/AuthContext';
import { isControlExclusivo } from '@/utils/controlExperience';

/**
 * Única función/lugar que decide la redirección post-login/restauración de sesión hacia Control
 * (Frontend 3, punto 3): se apoya exclusivamente en el `user`/`initializing` ya resuelto por
 * AuthContext (nunca en Firebase directamente), así que no reintroduce la carrera de sesión que
 * ya se corrigió ahí (ver el comentario de activeUidRef en context/AuthContext.tsx). Un usuario
 * multirol nunca es redirigido acá: isControlExclusivo solo es true cuando TICKET_VALIDAR es la
 * única acción efectiva.
 *
 * El efecto solo empuja hacia /control; nunca aleja de ahí. Un usuario que deja de ser Control
 * exclusivo (o cierra sesión) simplemente deja de cumplir la condición y no vuelve a dispararse.
 */
export function ControlSessionGate() {
  const { user, initializing, syncError } = useAuth();
  const segments = useSegments();

  useEffect(() => {
    if (initializing || syncError || !user) return;
    if (!isControlExclusivo(user.acciones)) return;
    if (segments[0] === 'control') return;

    router.replace('/control');
  }, [user, initializing, syncError, segments]);

  return null;
}
