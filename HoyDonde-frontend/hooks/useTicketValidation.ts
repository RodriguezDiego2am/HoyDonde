import { useCallback, useRef, useState } from 'react';
import * as Haptics from 'expo-haptics';

import { TicketValidationResult, validateTicket } from '@/services/ticketValidationService';

export interface UseTicketValidation {
  result: TicketValidationResult | null;
  validating: boolean;
  /** Escáner e ingreso manual comparten esta misma función: mismo servicio, mismo lock. */
  validate: (ticketId: string, eventId: string) => Promise<void>;
  /** "Escanear siguiente" / "Validar otra entrada": limpia resultado y lock a la vez. */
  reset: () => void;
}

/**
 * Lock anti-repetición compartido por el escáner QR y el ingreso manual: una vez que arranca una
 * validación, cualquier llamada adicional (varios frames del mismo QR, doble tap) se ignora hasta
 * que reset() la limpia explícitamente — nunca hay una segunda llamada a la API para el mismo
 * intento, ni siquiera si `validate` se invoca de nuevo mientras la primera sigue en vuelo.
 */
export function useTicketValidation(): UseTicketValidation {
  const [result, setResult] = useState<TicketValidationResult | null>(null);
  const [validating, setValidating] = useState(false);
  const lockRef = useRef(false);

  const validate = useCallback(async (ticketId: string, eventId: string) => {
    if (lockRef.current) return;
    lockRef.current = true;
    setValidating(true);

    const outcome = await validateTicket({ ticketId, eventId });

    setResult(outcome);
    setValidating(false);

    // Haptics es un refuerzo, nunca la única señal: el resultado ya se muestra con ícono, color y texto.
    Haptics.notificationAsync(
      outcome.kind === 'valid' ? Haptics.NotificationFeedbackType.Success : Haptics.NotificationFeedbackType.Error
    ).catch(() => {});
  }, []);

  const reset = useCallback(() => {
    lockRef.current = false;
    setResult(null);
  }, []);

  return { result, validating, validate, reset };
}
