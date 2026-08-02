/* eslint-disable import/first -- jest.mock debe preceder a los imports que lo usan transitivamente */

// LoginScreen/RegisterScreen importan AuthContext, que a su vez inicializa Firebase
// a nivel de módulo; se mockea para no requerir variables de entorno reales en tests.
jest.mock('../config/firebase', () => ({ auth: {} }));
jest.mock('firebase/auth', () => ({
  onAuthStateChanged: () => () => {},
  signInWithEmailAndPassword: () => Promise.resolve(),
  createUserWithEmailAndPassword: () => Promise.resolve(),
  signOut: () => Promise.resolve(),
}));

import LoginRoute from './login';
import RegisterRoute from './register';
import LoginScreen from '../screens/LoginScreen';
import RegisterScreen from '../screens/RegisterScreen';

describe('app/login and app/register route files', () => {
  it('wires /login to the real LoginScreen', () => {
    expect(LoginRoute).toBe(LoginScreen);
  });

  it('wires /register to the real RegisterScreen', () => {
    expect(RegisterRoute).toBe(RegisterScreen);
  });
});
