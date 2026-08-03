import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Alert, Modal, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { AsyncStateView } from '@/components/ui/AsyncStateView';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { SectionDivider } from '@/components/ui/SectionDivider';
import { StatusStamp, eventEstadoTone } from '@/components/ui/StatusStamp';
import { Surface } from '@/components/ui/Surface';
import { ChipSelectRow } from '@/components/reports/ChipSelectRow';
import { ReportLedgerRow } from '@/components/reports/ReportLedgerRow';
import { SegmentedDateField } from '@/components/forms/SegmentedDateField';
import { ACCIONES } from '@/constants/acciones';
import { REPORT_CATEGORIAS, REPORT_ESTADOS } from '@/constants/reportFilterOptions';
import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';
import { ApiError } from '@/services/APIService';
import { securityAdminService, UsuarioResumenResponse } from '@/services/securityAdminService';
import { ReporteAdminEventosResponse, ReporteEventoEstado, reportService } from '@/services/reportService';
import { isValidLocalDateRange, nextLocalDayExclusive, parseLocalDate, startOfLocalDay, toUtcIso } from '@/utils/datetime';
import { formatFecha, formatPrecio } from '@/utils/format';
import { generateAndShareReportPdf, wrapReportDocument } from '@/utils/reportPdf';
import { buildEventosSectionHtml, buildResumenTableHtml } from '@/utils/reportPdfBuilders';

interface AppliedFilters {
  fechaDesde: string;
  fechaHasta: string;
  fechaDesdeDisplay: string;
  fechaHastaDisplay: string;
  estado?: ReporteEventoEstado;
  categoria?: string;
  organizador?: UsuarioResumenResponse;
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
 * Reporte global de eventos del Administrador (docs/api-mvp-plan.md §11, GET
 * /reports/admin/events). El selector de organizador reutiliza GET /api/security/usuarios
 * (USUARIO_VER_PERMISOS_EFECTIVOS) filtrando por rol ORGANIZADOR: si la sesión no tiene esa
 * acción, el selector queda oculto pero el resto del reporte sigue funcionando sin ese filtro.
 */
export default function AdminEventsReportScreen() {
  const { hasAccion } = useAuth();
  const puedeListarUsuarios = hasAccion(ACCIONES.USUARIO_VER_PERMISOS_EFECTIVOS);

  const [fechaDesdeDraft, setFechaDesdeDraft] = useState('');
  const [fechaHastaDraft, setFechaHastaDraft] = useState('');
  const [estadoDraft, setEstadoDraft] = useState<ReporteEventoEstado | undefined>(undefined);
  const [categoriaDraft, setCategoriaDraft] = useState<string | undefined>(undefined);
  const [selectedOrganizador, setSelectedOrganizador] = useState<UsuarioResumenResponse | undefined>(undefined);
  const [filterError, setFilterError] = useState<string | null>(null);

  const [organizadorPickerVisible, setOrganizadorPickerVisible] = useState(false);
  const [organizadores, setOrganizadores] = useState<UsuarioResumenResponse[]>([]);
  const [organizadoresLoading, setOrganizadoresLoading] = useState(false);

  const [applied, setApplied] = useState<AppliedFilters | null>(null);
  const [report, setReport] = useState<ReporteAdminEventosResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);

