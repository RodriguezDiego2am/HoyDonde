const mockGenerateAndShare = jest.fn();

jest.mock('expo-print', () => ({ printToFileAsync: jest.fn() }));
jest.mock('expo-sharing', () => ({ isAvailableAsync: jest.fn(), shareAsync: jest.fn() }));
jest.mock('expo-file-system', () => ({
  Paths: { cache: 'CACHE_DIR' },
  File: class {
    uri = 'mock-file://x';
    exists = false;
    delete() {}
    copy() {}
  },
}));
jest.mock('@/utils/purchaseReceiptPdf', () => {
  const actual = jest.requireActual('@/utils/purchaseReceiptPdf');
  return {
    ...actual,
    generateAndSharePurchaseReceiptPdf: (...args: unknown[]) => mockGenerateAndShare(...args),
  };
});

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import React from 'react';
// eslint-disable-next-line import/first
import { act, fireEvent, render, waitFor } from '@testing-library/react-native';
// eslint-disable-next-line import/first
import { PurchaseReceiptPanel } from './PurchaseReceiptPanel';
// eslint-disable-next-line import/first
import type { CompraResponse, TicketResponse } from '@/services/APIService';

function ticket(overrides: Partial<TicketResponse> = {}): TicketResponse {
  return {
    id: 'ticket-1',
    compraId: 'compra-1',
    eventoId: 'evento-1',
    ticketTypeId: 'tipo-general',
    clientePersonaId: 'persona-1',
    fechaCompra: '2026-08-02T12:00:00Z',
    estado: 'Emitido',
    utilizable: true,
    motivoNoUtilizable: null,
    eventoNombre: 'Festival de Verano',
    ticketTypeNombre: 'General',
    precioPagado: 5000,
    fechaInicio: '2026-12-01T22:00:00Z',
    fechaFin: '2026-12-02T04:00:00Z',
    ...overrides,
  };
}

/** Construye una CompraResponse consistente por default (cantidadEntradas/importeTotal calculados desde los tickets pasados). */
function compra(overrides: Partial<CompraResponse> = {}): CompraResponse {
  const tickets = overrides.tickets ?? [ticket()];
  return {
    id: 'compra-1',
    eventoId: 'evento-1',
    eventoNombre: 'Festival de Verano',
    ubicacion: 'Parque Central',
    fechaInicio: '2026-12-01T22:00:00Z',
    fechaFin: '2026-12-02T04:00:00Z',
    fechaCompra: '2026-08-02T12:00:00Z',
    cantidadEntradas: tickets.length,
    importeTotal: tickets.reduce((sum, t) => sum + t.precioPagado, 0),
    pagoSimulado: true,
    ...overrides,
    tickets,
  };
}

