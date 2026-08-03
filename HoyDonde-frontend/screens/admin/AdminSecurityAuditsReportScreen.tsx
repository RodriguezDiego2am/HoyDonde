import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { Alert, Modal, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { AsyncStateView } from '@/components/ui/AsyncStateView';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { SectionDivider } from '@/components/ui/SectionDivider';
import { Surface } from '@/components/ui/Surface';
import { ChipSelectRow } from '@/components/reports/ChipSelectRow';
import FormInput from '@/components/FormInput';
import { SegmentedDateField } from '@/components/forms/SegmentedDateField';
import { ACCIONES } from '@/constants/acciones';
import { REPORT_OPERACIONES_AUDITORIA, REPORT_TARGET_TIPOS } from '@/constants/reportFilterOptions';
import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';
import { useAuth } from '@/context/AuthContext';
import { ApiError } from '@/services/APIService';
import { securityAdminService, UsuarioResumenResponse } from '@/services/securityAdminService';
import { SecurityAuditReporteResponse, SecurityAuditTargetTipo, reportService } from '@/services/reportService';
import { isValidLocalDateRange, nextLocalDayExclusive, parseLocalDate, startOfLocalDay, toUtcIso } from '@/utils/datetime';
import { formatFechaHora } from '@/utils/format';
import { generateAndShareReportPdf, wrapReportDocument } from '@/utils/reportPdf';
import { buildSecurityAuditSectionHtml } from '@/utils/reportPdfBuilders';

interface AppliedFilters {
  fechaDesde?: string;
  fechaHasta?: string;
  fechaDesdeDisplay?: string;
  fechaHastaDisplay?: string;
  operacion?: string;
  actor?: UsuarioResumenResponse;
  targetTipo?: SecurityAuditTargetTipo;
  targetId?: string;
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
 * Auditoría de seguridad, primer corte aprobado (docs/api-mvp-plan.md §11.3, GET
 * /reports/admin/security-audits). Sin rango informado, el backend aplica el default de 30
 * días -acá se muestra siempre el rango EFECTIVO que devuelve la respuesta, nunca lo que el
 * usuario tipeó, para no mentir sobre qué período se está mirando realmente.
 */
export default function AdminSecurityAuditsReportScreen() {
  const { hasAccion } = useAuth();
  const puedeListarUsuarios = hasAccion(ACCIONES.USUARIO_VER_PERMISOS_EFECTIVOS);

  const [fechaDesdeDraft, setFechaDesdeDraft] = useState('');
  const [fechaHastaDraft, setFechaHastaDraft] = useState('');
  const [operacionDraft, setOperacionDraft] = useState<string | undefined>(undefined);
  const [selectedActor, setSelectedActor] = useState<UsuarioResumenResponse | undefined>(undefined);
  const [targetTipoDraft, setTargetTipoDraft] = useState<SecurityAuditTargetTipo | undefined>(undefined);
  const [targetIdDraft, setTargetIdDraft] = useState('');
  const [filterError, setFilterError] = useState<string | null>(null);

  const [actorPickerVisible, setActorPickerVisible] = useState(false);
  const [usuarios, setUsuarios] = useState<UsuarioResumenResponse[]>([]);
  const [usuariosLoading, setUsuariosLoading] = useState(false);

  const [applied, setApplied] = useState<AppliedFilters | null>(null);
  const [report, setReport] = useState<SecurityAuditReporteResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);

  useEffect(() => {
    if (!puedeListarUsuarios) return;
    let mounted = true;
    setUsuariosLoading(true);
    securityAdminService
      .listUsuarios()
      .then((data) => {
        if (mounted) setUsuarios(data);
      })
      .catch(() => {
        // Comodidad del filtro: si falla, el reporte sigue sin filtro por actor.
      })
      .finally(() => {
        if (mounted) setUsuariosLoading(false);
      });
    return () => {
      mounted = false;
    };
  }, [puedeListarUsuarios]);

  const runReport = useCallback(async (filters: AppliedFilters) => {
    setLoading(true);
    setError(null);
    try {
      const data = await reportService.getSecurityAuditsReport({
        fechaDesde: filters.fechaDesde,
        fechaHasta: filters.fechaHasta,
        operacion: filters.operacion,
        actorUsuarioId: filters.actor?.usuarioId,
        targetTipo: filters.targetTipo,
        targetId: filters.targetId || undefined,
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
    const desdeProvista = /\d/.test(desdeTexto);
    const hastaProvista = /\d/.test(hastaTexto);
    const desdeDate = desdeProvista ? parseLocalDate(desdeTexto) : null;
    const hastaDate = hastaProvista ? parseLocalDate(hastaTexto) : null;

    if (desdeProvista && !desdeDate) {
      setFilterError('Ingresá una fecha "Desde" válida (DD/MM/AAAA).');
      return;
    }
    if (hastaProvista && !hastaDate) {
      setFilterError('Ingresá una fecha "Hasta" válida (DD/MM/AAAA).');
      return;
    }
    if (desdeDate && hastaDate && !isValidLocalDateRange(desdeDate, hastaDate)) {
      setFilterError('"Desde" no puede ser posterior a "Hasta".');
      return;
    }

    setFilterError(null);
    const filters: AppliedFilters = {
      fechaDesde: desdeDate ? toUtcIso(startOfLocalDay(desdeDate)) : undefined,
      fechaHasta: hastaDate ? toUtcIso(nextLocalDayExclusive(hastaDate)) : undefined,
      fechaDesdeDisplay: desdeDate ? desdeTexto : undefined,
      fechaHastaDisplay: hastaDate ? hastaTexto : undefined,
      operacion: operacionDraft,
      actor: selectedActor,
      targetTipo: targetTipoDraft,
      targetId: targetIdDraft.trim(),
    };
    setApplied(filters);
    runReport(filters);
  };

  const handleClear = () => {
    setFechaDesdeDraft('');
    setFechaHastaDraft('');
    setOperacionDraft(undefined);
    setSelectedActor(undefined);
    setTargetTipoDraft(undefined);
    setTargetIdDraft('');
    setFilterError(null);
    setApplied(null);
    setReport(null);
    setError(null);
  };

  const handleRefresh = () => {
    if (applied) runReport(applied);
  };

  const periodoEfectivoLabel = useMemo(() => {
    if (!report) return null;
    return `${formatFechaHora(report.fechaDesde)} – ${formatFechaHora(report.fechaHasta)}`;
  }, [report]);

  const filtrosActivosLabel = useMemo(() => {
    if (!applied) return null;
    const partes: string[] = [];
    if (applied.operacion) partes.push(applied.operacion);
    if (applied.actor) partes.push(applied.actor.email);
    if (applied.targetTipo) partes.push(applied.targetTipo);
    if (applied.targetId) partes.push(`id: ${applied.targetId}`);
    return partes.length ? partes.join(' · ') : 'Sin filtros adicionales';
  }, [applied]);

  const handleExportPdf = async () => {
    if (!report || !applied || exporting) return;
    setExporting(true);
    try {
      const filtros = [{ label: 'Período efectivo', value: periodoEfectivoLabel ?? '' }];
      if (applied.operacion) filtros.push({ label: 'Operación', value: applied.operacion });
      if (applied.actor) filtros.push({ label: 'Actor', value: applied.actor.email });
      if (applied.targetTipo) filtros.push({ label: 'Objetivo', value: applied.targetTipo });
      if (applied.targetId) filtros.push({ label: 'Id de objetivo', value: applied.targetId });

      const html = wrapReportDocument({
        eyebrow: 'HOYDONDE · ADMINISTRACIÓN',
        title: 'Auditoría de seguridad',
        periodoLabel: `Período: ${periodoEfectivoLabel}`,
        filtros,
        bodyHtml: buildSecurityAuditSectionHtml(report.auditorias),
        disclaimer:
          'Esta auditoría refleja mutaciones de administración de seguridad (roles, acciones, usuarios), no movimientos de dinero: el MVP no procesa pagos reales.',
      });

      const result = await generateAndShareReportPdf(html, 'Auditoría de seguridad');
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
      <EditorialHeader eyebrow="ADMINISTRACIÓN" title="Auditoría de seguridad" subtitle="Mutaciones de roles, acciones y usuarios." showBack onRefresh={applied ? handleRefresh : undefined} />

      <ScrollView contentContainerStyle={styles.scroll}>
        <SectionDivider index="01" label="Filtros" />
        <Surface style={styles.card}>
          <Text style={styles.fieldHint}>Sin fechas, se muestran los últimos 30 días.</Text>
          <View style={styles.dateRow}>
            <View style={styles.dateField}>
              <SegmentedDateField label="Desde" value={fechaDesdeDraft} onChange={setFechaDesdeDraft} testIDPrefix="auditoria-fecha-desde" />
            </View>
            <View style={styles.dateField}>
              <SegmentedDateField label="Hasta" value={fechaHastaDraft} onChange={setFechaHastaDraft} testIDPrefix="auditoria-fecha-hasta" />
            </View>
          </View>
          {filterError ? (
            <Text style={styles.errorText} accessibilityRole="alert" accessibilityLiveRegion="polite">
              {filterError}
            </Text>
          ) : null}

          <Text style={styles.fieldLabel}>Operación</Text>
          <ChipSelectRow options={REPORT_OPERACIONES_AUDITORIA} selected={operacionDraft} onSelect={setOperacionDraft} />

          <Text style={[styles.fieldLabel, styles.fieldLabelSpaced]}>Objetivo</Text>
          <ChipSelectRow options={REPORT_TARGET_TIPOS} selected={targetTipoDraft} onSelect={setTargetTipoDraft} />

          <View style={styles.fieldLabelSpaced}>
            <FormInput label="Id de objetivo (opcional)" value={targetIdDraft} onChangeText={setTargetIdDraft} placeholder="Ej: ORGANIZADOR/EVENTO_CREAR" />
          </View>

          {puedeListarUsuarios ? (
            <>
              <Text style={styles.fieldLabel}>Actor (opcional)</Text>
              <Pressable
                accessibilityRole="button"
                accessibilityLabel="Elegir actor"
                onPress={() => setActorPickerVisible(true)}
                style={({ pressed }) => [styles.picker, pressed && styles.pickerPressed]}
              >
                <MaterialIcons name="person" size={18} color={colors.ink} />
                <Text style={styles.pickerText} numberOfLines={1}>
                  {selectedActor ? selectedActor.email : 'Cualquier actor'}
                </Text>
                {selectedActor ? (
                  <Pressable accessibilityRole="button" accessibilityLabel="Quitar actor seleccionado" hitSlop={10} onPress={() => setSelectedActor(undefined)}>
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
          <AsyncStateView variant="loading" message="Generando auditoría" />
        ) : error ? (
          <AsyncStateView variant="error" message={error} onRetry={handleRefresh} />
        ) : !report || !applied ? (
          <AsyncStateView variant="empty" icon="fact-check" message="Aplicá filtros para generar la auditoría." hint="Sin fechas, se muestran los últimos 30 días." />
        ) : (
          <>
            <SectionDivider index="02" label={`Auditorías (${report.auditorias.length})`} />
            <Text style={styles.periodoEfectivo}>Período efectivo: {periodoEfectivoLabel}</Text>
            {report.auditorias.length === 0 ? (
              <AsyncStateView variant="empty" icon="fact-check" message="Ninguna auditoría coincide con los filtros aplicados." />
            ) : (
              report.auditorias.map((item, index) => (
                <Surface key={`${item.timestamp}-${index}`} style={styles.auditCard}>
                  <View style={styles.auditHeader}>
                    <Text style={styles.auditOperacion}>{item.operacion}</Text>
                    <Text style={styles.auditFecha}>{formatFechaHora(item.timestamp)}</Text>
                  </View>
                  <Text style={styles.auditMeta}>Actor: {item.actorEmail ?? item.actorUsuarioId}</Text>
                  <Text style={styles.auditMeta}>
                    Objetivo: {item.targetTipo} · {item.targetId}
                  </Text>
                  {item.detalle ? <Text style={styles.auditDetalle}>{item.detalle}</Text> : null}
                </Surface>
              ))
            )}

            <View style={styles.exportRow}>
              <ActionButton label="Generar y compartir PDF" onPress={handleExportPdf} loading={exporting} disabled={exporting} />
            </View>
          </>
        )}
      </ScrollView>

      <Modal visible={actorPickerVisible} animationType="slide" transparent onRequestClose={() => setActorPickerVisible(false)}>
        <View style={styles.backdrop}>
          <Pressable style={StyleSheet.absoluteFill} onPress={() => setActorPickerVisible(false)} />
          <View style={styles.sheet}>
            <View style={styles.sheetHeader}>
              <Text style={styles.sheetTitle}>Elegí un actor</Text>
              <Pressable accessibilityRole="button" accessibilityLabel="Cerrar" onPress={() => setActorPickerVisible(false)} hitSlop={10}>
                <MaterialIcons name="close" size={20} color={colors.ink} />
              </Pressable>
            </View>
            <ScrollView style={styles.sheetList}>
              <Pressable
                style={styles.sheetRow}
                onPress={() => {
                  setSelectedActor(undefined);
                  setActorPickerVisible(false);
                }}
              >
                <Text style={styles.sheetRowText}>Cualquier actor</Text>
              </Pressable>
              {usuariosLoading ? (
                <Text style={styles.sheetHint}>Cargando usuarios…</Text>
              ) : usuarios.length === 0 ? (
                <Text style={styles.sheetHint}>No hay usuarios registrados.</Text>
              ) : (
                usuarios.map((usuario) => (
                  <Pressable
                    key={usuario.usuarioId}
                    style={styles.sheetRow}
                    onPress={() => {
                      setSelectedActor(usuario);
                      setActorPickerVisible(false);
                    }}
                  >
                    <Text style={styles.sheetRowText} numberOfLines={1}>
                      {usuario.email}
                    </Text>
                    <Text style={styles.sheetRowMeta}>{usuario.rolesActivos.join(', ') || 'Sin roles activos'}</Text>
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
  fieldHint: { fontFamily: fonts.regular, fontSize: 12, color: colors.inkSoft, marginBottom: spacing.sm },
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
  periodoEfectivo: { fontFamily: fonts.regular, fontSize: 12, color: colors.inkSoft, marginBottom: spacing.md },
  auditCard: { marginBottom: spacing.sm, paddingVertical: spacing.sm },
  auditHeader: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start', gap: spacing.sm },
  auditOperacion: { fontFamily: fonts.bold, fontSize: 13, color: colors.ink, flexShrink: 1 },
  auditFecha: { fontFamily: fonts.regular, fontSize: 11, color: colors.inkSoft },
  auditMeta: { fontFamily: fonts.regular, fontSize: 12, color: colors.inkSoft, marginTop: 2 },
  auditDetalle: { fontFamily: fonts.regular, fontSize: 12, color: colors.ink, marginTop: spacing.xs, fontStyle: 'italic' },
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
