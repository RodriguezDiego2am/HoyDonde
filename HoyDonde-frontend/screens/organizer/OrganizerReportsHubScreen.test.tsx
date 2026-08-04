import React from 'react';
import { fireEvent, render } from '@testing-library/react-native';

const mockPush = jest.fn();
jest.mock('expo-router', () => ({
  router: { push: (...args: unknown[]) => mockPush(...args), back: jest.fn() },
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import OrganizerReportsHubScreen from './OrganizerReportsHubScreen';

describe('OrganizerReportsHubScreen', () => {
  beforeEach(() => {
    mockPush.mockClear();
  });

  it('navega a /organizer/reports/events al tocar "Desempeño de eventos"', () => {
    const { getByText } = render(<OrganizerReportsHubScreen />);

    fireEvent.press(getByText('Desempeño de eventos'));

    expect(mockPush).toHaveBeenCalledWith('/organizer/reports/events');
  });

  it('navega a /organizer/reports/sales al tocar "Ventas simuladas"', () => {
    const { getByText } = render(<OrganizerReportsHubScreen />);

    fireEvent.press(getByText('Ventas simuladas'));

    expect(mockPush).toHaveBeenCalledWith('/organizer/reports/sales');
  });
});
