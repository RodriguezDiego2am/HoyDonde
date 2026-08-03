import { isControlExclusivo } from './controlExperience';

describe('isControlExclusivo', () => {
  it('es true cuando TICKET_VALIDAR es la única acción efectiva', () => {
    expect(isControlExclusivo(['TICKET_VALIDAR'])).toBe(true);
  });

  it('es false para un usuario multirol que además tiene otras acciones', () => {
    expect(isControlExclusivo(['TICKET_VALIDAR', 'EVENTO_VER_PROPIOS'])).toBe(false);
    expect(isControlExclusivo(['EVENTO_CREAR', 'TICKET_VALIDAR'])).toBe(false);
  });

  it('es false sin ninguna acción (nunca por defecto true)', () => {
    expect(isControlExclusivo([])).toBe(false);
  });

  it('es false para un Cliente u Organizador sin TICKET_VALIDAR', () => {
    expect(isControlExclusivo(['TICKET_VER_PROPIO'])).toBe(false);
    expect(isControlExclusivo(['EVENTO_CREAR', 'EVENTO_VER_PROPIOS'])).toBe(false);
  });

  it('nunca decide por nombre de rol: solo mira el arreglo de acciones', () => {
    // Un Administrador que por error tuviera solo TICKET_VALIDAR asignado igual cuenta como exclusivo:
    // la función no sabe ni le importa qué rol lo originó.
    expect(isControlExclusivo(['TICKET_VALIDAR'])).toBe(true);
  });
});
