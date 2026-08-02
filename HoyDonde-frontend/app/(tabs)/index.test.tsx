import React from 'react';
import { act, fireEvent, render, waitFor } from '@testing-library/react-native';

import CarteleraScreen from './index';
import type { EventResponse } from '@/services/APIService';

const mockSearch = jest.fn();
jest.mock('@/services/APIService', () => ({
  eventService: { search: (...args: unknown[]) => mockSearch(...args) },
}));

const mockPush = jest.fn();
jest.mock('expo-router', () => ({
  router: { push: (...args: unknown[]) => mockPush(...args) },
}));

function evento(id: string, overrides: Partial<EventResponse> = {}): EventResponse {
  return {
    id,
    nombre: `Evento ${id}`,
    descripcion: '',
    fechaInicio: '2026-12-01T22:00:00Z',
    fechaFin: '2026-12-02T04:00:00Z',
    ubicacion: 'Parque Central',
    categoria: 'Musica',
    estado: 'Publicado',
    ticketGroups: [],
    ...overrides,
  };
}

describe('CarteleraScreen (catálogo)', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('carga la primera página al montar, sin exigir sesión', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });

    const { findByText } = render(<CarteleraScreen />);

    expect(await findByText('Evento e1')).toBeTruthy();
    expect(mockSearch).toHaveBeenCalledWith({ limit: 10, lastEventId: undefined, categoria: undefined });
  });

  it('pagina con el cursor real del backend y agrega, sin duplicar, los eventos ya cargados', async () => {
    mockSearch.mockResolvedValueOnce({
      data: [evento('e1'), evento('e2')],
      hasNextPage: true,
      lastDocumentId: 'e2',
    });
    const { findByText, getByTestId, queryAllByText } = render(<CarteleraScreen />);
    await findByText('Evento e1');

    mockSearch.mockResolvedValueOnce({
      // e2 se repite (ya visto) y e3 es nuevo: no debe duplicarse e2.
      data: [evento('e2'), evento('e3')],
      hasNextPage: false,
      lastDocumentId: 'e3',
    });

    const list = getByTestId('cartelera-list');
    await act(async () => {
      fireEvent(list, 'onEndReached');
    });

    await waitFor(() => expect(mockSearch).toHaveBeenCalledTimes(2));
    expect(mockSearch).toHaveBeenLastCalledWith({ limit: 10, lastEventId: 'e2', categoria: undefined });
    expect(queryAllByText('Evento e2')).toHaveLength(1);
    expect(await findByText('Evento e3')).toBeTruthy();
  });

  it('ignora un segundo onEndReached mientras la primera página "más" sigue en vuelo', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: true, lastDocumentId: 'e1' });
    const { findByText, getByTestId } = render(<CarteleraScreen />);
    await findByText('Evento e1');

    let resolveSecond!: (value: unknown) => void;
    mockSearch.mockReturnValueOnce(
      new Promise((resolve) => {
        resolveSecond = resolve;
      })
    );

    const list = getByTestId('cartelera-list');
    fireEvent(list, 'onEndReached');
    fireEvent(list, 'onEndReached');
    fireEvent(list, 'onEndReached');

    await act(async () => {
      resolveSecond({ data: [evento('e2')], hasNextPage: false, lastDocumentId: 'e2' });
    });

    expect(mockSearch).toHaveBeenCalledTimes(2);
  });

  it('navega al detalle público del evento al tocar una tarjeta', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });
    const { findByLabelText } = render(<CarteleraScreen />);

    const card = await findByLabelText('Ver detalle de Evento e1');
    fireEvent.press(card);

    expect(mockPush).toHaveBeenCalledWith({ pathname: '/events/[id]', params: { id: 'e1' } });
  });

  it('muestra el estado vacío cuando el backend no devuelve eventos', async () => {
    mockSearch.mockResolvedValueOnce({ data: [], hasNextPage: false, lastDocumentId: undefined });
    const { findByText } = render(<CarteleraScreen />);

    expect(await findByText('No hay eventos disponibles por el momento.')).toBeTruthy();
  });

  it('muestra error y permite reintentar cuando falla la carga', async () => {
    mockSearch.mockRejectedValueOnce(new Error('network down'));
    const { findByText } = render(<CarteleraScreen />);

    expect(await findByText('No se pudieron cargar los eventos. Verificá tu conexión.')).toBeTruthy();

    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });
    fireEvent.press(await findByText('Reintentar'));

    expect(await findByText('Evento e1')).toBeTruthy();
  });

  it('filtra por categoría reiniciando la paginación', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });
    const { findByText, findByLabelText } = render(<CarteleraScreen />);
    await findByText('Evento e1');

    mockSearch.mockResolvedValueOnce({ data: [evento('e2')], hasNextPage: false, lastDocumentId: undefined });
    fireEvent.press(await findByLabelText('Filtrar por Deportes'));

    await waitFor(() =>
      expect(mockSearch).toHaveBeenLastCalledWith({ limit: 10, lastEventId: undefined, categoria: 'Deportes' })
    );
    expect(await findByText('Evento e2')).toBeTruthy();
  });
});
