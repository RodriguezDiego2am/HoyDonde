const mockPrintToFileAsync = jest.fn();
const mockIsAvailableAsync = jest.fn();
const mockShareAsync = jest.fn();

jest.mock('expo-print', () => ({
  printToFileAsync: (...args: unknown[]) => mockPrintToFileAsync(...args),
}));
jest.mock('expo-sharing', () => ({
  isAvailableAsync: (...args: unknown[]) => mockIsAvailableAsync(...args),
  shareAsync: (...args: unknown[]) => mockShareAsync(...args),
}));

// eslint-disable-next-line import/first -- debe importarse después de los jest.mock de sus dependencias
import { escapeHtml, generateAndShareReportPdf, wrapReportDocument } from './reportPdf';

describe('escapeHtml', () => {
  it('escapa &, <, >, comillas dobles y simples', () => {
    expect(escapeHtml(`<script>alert("x")</script> & 'ok'`)).toBe(
      '&lt;script&gt;alert(&quot;x&quot;)&lt;/script&gt; &amp; &#39;ok&#39;'
    );
  });

  it('convierte null/undefined en string vacío', () => {
    expect(escapeHtml(null)).toBe('');
    expect(escapeHtml(undefined)).toBe('');
  });

  it('convierte números a string sin escapar nada', () => {
    expect(escapeHtml(42)).toBe('42');
  });
});

describe('wrapReportDocument', () => {
  it('escapa nombres de evento/organizador maliciosos antes de interpolarlos', () => {
    const html = wrapReportDocument({
      eyebrow: 'HOYDONDE',
      title: 'Reporte',
      periodoLabel: 'Período: hoy',
      filtros: [{ label: 'Evento', value: '<img src=x onerror=alert(1)>' }],
      bodyHtml: '<p>cuerpo</p>',
    });

    expect(html).not.toContain('<img src=x onerror=alert(1)>');
    expect(html).toContain('&lt;img src=x onerror=alert(1)&gt;');
  });

  it('incluye el bodyHtml recibido sin modificarlo', () => {
    const html = wrapReportDocument({
      eyebrow: 'HOYDONDE',
      title: 'Reporte',
      periodoLabel: 'Período: hoy',
      filtros: [],
      bodyHtml: '<table><tr><td>fila</td></tr></table>',
    });

    expect(html).toContain('<table><tr><td>fila</td></tr></table>');
  });

  it('sin filtros, muestra "Sin filtros aplicados"', () => {
    const html = wrapReportDocument({
      eyebrow: 'HOYDONDE',
      title: 'Reporte',
      periodoLabel: 'Período: hoy',
      filtros: [],
      bodyHtml: '',
    });

    expect(html).toContain('Sin filtros aplicados');
  });

  it('incluye la aclaración de pagos simulados por default', () => {
    const html = wrapReportDocument({
      eyebrow: 'HOYDONDE',
      title: 'Reporte',
      periodoLabel: 'Período: hoy',
      filtros: [],
      bodyHtml: '',
    });

    expect(html).toContain('nunca una recaudación ni un cobro real');
  });

  it('permite reemplazar la aclaración (auditoría de seguridad)', () => {
    const html = wrapReportDocument({
      eyebrow: 'HOYDONDE',
      title: 'Reporte',
      periodoLabel: 'Período: hoy',
      filtros: [],
      bodyHtml: '',
      disclaimer: 'Aclaración custom',
    });

    expect(html).toContain('Aclaración custom');
    expect(html).not.toContain('nunca una recaudación ni un cobro real');
  });
});

describe('generateAndShareReportPdf', () => {
  afterEach(() => {
    jest.clearAllMocks();
  });

  it('genera el PDF y comparte cuando expo-sharing está disponible', async () => {
    mockPrintToFileAsync.mockResolvedValue({ uri: 'file://reporte.pdf' });
    mockIsAvailableAsync.mockResolvedValue(true);
    mockShareAsync.mockResolvedValue(undefined);

    const result = await generateAndShareReportPdf('<html></html>', 'Mi reporte');

    expect(mockPrintToFileAsync).toHaveBeenCalledWith({ html: '<html></html>' });
    expect(mockShareAsync).toHaveBeenCalledWith('file://reporte.pdf', expect.objectContaining({ mimeType: 'application/pdf', dialogTitle: 'Mi reporte' }));
    expect(result).toEqual({ uri: 'file://reporte.pdf', shared: true });
  });

  it('cuando sharing no está disponible, devuelve shared:false sin lanzar', async () => {
    mockPrintToFileAsync.mockResolvedValue({ uri: 'file://reporte.pdf' });
    mockIsAvailableAsync.mockResolvedValue(false);

    const result = await generateAndShareReportPdf('<html></html>', 'Mi reporte');

    expect(mockShareAsync).not.toHaveBeenCalled();
    expect(result).toEqual({ uri: 'file://reporte.pdf', shared: false });
  });
});
