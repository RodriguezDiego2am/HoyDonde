import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Alert, Modal, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { AsyncStateView } from '@/components/ui/AsyncStateView';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { SectionDivider } from '@/components/ui/SectionDivider';
import { Surface } from '@/components/ui/Surface';
import { ChipSelectRow } from '@/components/reports/ChipSelectRow';
import { ReportLedgerRow } from '@/components/reports/ReportLedgerRow';
import { SegmentedDateField } from '@/components/forms/SegmentedDateField';
import { SalesTimelineChart } from '@/components/charts/SalesTimelineChart';
import { TopEventosBarChart } from '@/components/charts/TopEventosBarChart';
import { REPORT_CATEGORIAS } from '@/constants/reportFilterOptions';
import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';
import { ACCIONES } from '@/constants/acciones';
import { useAuth } from '@/context/AuthContext';
import { ApiError } from '@/services/APIService';
import { securityAdminService, UsuarioResumenResponse } from '@/services/securityAdminService';
import { VentasReporteResponse, reportService } from '@/services/reportService';
import { isValidLocalDateRange, nextLocalDayExclusive, parseLocalDate, startOfLocalDay, toUtcIso } from '@/utils/datetime';
import { formatPrecio } from '@/utils/format';
import { generateAndShareReportPdf, wrapReportDocument } from '@/utils/reportPdf';
import { buildVentasBodyHtml } from '@/utils/salesReportPdfBuilder';

interface AppliedFilters {
  fechaDesde: string;
  fechaHasta: string;
  fechaDesdeDisplay: string;
  fechaHastaDisplay: string;
  categoria?: string;
  organizador?: UsuarioResumenResponse;
  eventId?: string;
  eventNombre?: string;
}

function fetchErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    if (error.isForbidden) return 'Tu cuenta no tiene permiso para ver este reporte.';
    return error.message || 'No se pudo generar el reporte.';
  }
  return 'No se pudo generar el reporte. Verificá tu conexión.';
}

/**
 * Reporte global de ventas simuladas del Administrador (docs/api-mvp-plan.md §11.11, GET
 * /reports/admin/sales). Como no existe un endpoint general de eventos para elegir de antemano, el
 * selector de evento se puebla con `report.filtrosDisponibles.eventos` -calculado en el backend
 * sobre TODAS las Compras del conjunto ya filtrado, no solo el Top 5- y solo existe una vez que ya
 * se generó un reporte con el rango/organizador/categoría deseados. Tocar una barra del Top 5 es
 * un atajo opcional que reutiliza exactamente el mismo `handleDrillIntoEvent`, así que ambos caminos
 * producen el mismo request. El nombre siempre se muestra; el id solo viaja internamente.
 */
