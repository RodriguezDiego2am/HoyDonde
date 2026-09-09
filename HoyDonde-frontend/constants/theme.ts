/**
 * Tokens de la identidad visual "Cartelera urbana": una guía de ciudad
 * editorial, con bloques de tinta, papel cálido y acentos de afiche.
 */
export const colors = {
  paper: '#F3EBDD',
  paperBright: '#FFF9EE',
  ink: '#171512',
  inkSoft: '#6B6357',
  sand: '#D8CDBB',
  tomato: '#F04E3E',
  cobalt: '#3454D1',
  lime: '#C8F25B',
  success: '#167A50',
  error: '#C83737',
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
  sm: 8,
  md: 14,
  lg: 22,
  xl: 30,
  pill: 999,
} as const;

export const borderWidth = {
  thin: 1,
  thick: 2,
} as const;

/** Elevación corta y opaca: más afiche pegado que tarjeta SaaS flotante. */
export const posterShadow = {
  shadowColor: colors.ink,
  shadowOffset: { width: 4, height: 5 },
  shadowOpacity: 1,
  shadowRadius: 0,
  elevation: 5,
} as const;

export const fonts = {
  regular: 'Archivo_400Regular',
  medium: 'Archivo_500Medium',
  semiBold: 'Archivo_600SemiBold',
  bold: 'Archivo_700Bold',
  black: 'Archivo_900Black',
} as const;
