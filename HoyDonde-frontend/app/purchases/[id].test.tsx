import React from 'react';
import { render, waitFor } from '@testing-library/react-native';

jest.mock('../../config/apiEnv', () => ({ resolveApiUrl: () => 'http://localhost:5053/api' }));
jest.mock('../../config/firebase', () => ({ auth: { currentUser: null } }));
jest.mock('firebase/auth', () => ({ signOut: jest.fn().mockResolvedValue(undefined) }));
jest.mock('expo-router', () => ({ router: { replace: jest.fn(), push: jest.fn(), back: jest.fn() }, useLocalSearchParams: () => ({ id: 'purchase-1' }) }));

// eslint-disable-next-line import/first -- mocked dependencies must load before the API client.
import { apiClient } from '@/services/APIService';
// eslint-disable-next-line import/first -- mocked router must load before the route.
import PurchaseStatusScreen from './[id]';

describe('PurchaseStatusScreen', () => {
  it('shows a pending return while refreshing only the server-owned purchase state', async () => {
    const getSpy = jest.spyOn(apiClient, 'get').mockResolvedValue({ data: { id: 'purchase-1', estadoCompra: 'PendingPayment', estadoReserva: 'Active', checkoutState: 'Ready', checkoutUrl: null, refundStatus: null } } as any);
    const { findByText, queryByText } = render(<PurchaseStatusScreen />);
    expect(await findByText('Estamos confirmando tu pago')).toBeTruthy();
    expect(queryByText('Ver QR')).toBeNull();
    expect(queryByText('Descargar comprobante PDF')).toBeNull();
    await waitFor(() => expect(getSpy).toHaveBeenCalledWith('/purchases/purchase-1'));
  });
});