describe('PurchaseReceiptPanel', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('muestra el resumen de la compra: evento, fecha de compra, tipos, cantidades y total (desde Compra)', async () => {
    const tickets = [
      ticket({ id: 't1', ticketTypeId: 'general', ticketTypeNombre: 'General', precioPagado: 5000 }),
      ticket({ id: 't2', ticketTypeId: 'general', ticketTypeNombre: 'General', precioPagado: 5000 }),
      ticket({ id: 't3', ticketTypeId: 'vip', ticketTypeNombre: 'VIP', precioPagado: 10000 }),
    ];

    const { getByText } = render(<PurchaseReceiptPanel compra={compra({ tickets })} onGoToTickets={() => {}} />);

    expect(getByText('Festival de Verano')).toBeTruthy();
    expect(getByText('Parque Central')).toBeTruthy();
    expect(getByText('2 × General')).toBeTruthy();
    expect(getByText('1 × VIP')).toBeTruthy();
    expect(getByText('Total: 3 entradas')).toBeTruthy();
    expect(getByText('Descargar comprobante PDF')).toBeTruthy();
  });

  it('entrada gratuita muestra GRATIS en el resumen', () => {
    const { getAllByText } = render(
      <PurchaseReceiptPanel
        compra={compra({ tickets: [ticket({ precioPagado: 0 })], importeTotal: 0 })}
        onGoToTickets={() => {}}
      />
    );

    expect(getAllByText(/GRATIS/).length).toBeGreaterThan(0);
  });

  it('bloquea la descarga y muestra un aviso seguro cuando Compra es inconsistente con sus Ticket, sin afectar la compra', () => {
    const inconsistente = compra({ importeTotal: 999999 });
    const onGoToTickets = jest.fn();

    const { getByText, queryByText } = render(
      <PurchaseReceiptPanel compra={inconsistente} onGoToTickets={onGoToTickets} />
    );

    expect(getByText(/no son consistentes/)).toBeTruthy();
    expect(queryByText('Descargar comprobante PDF')).toBeNull();
    expect(mockGenerateAndShare).not.toHaveBeenCalled();

    // La compra sigue siendo válida: "Ver mis entradas" sigue disponible y funcional.
    fireEvent.press(getByText('Ver mis entradas'));
    expect(onGoToTickets).toHaveBeenCalledTimes(1);
  });

  it('al descargar, genera el PDF con un nombre de archivo derivado de la fecha de compra', async () => {
    mockGenerateAndShare.mockResolvedValue({ uri: 'file://x.pdf', shared: true });

    const { getByText } = render(<PurchaseReceiptPanel compra={compra()} onGoToTickets={() => {}} />);

    await act(async () => {
      fireEvent.press(getByText('Descargar comprobante PDF'));
    });

    await waitFor(() => expect(mockGenerateAndShare).toHaveBeenCalledTimes(1));
    const [html, fileName] = mockGenerateAndShare.mock.calls[0];
    expect(fileName).toBe('HoyDonde-Comprobante-2026-08-02.pdf');
    expect(html).toContain('Festival de Verano');
    expect(html).toContain('N.º DE OPERACIÓN: compra-1');
  });

  it('doble toque mientras genera produce un único PDF', async () => {
    let resolveGenerate!: (value: unknown) => void;
    mockGenerateAndShare.mockReturnValue(
      new Promise((resolve) => {
        resolveGenerate = resolve;
      })
    );

    const { getByText } = render(<PurchaseReceiptPanel compra={compra()} onGoToTickets={() => {}} />);

    const button = getByText('Descargar comprobante PDF');
    fireEvent.press(button);
    fireEvent.press(button);
    fireEvent.press(button);

    expect(mockGenerateAndShare).toHaveBeenCalledTimes(1);

    await act(async () => {
      resolveGenerate({ uri: 'file://x.pdf', shared: true });
    });
  });

  it('cuando sharing no está disponible, muestra un aviso informativo (no un error)', async () => {
    mockGenerateAndShare.mockResolvedValue({ uri: 'file://x.pdf', shared: false });

    const { getByText, findByText } = render(<PurchaseReceiptPanel compra={compra()} onGoToTickets={() => {}} />);

    await act(async () => {
      fireEvent.press(getByText('Descargar comprobante PDF'));
    });

    expect(await findByText(/no pudo abrir el selector para compartir/)).toBeTruthy();
  });

  it('un error al generar permite reintentar, y un reintento exitoso limpia el error', async () => {
    mockGenerateAndShare.mockRejectedValueOnce(new Error('boom'));
    mockGenerateAndShare.mockResolvedValueOnce({ uri: 'file://x.pdf', shared: true });

    const { getByText, findByText, queryByText } = render(
      <PurchaseReceiptPanel compra={compra()} onGoToTickets={() => {}} />
    );

    await act(async () => {
      fireEvent.press(getByText('Descargar comprobante PDF'));
    });
    expect(await findByText('No se pudo generar el comprobante. Volvé a intentarlo.')).toBeTruthy();

    await act(async () => {
      fireEvent.press(getByText('Reintentar descarga'));
    });

    await waitFor(() => expect(mockGenerateAndShare).toHaveBeenCalledTimes(2));
    expect(queryByText('No se pudo generar el comprobante. Volvé a intentarlo.')).toBeNull();
  });

  it('nunca expone el mensaje interno del error (ni stack ni payload)', async () => {
    mockGenerateAndShare.mockRejectedValue(new Error('Detalle interno sensible xyz'));

    const { getByText, findByText, queryByText } = render(
      <PurchaseReceiptPanel compra={compra()} onGoToTickets={() => {}} />
    );

    await act(async () => {
      fireEvent.press(getByText('Descargar comprobante PDF'));
    });

    expect(await findByText('No se pudo generar el comprobante. Volvé a intentarlo.')).toBeTruthy();
    expect(queryByText(/Detalle interno sensible/)).toBeNull();
  });

  it('nunca muestra clientePersonaId ni otros identificadores sensibles en el resumen', () => {
    const { queryByText } = render(
      <PurchaseReceiptPanel
        compra={compra({ tickets: [ticket({ clientePersonaId: 'persona-secreta-999' })] })}
        onGoToTickets={() => {}}
      />
    );

    expect(queryByText(/persona-secreta-999/)).toBeNull();
  });

  it('"Ver mis entradas" sigue funcionando y no depende de haber descargado el comprobante', () => {
    const onGoToTickets = jest.fn();
    const { getByText } = render(<PurchaseReceiptPanel compra={compra()} onGoToTickets={onGoToTickets} />);

    fireEvent.press(getByText('Ver mis entradas'));

    expect(onGoToTickets).toHaveBeenCalledTimes(1);
    expect(mockGenerateAndShare).not.toHaveBeenCalled();
  });
});
