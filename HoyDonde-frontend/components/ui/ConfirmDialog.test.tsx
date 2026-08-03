import React from 'react';
import { fireEvent, render } from '@testing-library/react-native';

import { ConfirmDialog } from './ConfirmDialog';

describe('ConfirmDialog', () => {
  it('no renderiza contenido cuando visible es false', () => {
    const { queryByText } = render(
      <ConfirmDialog
        visible={false}
        title="Publicar evento"
        message="¿Publicar este evento?"
        confirmLabel="Publicar"
        onConfirm={jest.fn()}
        onCancel={jest.fn()}
      />
    );

    expect(queryByText('Publicar evento')).toBeNull();
  });

  it('llama a onConfirm y onCancel con los botones correctos', () => {
    const onConfirm = jest.fn();
    const onCancel = jest.fn();
    const { getByText } = render(
      <ConfirmDialog
        visible
        title="Cancelar evento"
        message="Esta acción no se puede deshacer."
        confirmLabel="Sí, cancelar"
        cancelLabel="Volver"
        variant="danger"
        onConfirm={onConfirm}
        onCancel={onCancel}
      />
    );

    fireEvent.press(getByText('Sí, cancelar'));
    expect(onConfirm).toHaveBeenCalledTimes(1);

    fireEvent.press(getByText('Volver'));
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('deshabilita el botón de cancelar mientras loading es true, para evitar cerrar en medio de la operación', () => {
    const onCancel = jest.fn();
    const { getByText, queryByText } = render(
      <ConfirmDialog
        visible
        title="Publicar evento"
        message="¿Publicar este evento?"
        confirmLabel="Publicar"
        loading
        onConfirm={jest.fn()}
        onCancel={onCancel}
      />
    );

    // El botón "Publicar" muestra un spinner en vez de su texto mientras loading es true.
    expect(queryByText('Publicar')).toBeNull();

    fireEvent.press(getByText('Cancelar'));
    expect(onCancel).not.toHaveBeenCalled();
  });
});
