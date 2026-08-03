import { CONTROL_EMAIL_DOMAIN, resolveLoginEmail } from './controlLoginEmail';

describe('resolveLoginEmail', () => {
  it('deja un email normal tal cual', () => {
    expect(resolveLoginEmail('cliente@hoydonde.com')).toBe('cliente@hoydonde.com');
  });

  it('arma el email sintético de Control a partir de un nombre de usuario sin "@"', () => {
    expect(resolveLoginEmail('control_puerta_norte')).toBe('control_puerta_norte@control.hoydonde.com');
    expect(resolveLoginEmail('control_puerta_norte')).toBe(`control_puerta_norte${CONTROL_EMAIL_DOMAIN}`);
  });

  it('recorta espacios en ambos casos (email y usuario)', () => {
    expect(resolveLoginEmail('  cliente@hoydonde.com  ')).toBe('cliente@hoydonde.com');
    expect(resolveLoginEmail('  control_puerta_norte  ')).toBe('control_puerta_norte@control.hoydonde.com');
  });

  it('no altera mayúsculas/minúsculas: el backend tampoco lo hace al crear la cuenta', () => {
    expect(resolveLoginEmail('Control_Puerta_Norte')).toBe('Control_Puerta_Norte@control.hoydonde.com');
    expect(resolveLoginEmail('Cliente@HoyDonde.com')).toBe('Cliente@HoyDonde.com');
  });

  it('un string vacío (o solo espacios) queda vacío: la validación de campo obligatorio es responsabilidad del formulario', () => {
    expect(resolveLoginEmail('')).toBe('');
    expect(resolveLoginEmail('   ')).toBe('');
  });

  it('un identificador con espacios internos no se altera más allá del recorte de bordes', () => {
    expect(resolveLoginEmail('nombre con espacios')).toBe('nombre con espacios@control.hoydonde.com');
  });
});