export default function AdminSalesReportScreen() {
  const { hasAccion } = useAuth();
  const puedeListarUsuarios = hasAccion(ACCIONES.USUARIO_VER_PERMISOS_EFECTIVOS);

  const [fechaDesdeDraft, setFechaDesdeDraft] = useState('');
  const [fechaHastaDraft, setFechaHastaDraft] = useState('');
  const [categoriaDraft, setCategoriaDraft] = useState<string | undefined>(undefined);
  const [selectedOrganizador, setSelectedOrganizador] = useState<UsuarioResumenResponse | undefined>(undefined);
  const [filterError, setFilterError] = useState<string | null>(null);

  const [organizadorPickerVisible, setOrganizadorPickerVisible] = useState(false);
  const [organizadores, setOrganizadores] = useState<UsuarioResumenResponse[]>([]);
  const [organizadoresLoading, setOrganizadoresLoading] = useState(false);

  const [eventPickerVisible, setEventPickerVisible] = useState(false);

  const [applied, setApplied] = useState<AppliedFilters | null>(null);
  const [report, setReport] = useState<VentasReporteResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);

  useEffect(() => {
    if (!puedeListarUsuarios) return;
    let mounted = true;
    setOrganizadoresLoading(true);
    securityAdminService
      .listUsuarios()
      .then((usuarios: UsuarioResumenResponse[]) => {
        if (mounted) setOrganizadores(usuarios.filter((u) => u.rolesActivos.includes('ORGANIZADOR')));
      })
      .catch(() => {
        // Comodidad del filtro: si falla, el reporte sigue sin filtro por organizador.
      })
      .finally(() => {
        if (mounted) setOrganizadoresLoading(false);
      });
    return () => {
      mounted = false;
    };
  }, [puedeListarUsuarios]);

  const runReport = useCallback(async (filters: AppliedFilters) => {
    setLoading(true);
    setError(null);
    try {
      const data = await reportService.getAdminSalesReport({
        fechaDesde: filters.fechaDesde,
        fechaHasta: filters.fechaHasta,
        categoria: filters.categoria,
        organizadorPersonaId: filters.organizador?.personaId,
        eventId: filters.eventId,
      });
      setReport(data);
    } catch (err) {
      setError(fetchErrorMessage(err));
    } finally {
      setLoading(false);
    }
  }, []);

  const handleApply = (overrides?: Partial<AppliedFilters>) => {
    const desdeTexto = fechaDesdeDraft.trim();
    const hastaTexto = fechaHastaDraft.trim();
    if (!/\d/.test(desdeTexto) || !/\d/.test(hastaTexto)) {
      setFilterError('Ingresá el rango "Desde" y "Hasta": ambos son obligatorios para este reporte.');
      return;
    }
    const desdeDate = parseLocalDate(desdeTexto);
    const hastaDate = parseLocalDate(hastaTexto);
    if (!desdeDate) {
      setFilterError('Ingresá una fecha "Desde" válida (DD/MM/AAAA).');
      return;
    }
    if (!hastaDate) {
      setFilterError('Ingresá una fecha "Hasta" válida (DD/MM/AAAA).');
      return;
    }
    if (!isValidLocalDateRange(desdeDate, hastaDate)) {
      setFilterError('"Desde" no puede ser posterior a "Hasta".');
      return;
    }

    setFilterError(null);
    const filters: AppliedFilters = {
      fechaDesde: toUtcIso(startOfLocalDay(desdeDate)),
      fechaHasta: toUtcIso(nextLocalDayExclusive(hastaDate)),
      fechaDesdeDisplay: desdeTexto,
      fechaHastaDisplay: hastaTexto,
      categoria: categoriaDraft,
      organizador: selectedOrganizador,
      ...overrides,
    };
    setApplied(filters);
    runReport(filters);
  };

  const handleClear = () => {
    setFechaDesdeDraft('');
    setFechaHastaDraft('');
    setCategoriaDraft(undefined);
    setSelectedOrganizador(undefined);
    setFilterError(null);
    setApplied(null);
    setReport(null);
    setError(null);
  };

  const handleRefresh = () => {
    if (applied) runReport(applied);
  };

  const handleDrillIntoEvent = (eventId: string, eventNombre: string) => {
    if (!applied) return;
    handleApply({ eventId, eventNombre });
  };

  const handleClearEventFilter = () => {
    if (!applied) return;
    handleApply({ eventId: undefined, eventNombre: undefined });
  };

  const filtrosActivosLabel = useMemo(() => {
    if (!applied) return null;
    const partes = [`${applied.fechaDesdeDisplay} – ${applied.fechaHastaDisplay}`];
    if (applied.categoria) partes.push(applied.categoria);
    if (applied.organizador) partes.push(applied.organizador.email);
    if (applied.eventNombre) partes.push(applied.eventNombre);
    return partes.join(' · ');
  }, [applied]);

  const handleExportPdf = async () => {
    if (!report || !applied || exporting) return;
    setExporting(true);
    try {
      const filtros = [{ label: 'Período (fecha de venta)', value: `${applied.fechaDesdeDisplay} – ${applied.fechaHastaDisplay}` }];
      if (applied.categoria) filtros.push({ label: 'Categoría', value: applied.categoria });
      if (applied.organizador) filtros.push({ label: 'Organizador', value: applied.organizador.email });
      if (applied.eventNombre) filtros.push({ label: 'Evento', value: applied.eventNombre });

      const html = wrapReportDocument({
        eyebrow: 'HOYDONDE · ADMINISTRACIÓN',
        title: 'Reporte global de ventas simuladas',
        periodoLabel: `Ventas entre ${applied.fechaDesdeDisplay} y ${applied.fechaHastaDisplay}`,
        filtros,
        bodyHtml: buildVentasBodyHtml(report),
        disclaimer: report.aclaracionImporte,
      });

      const result = await generateAndShareReportPdf(html, 'Reporte global de ventas');
      if (!result.shared) {
        Alert.alert('PDF generado', `No se pudo abrir el selector para compartir en este dispositivo. El archivo quedó guardado en:\n${result.uri}`);
      }
    } catch {
      Alert.alert('No se pudo generar el PDF', 'Intentá de nuevo en unos segundos.');
    } finally {
      setExporting(false);
    }
  };

  return (
    <View style={styles.container}>
      <EditorialHeader eyebrow="ADMINISTRACIÓN" title="Ventas simuladas" subtitle="Cuándo y cuánto se vendió, en toda la plataforma." showBack onRefresh={applied ? handleRefresh : undefined} />

      <ScrollView contentContainerStyle={styles.scroll}>
        <SectionDivider index="01" label="Filtros" />
        <Surface style={styles.card}>
          <View style={styles.dateRow}>
            <View style={styles.dateField}>
              <SegmentedDateField label="Desde" value={fechaDesdeDraft} onChange={setFechaDesdeDraft} testIDPrefix="admin-ventas-fecha-desde" />
            </View>
            <View style={styles.dateField}>
              <SegmentedDateField label="Hasta" value={fechaHastaDraft} onChange={setFechaHastaDraft} testIDPrefix="admin-ventas-fecha-hasta" />
            </View>
          </View>
          {filterError ? (
            <Text style={styles.errorText} accessibilityRole="alert" accessibilityLiveRegion="polite">
              {filterError}
            </Text>
          ) : null}

          <Text style={styles.fieldLabel}>Categoría</Text>
          <ChipSelectRow options={REPORT_CATEGORIAS} selected={categoriaDraft} onSelect={setCategoriaDraft} />

          {puedeListarUsuarios ? (
            <>
              <Text style={[styles.fieldLabel, styles.fieldLabelSpaced]}>Organizador (opcional)</Text>
              <Pressable
                accessibilityRole="button"
                accessibilityLabel="Elegir organizador"
                onPress={() => setOrganizadorPickerVisible(true)}
                style={({ pressed }) => [styles.picker, pressed && styles.pickerPressed]}
              >
                <MaterialIcons name="person" size={18} color={colors.ink} />
                <Text style={styles.pickerText} numberOfLines={1}>
                  {selectedOrganizador ? selectedOrganizador.email : 'Todos los organizadores'}
                </Text>
                {selectedOrganizador ? (
                  <Pressable accessibilityRole="button" accessibilityLabel="Quitar organizador seleccionado" hitSlop={10} onPress={() => setSelectedOrganizador(undefined)}>
                    <MaterialIcons name="close" size={18} color={colors.inkSoft} />
                  </Pressable>
                ) : (
                  <MaterialIcons name="chevron-right" size={18} color={colors.inkSoft} />
                )}
              </Pressable>
            </>
          ) : null}

          <Text style={[styles.fieldLabel, styles.fieldLabelSpaced]}>Evento (opcional)</Text>
          <Pressable
            accessibilityRole="button"
            accessibilityLabel="Elegir evento"
            disabled={!report}
            onPress={() => setEventPickerVisible(true)}
            style={({ pressed }) => [styles.picker, !report && styles.pickerDisabled, pressed && report && styles.pickerPressed]}
          >
            <MaterialIcons name="event" size={18} color={report ? colors.ink : colors.inkSoft} />
            <Text style={[styles.pickerText, !report && styles.pickerTextDisabled]} numberOfLines={1}>
              {applied?.eventNombre ?? (report ? 'Todos los eventos' : 'Generá el reporte para elegir un evento')}
            </Text>
            {applied?.eventNombre ? (
              <Pressable accessibilityRole="button" accessibilityLabel="Quitar evento seleccionado" hitSlop={10} onPress={handleClearEventFilter}>
                <MaterialIcons name="close" size={18} color={colors.inkSoft} />
              </Pressable>
            ) : (
              <MaterialIcons name="chevron-right" size={18} color={colors.inkSoft} />
            )}
          </Pressable>

          <View style={styles.footerButtons}>
            <View style={styles.footerButton}>
              <ActionButton label="Limpiar" variant="secondary" onPress={handleClear} />
            </View>
            <View style={styles.footerButton}>
              <ActionButton label="Aplicar filtros" onPress={() => handleApply({ eventId: undefined, eventNombre: undefined })} />
            </View>
          </View>
        </Surface>

        {applied ? (
          <View style={styles.appliedBanner}>
            <MaterialIcons name="filter-alt" size={14} color={colors.inkSoft} />
            <Text style={styles.appliedText} numberOfLines={2}>
              {filtrosActivosLabel}
            </Text>
            {applied.eventNombre ? (
              <Pressable accessibilityRole="button" accessibilityLabel="Quitar filtro de evento" onPress={handleClearEventFilter} hitSlop={8}>
                <MaterialIcons name="close" size={16} color={colors.inkSoft} />
              </Pressable>
            ) : null}
          </View>
        ) : null}

        {loading ? (
          <AsyncStateView variant="loading" message="Generando reporte" />
        ) : error ? (
          <AsyncStateView variant="error" message={error} onRetry={handleRefresh} />
        ) : !report || !applied ? (
          <AsyncStateView
            variant="empty"
            icon="insights"
            message="Elegí un período y aplicá filtros para generar el reporte de ventas."
            hint="El rango Desde/Hasta es obligatorio (filtra por fecha de compra)."
          />
        ) : (
          <>
            <SectionDivider index="02" label="Lectura rápida" />
            <Surface style={styles.card}>
              <ReportLedgerRow label="Importe emitido" value={formatPrecio(report.resumen.importeEmitido)} emphasis />
              <ReportLedgerRow label="Compras" value={String(report.resumen.cantidadCompras)} />
              <ReportLedgerRow label="Entradas" value={String(report.resumen.entradasEmitidas)} />
              <ReportLedgerRow label="Compra promedio" value={formatPrecio(report.resumen.importePromedioPorCompra)} />
              <ReportLedgerRow label="Precio promedio" value={formatPrecio(report.resumen.precioPromedioEntrada)} />
              <ReportLedgerRow label="Clientes únicos" value={String(report.resumen.clientesUnicos)} />
              {report.resumen.eventoConMayorImporte ? (
                <ReportLedgerRow label="Evento destacado" value={report.resumen.eventoConMayorImporte.eventoNombre} />
              ) : null}
            </Surface>
            <Text style={styles.disclaimer}>{report.aclaracionImporte}</Text>

            <SectionDivider index="03" label="Evolución temporal" />
            <Surface style={styles.card}>
              <SalesTimelineChart buckets={report.serieTemporal} />
            </Surface>

            <SectionDivider index="04" label="Top eventos por importe emitido" />
            <Surface style={styles.card}>
              <TopEventosBarChart
                items={report.topEventos.map((e) => ({ key: e.eventoId, nombre: e.eventoNombre, importeEmitido: e.importeEmitido, entradasEmitidas: e.entradasEmitidas }))}
                onPressItem={(item) => handleDrillIntoEvent(item.key, item.nombre)}
              />
            </Surface>

            <SectionDivider index="05" label="Por categoría" />
            <Surface style={styles.card}>
              {report.porCategoria.length === 0 ? (
                <Text style={styles.emptySection}>Sin datos para el período elegido.</Text>
              ) : (
                report.porCategoria.map((c) => (
                  <ReportLedgerRow key={c.categoria} label={c.categoria} value={`${formatPrecio(c.importeEmitido)} (${c.porcentajeDelImporteTotal.toFixed(1)}%)`} />
                ))
              )}
            </Surface>

            {report.porTipoEntrada.length > 0 ? (
              <>
                <SectionDivider index="06" label="Por tipo de entrada" />
                <Surface style={styles.card}>
                  {report.porTipoEntrada.map((t) => (
                    <ReportLedgerRow key={t.ticketTypeId} label={t.ticketTypeNombre} value={`${formatPrecio(t.importeEmitido)} (${t.porcentajeDelImporteTotal.toFixed(1)}%)`} />
                  ))}
                </Surface>
              </>
            ) : null}

            <View style={styles.exportRow}>
              <ActionButton label="Generar y compartir PDF" onPress={handleExportPdf} loading={exporting} disabled={exporting} />
            </View>
          </>
        )}
      </ScrollView>

      <Modal visible={organizadorPickerVisible} animationType="slide" transparent onRequestClose={() => setOrganizadorPickerVisible(false)}>
        <View style={styles.backdrop}>
          <Pressable style={StyleSheet.absoluteFill} onPress={() => setOrganizadorPickerVisible(false)} />
          <View style={styles.sheet}>
            <View style={styles.sheetHeader}>
              <Text style={styles.sheetTitle}>Elegí un organizador</Text>
              <Pressable accessibilityRole="button" accessibilityLabel="Cerrar" onPress={() => setOrganizadorPickerVisible(false)} hitSlop={10}>
                <MaterialIcons name="close" size={20} color={colors.ink} />
              </Pressable>
            </View>
            <ScrollView style={styles.sheetList}>
              <Pressable
                style={styles.sheetRow}
                onPress={() => {
                  setSelectedOrganizador(undefined);
                  setOrganizadorPickerVisible(false);
                }}
              >
                <Text style={styles.sheetRowText}>Todos los organizadores</Text>
              </Pressable>
              {organizadoresLoading ? (
                <Text style={styles.sheetHint}>Cargando organizadores…</Text>
              ) : organizadores.length === 0 ? (
                <Text style={styles.sheetHint}>No hay organizadores registrados.</Text>
              ) : (
                organizadores.map((org) => (
                  <Pressable
                    key={org.usuarioId}
                    style={styles.sheetRow}
                    onPress={() => {
                      setSelectedOrganizador(org);
                      setOrganizadorPickerVisible(false);
                    }}
                  >
                    <Text style={styles.sheetRowText} numberOfLines={1}>
                      {org.email}
                    </Text>
                  </Pressable>
                ))
              )}
            </ScrollView>
          </View>
        </View>
      </Modal>

      <Modal visible={eventPickerVisible} animationType="slide" transparent onRequestClose={() => setEventPickerVisible(false)}>
        <View style={styles.backdrop}>
          <Pressable style={StyleSheet.absoluteFill} onPress={() => setEventPickerVisible(false)} />
          <View style={styles.sheet}>
            <View style={styles.sheetHeader}>
              <Text style={styles.sheetTitle}>Elegí un evento</Text>
              <Pressable accessibilityRole="button" accessibilityLabel="Cerrar" onPress={() => setEventPickerVisible(false)} hitSlop={10}>
                <MaterialIcons name="close" size={20} color={colors.ink} />
              </Pressable>
            </View>
            <ScrollView style={styles.sheetList}>
              <Pressable
                style={styles.sheetRow}
                onPress={() => {
                  setEventPickerVisible(false);
                  handleClearEventFilter();
                }}
              >
                <Text style={styles.sheetRowText}>Todos los eventos</Text>
              </Pressable>
              {(report?.filtrosDisponibles.eventos.length ?? 0) === 0 ? (
                <Text style={styles.sheetHint}>Ningún evento con ventas en el período elegido.</Text>
              ) : (
                report!.filtrosDisponibles.eventos.map((evento) => (
                  <Pressable
                    key={evento.id}
                    style={styles.sheetRow}
                    onPress={() => {
                      setEventPickerVisible(false);
                      handleDrillIntoEvent(evento.id, evento.nombre);
                    }}
                  >
                    <Text style={styles.sheetRowText} numberOfLines={1}>
                      {evento.nombre}
                    </Text>
                  </Pressable>
                ))
              )}
            </ScrollView>
          </View>
        </View>
      </Modal>
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: colors.paper },
  scroll: { padding: spacing.lg, paddingBottom: spacing.xxl },
  card: { marginBottom: spacing.md },
  dateRow: { flexDirection: 'row', gap: spacing.md },
  dateField: { flex: 1 },
  errorText: { fontFamily: fonts.medium, color: colors.error, fontSize: 13, marginTop: -spacing.sm, marginBottom: spacing.sm },
  fieldLabel: { fontFamily: fonts.bold, fontSize: 12, letterSpacing: 0.8, textTransform: 'uppercase', marginBottom: spacing.xs, color: colors.inkSoft },
  fieldLabelSpaced: { marginTop: spacing.md },
  picker: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    borderWidth: borderWidth.thin,
    borderColor: colors.ink,
    borderRadius: radii.sm,
    paddingVertical: 10,
    paddingHorizontal: spacing.md,
  },
  pickerPressed: { backgroundColor: colors.sand },
  pickerDisabled: { borderColor: colors.sand },
  pickerText: { flex: 1, fontFamily: fonts.medium, fontSize: 14, color: colors.ink },
  pickerTextDisabled: { color: colors.inkSoft },
  footerButtons: { flexDirection: 'row', gap: spacing.sm, marginTop: spacing.lg },
  footerButton: { flex: 1 },
  appliedBanner: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs, marginBottom: spacing.md },
  appliedText: { flex: 1, fontFamily: fonts.medium, fontSize: 12, color: colors.inkSoft },
  disclaimer: { fontFamily: fonts.regular, fontSize: 11, color: colors.inkSoft, fontStyle: 'italic', marginBottom: spacing.md },
  emptySection: { fontFamily: fonts.regular, fontSize: 13, color: colors.inkSoft, fontStyle: 'italic' },
  exportRow: { marginTop: spacing.md },
  backdrop: { flex: 1, backgroundColor: 'rgba(23, 21, 18, 0.5)', justifyContent: 'flex-end' },
  sheet: {
    backgroundColor: colors.paper,
    borderTopWidth: borderWidth.thick,
    borderColor: colors.ink,
    borderTopLeftRadius: radii.lg,
    borderTopRightRadius: radii.lg,
    maxHeight: '75%',
    paddingHorizontal: spacing.lg,
    paddingTop: spacing.md,
    paddingBottom: spacing.lg,
  },
  sheetHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'center', marginBottom: spacing.md },
  sheetTitle: { fontFamily: fonts.black, fontSize: 20, color: colors.ink },
  sheetList: { maxHeight: 360 },
  sheetRow: { paddingVertical: spacing.sm, borderBottomWidth: 1, borderBottomColor: colors.sand },
  sheetRowText: { fontFamily: fonts.medium, fontSize: 15, color: colors.ink },
  sheetHint: { fontFamily: fonts.regular, fontSize: 13, color: colors.inkSoft, paddingVertical: spacing.md },
});
