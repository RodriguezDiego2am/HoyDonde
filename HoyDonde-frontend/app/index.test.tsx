import React from 'react';
import { render } from '@testing-library/react-native';
import Index from './index';

const mockReplace = jest.fn();
jest.mock('expo-router', () => ({
  router: { replace: (...args: unknown[]) => mockReplace(...args) },
}));

describe('app/index', () => {
  it('redirects to the public catalog without checking authentication', () => {
    render(<Index />);
    expect(mockReplace).toHaveBeenCalledWith('/(tabs)');
  });
});
