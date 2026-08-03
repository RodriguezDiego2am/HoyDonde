import { controlDisplayName } from './controlDisplayName';

describe('controlDisplayName', () => {
  it('deriva el nombre visible del local-part del email sintético de Control', () => {
    expect(controlDisplayName('control_1@control.hoydonde.com')).toBe('CONTROL 1');
  });

  it('reemplaza separadores por espacios y pasa a mayúsculas', () => {
    expect(controlDisplayName('control-puerta.norte@control.hoydonde.com')).toBe('CONTROL PUERTA NORTE');
  });

  it('nunca incluye el dominio sintético completo en el resultado', () => {
    const result = controlDisplayName('control_puerta_norte@control.hoydonde.com');
    expect(result).not.toContain('@');
    expect(result).not.toContain('control.hoydonde.com');
  });

  it('cae a CONTROL sin email', () => {
    expect(controlDisplayName(null)).toBe('CONTROL');
    expect(controlDisplayName(undefined)).toBe('CONTROL');
  });

  it('usa el local-part igual si el email no es del dominio sintético de Control', () => {
    expect(controlDisplayName('otra@hoydonde.com')).toBe('OTRA');
  });
});
