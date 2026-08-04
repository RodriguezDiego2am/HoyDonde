import React, { useMemo, useState } from 'react';
import { StyleSheet, Text, View } from 'react-native';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ActionButton } from '@/components/ui/ActionButton';
import { Surface } from '@/components/ui/Surface';
import { colors, fonts, spacing } from '@/constants/theme';
import type { CompraResponse } from '@/services/APIService';
import { formatFechaHora } from '@/utils/format';
import {
  PurchaseReceiptInconsistencyError,
  assertCompraConsistente,
  buildPurchaseReceiptHtml,
  buildReceiptFileName,
  formatPrecioOGratis,
  generateAndSharePurchaseReceiptPdf,
  groupPurchaseTickets,
} from '@/utils/purchaseReceiptPdf';

interface PurchaseReceiptPanelProps {
  /** Respuesta completa de POST /api/tickets/buy — única fuente del comprobante (evento, ubicación, fechas, cantidad e importe total). */
  compra: CompraResponse;
  onGoToTickets: () => void;
}

/**
 * Resumen posterior a una compra simulada exitosa + botón para descargar el comprobante PDF
 * (docs/api-mvp-plan.md §14). Un fallo al generar/compartir el PDF solo afecta el estado local de
 * este panel (error/info): nunca vuelve a llamar ticketService.buy, nunca repite ni revierte la
 * compra ya confirmada.
 */
export function PurchaseReceiptPanel({ compra, onGoToTickets }: PurchaseReceiptPanelProps) {
  const [generating, setGenerating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  const grupos = useMemo(() => groupPurchaseTickets(compra.tickets), [compra.tickets]);

  // La compra ya se realizó y sigue siendo válida aunque el comprobante no pueda generarse: si la
  // suma de los Ticket no coincide con Compra.ImporteTotal/CantidadEntradas, nunca se arma un PDF
  // engañoso — se bloquea la descarga con un aviso seguro en vez de intentarlo.
  const isConsistent = useMemo(() => {
    try {
      assertCompraConsistente(compra);
      return true;
    } catch {
      return false;
    }
  }, [compra]);

  const handleDownload = async () => {
    if (generating || !isConsistent) return;
    setGenerating(true);
    setError(null);
    setInfo(null);
    try {
      const html = buildPurchaseReceiptHtml(compra);
      const fileName = buildReceiptFileName(compra.fechaCompra);
      const result = await generateAndSharePurchaseReceiptPdf(html, fileName);
      if (!result.shared) {
        setInfo(
          'El comprobante se generó, pero este dispositivo no pudo abrir el selector para compartir. Quedó guardado en el almacenamiento local del dispositivo.'
        );
      }
    } catch (err) {
      setError(
        err instanceof PurchaseReceiptInconsistencyError
          ? 'Los datos de la compra no son consistentes: no se generó el comprobante. Tu compra sigue siendo válida.'
          : 'No se pudo generar el comprobante. Volvé a intentarlo.'
      );
    } finally {
      setGenerating(false);
    }
  };

  return (
    <Surface style={styles.container}>
      <Text style={styles.eyebrow}>RESUMEN DE LA COMPRA</Text>
      <Text style={styles.eventName}>{compra.eventoNombre}</Text>

      <View style={styles.metaRow}>
        <MaterialIcons name="place" size={14} color={colors.inkSoft} />
        <Text style={styles.metaText}>{compra.ubicacion}</Text>
      </View>
      <View style={styles.metaRow}>
        <MaterialIcons name="event" size={14} color={colors.inkSoft} />
        <Text style={styles.metaText}>{formatFechaHora(compra.fechaInicio)}</Text>
      </View>
      <View style={styles.metaRow}>
        <MaterialIcons name="receipt-long" size={14} color={colors.inkSoft} />
        <Text style={styles.metaText}>Comprada el {formatFechaHora(compra.fechaCompra)}</Text>
      </View>

      <View style={styles.divider} />

      {grupos.map((g) => (
        <View key={g.ticketTypeId} style={styles.groupRow}>
          <Text style={styles.groupLabel}>
            {g.cantidad} × {g.ticketTypeNombre}
          </Text>
          <Text style={styles.groupSub}>
            {formatPrecioOGratis(g.precioUnitario)} c/u — Subtotal {formatPrecioOGratis(g.subtotal)}
          </Text>
        </View>
      ))}

      <View style={styles.totalRow}>
        <Text style={styles.totalLabel}>
          Total: {compra.cantidadEntradas} {compra.cantidadEntradas === 1 ? 'entrada' : 'entradas'}
        </Text>
        <Text style={styles.totalValue}>{formatPrecioOGratis(compra.importeTotal)}</Text>
      </View>

      {!isConsistent ? (
        <Text style={styles.errorText}>
          No pudimos generar el comprobante: los datos de la compra no son consistentes. Tu compra sigue siendo
          válida — contactá a soporte si necesitás un comprobante.
        </Text>
      ) : (
        <>
          {error ? <Text style={styles.errorText}>{error}</Text> : null}
          {info ? <Text style={styles.infoText}>{info}</Text> : null}

          <View style={styles.buttonSpacing}>
            <ActionButton
              label={error ? 'Reintentar descarga' : 'Descargar comprobante PDF'}
              onPress={handleDownload}
              loading={generating}
            />
          </View>
        </>
      )}
      <View style={styles.buttonSpacing}>
        <ActionButton label="Ver mis entradas" variant="secondary" onPress={onGoToTickets} />
      </View>
    </Surface>
  );
}

const styles = StyleSheet.create({
  container: {
    gap: spacing.xs,
  },
  eyebrow: {
    fontFamily: fonts.bold,
    fontSize: 11,
    letterSpacing: 1.2,
    color: colors.tomato,
  },
  eventName: {
    fontFamily: fonts.bold,
    fontSize: 17,
    color: colors.ink,
    marginTop: 2,
  },
  metaRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: 4,
    marginTop: spacing.xs,
  },
  metaText: {
    fontFamily: fonts.regular,
    fontSize: 13,
    color: colors.inkSoft,
  },
  divider: {
    height: 1,
    backgroundColor: colors.ink,
    opacity: 0.2,
    marginVertical: spacing.md,
  },
  groupRow: {
    marginBottom: spacing.sm,
  },
  groupLabel: {
    fontFamily: fonts.semiBold,
    fontSize: 14,
    color: colors.ink,
  },
  groupSub: {
    fontFamily: fonts.regular,
    fontSize: 12,
    color: colors.inkSoft,
    marginTop: 2,
  },
  totalRow: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginTop: spacing.sm,
    paddingTop: spacing.sm,
    borderTopWidth: 1,
    borderTopColor: colors.ink,
  },
  totalLabel: {
    fontFamily: fonts.bold,
    fontSize: 15,
    color: colors.ink,
  },
  totalValue: {
    fontFamily: fonts.black,
    fontSize: 18,
    color: colors.tomato,
  },
  errorText: {
    fontFamily: fonts.medium,
    fontSize: 13,
    color: colors.error,
    marginTop: spacing.md,
  },
  infoText: {
    fontFamily: fonts.medium,
    fontSize: 13,
    color: colors.cobalt,
    marginTop: spacing.md,
  },
  buttonSpacing: {
    marginTop: spacing.md,
  },
});
