import React from 'react';
import { fireEvent, render } from '@testing-library/react-native';

const mockPush = jest.fn();
jest.mock('expo-router', () => ({
  router: { push: (...args: unknown[]) => mockPush(...args), back: jest.fn() },
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import AdminReportsHubScreen from './AdminReportsHubScreen';

describe('AdminReportsHubScreen', () => {
  beforeEach(() => {
    mockPush.mockClear();
  });

  it('navega a /admin/reports/events al tocar "Eventos (global)"', () => {
    const { getByText } = render(<AdminReportsHubScreen />);

    fireEvent.press(getByText('Eventos (global)'));

    expect(mockPush).toHaveBeenCalledWith('/admin/reports/events');
  });

  it('navega a /admin/reports/sales al tocar "Ventas simuladas"', () => {
    const { getByText } = render(<AdminReportsHubScreen />);

    fireEvent.press(getByText('Ventas simuladas'));

    expect(mockPush).toHaveBeenCalledWith('/admin/reports/sales');
  });

  it('navega a /admin/reports/security-audits al tocar "Auditoría de seguridad"', () => {
    const { getByText } = render(<AdminReportsHubScreen />);

    fireEvent.press(getByText('Auditoría de seguridad'));

    expect(mockPush).toHaveBeenCalledWith('/admin/reports/security-audits');
  });
});
