/**
 * Tokens de la identidad visual "Cartelera urbana" (docs/api-mvp-plan.md §6):
 * afiche cultural impreso, no dashboard genérico. Reutilizable por las
 * etapas Frontend 1-4.
 */

export const colors = {
  paper: '#F3EBDD',
  ink: '#171512',
  tomato: '#F04E3E',
  cobalt: '#3454D1',
  lime: '#C8F25B',
  sand: '#D8CDBB',
  success: '#167A50',
  error: '#C83737',
  /** Tinta atenuada para texto secundario: reemplaza el uso de opacity, mejor contraste sobre paper/sand. */
  inkSoft: '#6B6357',
} as const;

export const spacing = {
  xs: 4,
  sm: 8,
  md: 16,
  lg: 24,
  xl: 32,
  xxl: 48,
} as const;

export const radii = {
  sm: 2,
  md: 4,
  lg: 8,
} as const;

export const borderWidth = {
  thin: 1,
  thick: 2,
} as const;

export const fonts = {
  regular: 'Archivo_400Regular',
  medium: 'Archivo_500Medium',
  semiBold: 'Archivo_600SemiBold',
  bold: 'Archivo_700Bold',
  black: 'Archivo_900Black',
} as const;