  useEffect(() => {
    if (!puedeListarUsuarios) return;
    let mounted = true;
    setOrganizadoresLoading(true);
    securityAdminService
      .listUsuarios()
      .then((usuarios) => {
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

  const organizadorNombrePorPersonaId = useMemo(() => {
    const map: Record<string, string> = {};
    organizadores.forEach((o) => {
      map[o.personaId] = o.email;
    });
    return map;
  }, [organizadores]);

  const runReport = useCallback(async (filters: AppliedFilters) => {
    setLoading(true);
    setError(null);
    try {
      const data = await reportService.getAdminEventsReport({
        fechaDesde: filters.fechaDesde,
        fechaHasta: filters.fechaHasta,
        estado: filters.estado,
        categoria: filters.categoria,
        organizadorPersonaId: filters.organizador?.personaId,
      });
      setReport(data);
    } catch (err) {
      setError(fetchErrorMessage(err));
    } finally {
      setLoading(false);
    }
  }, []);

  const handleApply = () => {
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
      estado: estadoDraft,
      categoria: categoriaDraft,
      organizador: selectedOrganizador,
    };
    setApplied(filters);
    runReport(filters);
  };

  const handleClear = () => {
    setFechaDesdeDraft('');
    setFechaHastaDraft('');
    setEstadoDraft(undefined);
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

  const filtrosActivosLabel = useMemo(() => {
    if (!applied) return null;
    const partes = [`${applied.fechaDesdeDisplay} – ${applied.fechaHastaDisplay}`];
    if (applied.estado) partes.push(applied.estado);
    if (applied.categoria) partes.push(applied.categoria);
    if (applied.organizador) partes.push(applied.organizador.email);
    return partes.join(' · ');
  }, [applied]);

  const handleExportPdf = async () => {
    if (!report || !applied || exporting) return;
    setExporting(true);
    try {
      const filtros = [{ label: 'Período', value: `${applied.fechaDesdeDisplay} – ${applied.fechaHastaDisplay}` }];
      if (applied.estado) filtros.push({ label: 'Estado', value: applied.estado });
      if (applied.categoria) filtros.push({ label: 'Categoría', value: applied.categoria });
      if (applied.organizador) filtros.push({ label: 'Organizador', value: applied.organizador.email });

      const html = wrapReportDocument({
        eyebrow: 'HOYDONDE · ADMINISTRACIÓN',
        title: 'Reporte global de eventos',
        periodoLabel: `Período: ${applied.fechaDesdeDisplay} – ${applied.fechaHastaDisplay}`,
        filtros,
        bodyHtml: buildResumenTableHtml(report.resumen) + buildEventosSectionHtml(report.eventos, { organizadorNombrePorPersonaId }),
        disclaimer: report.aclaracionImporte,
      });

      const result = await generateAndShareReportPdf(html, 'Reporte global de eventos');
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
      <EditorialHeader eyebrow="ADMINISTRACIÓN" title="Eventos (global)" subtitle="Actividad de eventos de cualquier organizador." showBack onRefresh={applied ? handleRefresh : undefined} />

      <ScrollView contentContainerStyle={styles.scroll}>
        <SectionDivider index="01" label="Filtros" />
        <Surface style={styles.card}>
          <View style={styles.dateRow}>
            <View style={styles.dateField}>
              <SegmentedDateField label="Desde" value={fechaDesdeDraft} onChange={setFechaDesdeDraft} testIDPrefix="admin-reporte-fecha-desde" />
            </View>
            <View style={styles.dateField}>
              <SegmentedDateField label="Hasta" value={fechaHastaDraft} onChange={setFechaHastaDraft} testIDPrefix="admin-reporte-fecha-hasta" />
            </View>
          </View>
          {filterError ? (
            <Text style={styles.errorText} accessibilityRole="alert" accessibilityLiveRegion="polite">
              {filterError}
            </Text>
          ) : null}

          <Text style={styles.fieldLabel}>Estado</Text>
          <ChipSelectRow options={REPORT_ESTADOS} selected={estadoDraft} onSelect={setEstadoDraft} />

          <Text style={[styles.fieldLabel, styles.fieldLabelSpaced]}>Categoría</Text>
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

          <View style={styles.footerButtons}>
            <View style={styles.footerButton}>
              <ActionButton label="Limpiar" variant="secondary" onPress={handleClear} />
            </View>
            <View style={styles.footerButton}>
              <ActionButton label="Aplicar filtros" onPress={handleApply} />
            </View>
          </View>
        </Surface>

        {applied ? (
          <View style={styles.appliedBanner}>
            <MaterialIcons name="filter-alt" size={14} color={colors.inkSoft} />
            <Text style={styles.appliedText} numberOfLines={2}>
              {filtrosActivosLabel}
            </Text>
          </View>
        ) : null}

        {loading ? (
          <AsyncStateView variant="loading" message="Generando reporte" />
        ) : error ? (
          <AsyncStateView variant="error" message={error} onRetry={handleRefresh} />
        ) : !report || !applied ? (
          <AsyncStateView variant="empty" icon="query-stats" message="Elegí un período y aplicá filtros para generar el reporte." hint="El rango Desde/Hasta es obligatorio." />
        ) : (
          <>
            <SectionDivider index="02" label="Resumen" />
            <Surface style={styles.card}>
              <ReportLedgerRow label="Eventos en el período" value={String(report.resumen.cantidadEventos)} />
              <ReportLedgerRow label="Entradas emitidas" value={String(report.resumen.entradasEmitidas)} />
              <ReportLedgerRow label="Entradas usadas" value={String(report.resumen.entradasUsadas)} />
              <ReportLedgerRow label="Entradas pendientes" value={String(report.resumen.entradasPendientes)} />
              <ReportLedgerRow label="% Ocupación" value={`${report.resumen.porcentajeOcupacion.toFixed(1)}%`} />
              <ReportLedgerRow label="% Asistencia" value={`${report.resumen.porcentajeAsistencia.toFixed(1)}%`} />
              <ReportLedgerRow label="Importe emitido" value={formatPrecio(report.resumen.importeEmitido)} emphasis />
            </Surface>
            <Text style={styles.disclaimer}>{report.aclaracionImporte}</Text>

            <SectionDivider index="03" label={`Eventos (${report.eventos.length})`} />
            {report.eventos.length === 0 ? (
              <AsyncStateView variant="empty" icon="event-busy" message="Ningún evento coincide con los filtros aplicados." />
            ) : (
              report.eventos.map((evento) => (
                <Surface key={evento.eventId} style={styles.eventCard}>
                  <View style={styles.eventHeader}>
                    <Text style={styles.eventName} numberOfLines={2}>
                      {evento.nombre}
                    </Text>
                    <StatusStamp label={evento.estado} tone={eventEstadoTone(evento.estado)} />
                  </View>
                  <Text style={styles.eventMeta}>
                    {evento.categoria} · {formatFecha(evento.fechaInicio)} · {organizadorNombrePorPersonaId[evento.organizadorPersonaId] ?? evento.organizadorPersonaId}
                  </Text>
                  <ReportLedgerRow label="Emitidas / Usadas / Pendientes" value={`${evento.entradasEmitidas} / ${evento.entradasUsadas} / ${evento.entradasPendientes}`} />
                  <ReportLedgerRow label="Importe emitido" value={formatPrecio(evento.importeEmitido)} />
                </Surface>
              ))
            )}

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
                    <Text style={styles.sheetRowMeta}>{org.activo ? 'Activo' : 'Inactivo'}</Text>
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
  pickerText: { flex: 1, fontFamily: fonts.medium, fontSize: 14, color: colors.ink },
  footerButtons: { flexDirection: 'row', gap: spacing.sm, marginTop: spacing.lg },
  footerButton: { flex: 1 },
  appliedBanner: { flexDirection: 'row', alignItems: 'center', gap: spacing.xs, marginBottom: spacing.md },
  appliedText: { flex: 1, fontFamily: fonts.medium, fontSize: 12, color: colors.inkSoft },
  disclaimer: { fontFamily: fonts.regular, fontSize: 11, color: colors.inkSoft, fontStyle: 'italic', marginBottom: spacing.md },
  eventCard: { marginBottom: spacing.md },
  eventHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start', gap: spacing.sm, marginBottom: spacing.xs },
  eventName: { flex: 1, fontFamily: fonts.bold, fontSize: 16, color: colors.ink },
  eventMeta: { fontFamily: fonts.regular, fontSize: 12, color: colors.inkSoft, marginBottom: spacing.xs },
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
  sheetRowMeta: { fontFamily: fonts.regular, fontSize: 12, color: colors.inkSoft, marginTop: 2 },
  sheetHint: { fontFamily: fonts.regular, fontSize: 13, color: colors.inkSoft, paddingVertical: spacing.md },
});
