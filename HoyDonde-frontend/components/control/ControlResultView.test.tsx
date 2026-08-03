import React from 'react';
import { fireEvent, render } from '@testing-library/react-native';

import { ControlResultView } from './ControlResultView';
import type { TicketValidationOutcomeKind } from '@/services/ticketValidationService';

const CASES: { kind: TicketValidationOutcomeKind; message: string; headline: string }[] = [
  { kind: 'valid', message: 'Ticket validado para este evento.', headline: 'ENTRADA VÁLIDA' },
  { kind: 'alreadyUsed', message: 'El ticket ya fue utilizado.', headline: 'ENTRADA YA UTILIZADA' },
  { kind: 'anulado', message: 'El ticket fue anulado.', headline: 'ENTRADA ANULADA' },
  { kind: 'eventoCancelado', message: 'El evento fue cancelado.', headline: 'EVENTO CANCELADO' },
  { kind: 'eventoFinalizado', message: 'El evento ya finalizó.', headline: 'EVENTO FINALIZADO' },
  {
    kind: 'notAuthorized',
    message: 'No autorizado para validar tickets de este evento.',
    headline: 'CONTROL NO AUTORIZADO',
  },
  { kind: 'notFound', message: 'Ticket no encontrado.', headline: 'ENTRADA NO ENCONTRADA' },
  { kind: 'network', message: 'No se pudo conectar con el servidor. Verificá tu conexión.', headline: 'ERROR DE CONEXIÓN' },
  { kind: 'unexpected', message: 'Ocurrió un error inesperado.', headline: 'ERROR INESPERADO' },
];

describe('ControlResultView', () => {
  it.each(CASES)('muestra un headline y un mensaje de texto distintivos para $kind', ({ kind, message, headline }) => {
    const { getByText } = render(<ControlResultView result={{ kind, message }} onScanNext={() => {}} />);

    expect(getByText(headline)).toBeTruthy();
    expect(getByText(message)).toBeTruthy();
  });

  it('cada kind tiene un headline distinto de los demás (nunca solo color)', () => {
    const headlines = new Set(CASES.map((c) => c.headline));
    expect(headlines.size).toBe(CASES.length);
  });

  it('muestra el traceId cuando está disponible', () => {
    const { getByText } = render(
      <ControlResultView result={{ kind: 'unexpected', message: 'Error', traceId: 'trace-xyz' }} onScanNext={() => {}} />
    );
    expect(getByText(/trace-xyz/)).toBeTruthy();
  });

  it('no muestra ninguna referencia a traceId cuando no está disponible', () => {
    const { queryByText } = render(<ControlResultView result={{ kind: 'valid', message: 'ok' }} onScanNext={() => {}} />);
    expect(queryByText(/código de referencia/i)).toBeNull();
  });

  it('"Escanear siguiente" invoca onScanNext', () => {
    const onScanNext = jest.fn();
    const { getByText } = render(<ControlResultView result={{ kind: 'valid', message: 'ok' }} onScanNext={onScanNext} />);
    fireEvent.press(getByText('Escanear siguiente'));
    expect(onScanNext).toHaveBeenCalledTimes(1);
  });

  it('acepta una etiqueta de acción alternativa (ingreso manual)', () => {
    const { getByText } = render(
      <ControlResultView result={{ kind: 'valid', message: 'ok' }} onScanNext={() => {}} nextActionLabel="Validar otra entrada" />
    );
    expect(getByText('Validar otra entrada')).toBeTruthy();
  });
});
