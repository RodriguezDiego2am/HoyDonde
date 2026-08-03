import React from 'react';
import { act, fireEvent, render, waitFor } from '@testing-library/react-native';

import CarteleraScreen from './index';
import type { EventResponse } from '@/services/APIService';
import { nextLocalDayExclusive, startOfLocalDay, toUtcIso } from '@/utils/datetime';

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

/** Escribe una fecha "DD/MM/AAAA" en un SegmentedDateField pegándola completa en el primer segmento (día), tal cual haría un usuario que pega o tipea rápido — el propio componente la reparte entre día/mes/año. */
function escribirFechaSegmentada(getByTestId: (id: string) => unknown, prefix: string, ddmmaaaa: string) {
  const soloDigitos = ddmmaaaa.replace(/\D/g, '');
  fireEvent.changeText(getByTestId(`${prefix}-day`), soloDigitos);
}

async function abrirFiltros(utils: ReturnType<typeof render>) {
  fireEvent.press(await utils.findByLabelText('Abrir filtros'));
}

describe('CarteleraScreen (catálogo)', () => {
  beforeEach(() => {
    jest.clearAllMocks();
  });

  it('carga la primera página al montar, sin exigir sesión', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });

    const { findByText } = render(<CarteleraScreen />);

    expect(await findByText('Evento e1')).toBeTruthy();
    expect(mockSearch).toHaveBeenCalledWith({
      limit: 10,
      lastEventId: undefined,
      categoria: undefined,
      ubicacion: undefined,
      fechaDesde: undefined,
      fechaHasta: undefined,
    });
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
    expect(mockSearch).toHaveBeenLastCalledWith(
      expect.objectContaining({ limit: 10, lastEventId: 'e2', categoria: undefined })
    );
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

  it('muestra el estado vacío "sin filtros" cuando el backend no devuelve eventos', async () => {
    mockSearch.mockResolvedValueOnce({ data: [], hasNextPage: false, lastDocumentId: undefined });
    const { findByText } = render(<CarteleraScreen />);

    expect(await findByText('No hay eventos publicados por el momento.')).toBeTruthy();
  });

  it('muestra error y permite reintentar cuando falla la carga', async () => {
    mockSearch.mockRejectedValueOnce(new Error('network down'));
    const { findByText } = render(<CarteleraScreen />);

    expect(await findByText('No se pudieron cargar los eventos. Verificá tu conexión.')).toBeTruthy();

    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });
    fireEvent.press(await findByText('Reintentar'));

    expect(await findByText('Evento e1')).toBeTruthy();
  });

  // ---- Panel de filtros ----

  it('abre y cierra el panel de filtros', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });
    const utils = render(<CarteleraScreen />);
    await utils.findByText('Evento e1');

    expect(utils.queryByText('Aplicar filtros')).toBeNull();

    await abrirFiltros(utils);
    expect(await utils.findByText('Aplicar filtros')).toBeTruthy();

    fireEvent.press(utils.getByLabelText('Cerrar filtros'));
    await waitFor(() => expect(utils.queryByText('Aplicar filtros')).toBeNull());
  });

  it('filtra por categoría reiniciando la paginación', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });
    const utils = render(<CarteleraScreen />);
    await utils.findByText('Evento e1');

    mockSearch.mockResolvedValueOnce({ data: [evento('e2')], hasNextPage: false, lastDocumentId: undefined });
    await abrirFiltros(utils);
    fireEvent.press(await utils.findByLabelText('Filtrar por Deportes'));
    fireEvent.press(utils.getByText('Aplicar filtros'));

    await waitFor(() =>
      expect(mockSearch).toHaveBeenLastCalledWith(
        expect.objectContaining({ limit: 10, lastEventId: undefined, categoria: 'Deportes' })
      )
    );
    expect(await utils.findByText('Evento e2')).toBeTruthy();
  });

  it('convierte Desde/Hasta locales a UTC exacto y envía el payload esperado', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });
    const utils = render(<CarteleraScreen />);
    await utils.findByText('Evento e1');

    mockSearch.mockResolvedValueOnce({ data: [evento('e2')], hasNextPage: false, lastDocumentId: undefined });
    await abrirFiltros(utils);
    escribirFechaSegmentada(utils.getByTestId, 'filtro-fecha-desde', '05082026');
    escribirFechaSegmentada(utils.getByTestId, 'filtro-fecha-hasta', '10082026');
    fireEvent.press(utils.getByText('Aplicar filtros'));

    const desde = new Date(2026, 7, 5);
    const hasta = new Date(2026, 7, 10);
    const fechaDesdeEsperada = toUtcIso(startOfLocalDay(desde));
    const fechaHastaEsperada = toUtcIso(nextLocalDayExclusive(hasta));

    await waitFor(() =>
      expect(mockSearch).toHaveBeenLastCalledWith(
        expect.objectContaining({
          categoria: undefined,
          ubicacion: undefined,
          fechaDesde: fechaDesdeEsperada,
          fechaHasta: fechaHastaEsperada,
        })
      )
    );
  });

  it('un rango inválido (Desde posterior a Hasta) no llama a la API y muestra un error', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });
    const utils = render(<CarteleraScreen />);
    await utils.findByText('Evento e1');

    await abrirFiltros(utils);
    escribirFechaSegmentada(utils.getByTestId, 'filtro-fecha-desde', '10082026');
    escribirFechaSegmentada(utils.getByTestId, 'filtro-fecha-hasta', '05082026');
    fireEvent.press(utils.getByText('Aplicar filtros'));

    expect(await utils.findByText('"Desde" no puede ser posterior a "Hasta".')).toBeTruthy();
    // Sigue habiendo un solo llamado: el del montaje inicial.
    expect(mockSearch).toHaveBeenCalledTimes(1);
  });

  it('combina categoría y ubicación en el mismo pedido', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });
    const utils = render(<CarteleraScreen />);
    await utils.findByText('Evento e1');

    mockSearch.mockResolvedValueOnce({ data: [evento('e2')], hasNextPage: false, lastDocumentId: undefined });
    await abrirFiltros(utils);
    fireEvent.press(await utils.findByLabelText('Filtrar por Arte'));
    fireEvent.changeText(utils.getByLabelText('Ubicación'), '  Parque Central  ');
    fireEvent.press(utils.getByText('Aplicar filtros'));

    await waitFor(() =>
      expect(mockSearch).toHaveBeenLastCalledWith(
        expect.objectContaining({ categoria: 'Arte', ubicacion: 'Parque Central' })
      )
    );
  });

  it('aplicar un filtro reinicia el cursor de paginación', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: true, lastDocumentId: 'e1' });
    const utils = render(<CarteleraScreen />);
    await utils.findByText('Evento e1');

    mockSearch.mockResolvedValueOnce({ data: [evento('e2')], hasNextPage: false, lastDocumentId: undefined });
    await abrirFiltros(utils);
    fireEvent.press(await utils.findByLabelText('Filtrar por Deportes'));
    fireEvent.press(utils.getByText('Aplicar filtros'));

    await waitFor(() =>
      expect(mockSearch).toHaveBeenLastCalledWith(expect.objectContaining({ lastEventId: undefined }))
    );
  });

  it('cambiar de filtro reemplaza la lista anterior en vez de mezclarla', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });
    const utils = render(<CarteleraScreen />);
    await utils.findByText('Evento e1');

    mockSearch.mockResolvedValueOnce({ data: [evento('e2')], hasNextPage: false, lastDocumentId: undefined });
    await abrirFiltros(utils);
    fireEvent.press(await utils.findByLabelText('Filtrar por Deportes'));
    fireEvent.press(utils.getByText('Aplicar filtros'));
    await utils.findByText('Evento e2');

    expect(utils.queryByText('Evento e1')).toBeNull();
  });

  it('limpiar filtros restaura la cartelera sin filtros', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });
    const utils = render(<CarteleraScreen />);
    await utils.findByText('Evento e1');

    mockSearch.mockResolvedValueOnce({ data: [evento('e2')], hasNextPage: false, lastDocumentId: undefined });
    await abrirFiltros(utils);
    fireEvent.press(await utils.findByLabelText('Filtrar por Deportes'));
    fireEvent.press(utils.getByText('Aplicar filtros'));
    await utils.findByText('Evento e2');

    mockSearch.mockResolvedValueOnce({ data: [evento('e3')], hasNextPage: false, lastDocumentId: undefined });
    fireEvent.press(utils.getByLabelText('Limpiar filtros'));

    await waitFor(() =>
      expect(mockSearch).toHaveBeenLastCalledWith({
        limit: 10,
        lastEventId: undefined,
        categoria: undefined,
        ubicacion: undefined,
        fechaDesde: undefined,
        fechaHasta: undefined,
      })
    );
    expect(await utils.findByText('Evento e3')).toBeTruthy();
  });

  it('cargar más conserva los filtros aplicados', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });
    const utils = render(<CarteleraScreen />);
    await utils.findByText('Evento e1');

    mockSearch.mockResolvedValueOnce({ data: [evento('e2')], hasNextPage: true, lastDocumentId: 'e2' });
    await abrirFiltros(utils);
    fireEvent.press(await utils.findByLabelText('Filtrar por Deportes'));
    fireEvent.press(utils.getByText('Aplicar filtros'));
    await utils.findByText('Evento e2');

    mockSearch.mockResolvedValueOnce({ data: [evento('e3')], hasNextPage: false, lastDocumentId: 'e3' });
    const list = utils.getByTestId('cartelera-list');
    await act(async () => {
      fireEvent(list, 'onEndReached');
    });

    await waitFor(() =>
      expect(mockSearch).toHaveBeenLastCalledWith(
        expect.objectContaining({ categoria: 'Deportes', lastEventId: 'e2' })
      )
    );
  });

  it('refrescar (pull-to-refresh / botón de recarga) conserva los filtros aplicados', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });
    const utils = render(<CarteleraScreen />);
    await utils.findByText('Evento e1');

    mockSearch.mockResolvedValueOnce({ data: [evento('e2')], hasNextPage: false, lastDocumentId: undefined });
    await abrirFiltros(utils);
    fireEvent.press(await utils.findByLabelText('Filtrar por Deportes'));
    fireEvent.press(utils.getByText('Aplicar filtros'));
    await utils.findByText('Evento e2');

    mockSearch.mockResolvedValueOnce({ data: [evento('e2')], hasNextPage: false, lastDocumentId: undefined });
    fireEvent.press(utils.getByLabelText('Actualizar cartelera'));

    await waitFor(() =>
      expect(mockSearch).toHaveBeenLastCalledWith(expect.objectContaining({ categoria: 'Deportes' }))
    );
  });

  it('distingue el estado vacío "sin filtros" del estado vacío "con filtros"', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });
    const utils = render(<CarteleraScreen />);
    await utils.findByText('Evento e1');

    mockSearch.mockResolvedValueOnce({ data: [], hasNextPage: false, lastDocumentId: undefined });
    await abrirFiltros(utils);
    fireEvent.press(await utils.findByLabelText('Filtrar por Deportes'));
    fireEvent.press(utils.getByText('Aplicar filtros'));

    expect(await utils.findByText('No encontramos eventos con estos filtros.')).toBeTruthy();
  });

  it('funciona en uso anónimo, sin ningún dato de sesión', async () => {
    mockSearch.mockResolvedValueOnce({ data: [evento('e1')], hasNextPage: false, lastDocumentId: undefined });
    const { findByText } = render(<CarteleraScreen />);

    expect(await findByText('Evento e1')).toBeTruthy();
    // eventService.search nunca recibe token/headers acá: el mock no depende de sesión alguna.
  });
});
