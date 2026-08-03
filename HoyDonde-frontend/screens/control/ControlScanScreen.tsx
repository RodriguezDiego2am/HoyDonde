import React, { useCallback, useRef, useState } from 'react';
import { ActivityIndicator, StyleSheet, Text, View } from 'react-native';
import { router } from 'expo-router';
import { useIsFocused } from '@react-navigation/native';
import { CameraView, useCameraPermissions } from 'expo-camera';
import * as Linking from 'expo-linking';
import MaterialIcons from '@expo/vector-icons/MaterialIcons';

import { ControlResultView } from '@/components/control/ControlResultView';
import { ActionButton } from '@/components/ui/ActionButton';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { borderWidth, colors, fonts, radii, spacing } from '@/constants/theme';
import { useTicketValidation } from '@/hooks/useTicketValidation';
import { parseTicketQrPayload } from '@/utils/ticketQr';

interface BarcodeScannedEvent {
  data: string;
}

/** Estado de carga con texto explícito: AsyncStateView ('loading') solo muestra el spinner. */
function CameraLoadingState({ message }: { message: string }) {
  return (
    <View style={styles.centerBlock}>
      <ActivityIndicator size="large" color={colors.tomato} />
      <Text style={styles.hint}>{message}</Text>
    </View>
  );
}

/**
 * Escáner QR de Control (Frontend 3, API_Documentation.md §9): solo parsea localmente con
 * parseTicketQrPayload (misma forma que Frontend 1) y delega toda decisión de validez a
 * POST /api/tickets/validate vía useTicketValidation — nunca marca una entrada como válida por
 * el solo hecho de reconocer el QR.
 */
export default function ControlScanScreen() {
  const isFocused = useIsFocused();
  const [permission, requestPermission] = useCameraPermissions();
  const [mountError, setMountError] = useState<string | null>(null);
  const [scanError, setScanError] = useState<string | null>(null);
  const lastDataRef = useRef<string | null>(null);

  const { result, validating, validate, reset } = useTicketValidation();

  const handleBarcodeScanned = useCallback(
    ({ data }: BarcodeScannedEvent) => {
      if (validating || data === lastDataRef.current) return;
      lastDataRef.current = data;

      const payload = parseTicketQrPayload(data);
      if (!payload) {
        setScanError('Ese código no corresponde a una entrada de HoyDonde?.');
        return;
      }
      setScanError(null);
      void validate(payload.ticketId, payload.eventId);
    },
    [validating, validate]
  );

  const handleScanNext = useCallback(() => {
    lastDataRef.current = null;
    setScanError(null);
    setMountError(null);
    reset();
  }, [reset]);

  if (result) {
    return <ControlResultView result={result} onScanNext={handleScanNext} />;
  }

  return (
    <View style={styles.container} testID="control-scan-screen">
      <EditorialHeader eyebrow="CONTROL" title="Escanear entrada" showBack />

      <View style={styles.frameArea}>
        {!permission ? (
          <CameraLoadingState message="Comprobando el permiso de cámara" />
        ) : permission.granted ? (
          !isFocused ? (
            <CameraLoadingState message="Cámara en pausa" />
          ) : (
            <>
              <View style={styles.cameraFrame}>
                {mountError ? (
                  <View style={styles.centerBlock}>
                    <MaterialIcons name="videocam-off" size={40} color={colors.error} />
                    <Text style={styles.errorText}>No se pudo iniciar la cámara.</Text>
                    <Text style={styles.hint}>{mountError}</Text>
                    <View style={styles.actionSpacing}>
                      <ActionButton label="Reintentar" variant="secondary" onPress={() => setMountError(null)} />
                    </View>
                  </View>
                ) : (
                  <>
                    <CameraView
                      testID="control-camera-view"
                      style={StyleSheet.absoluteFillObject}
                      facing="back"
                      barcodeScannerSettings={{ barcodeTypes: ['qr'] }}
                      onBarcodeScanned={validating ? undefined : handleBarcodeScanned}
                      onMountError={(event) => setMountError(event.message)}
                    />
                    <View pointerEvents="none" style={styles.viewfinder} />
                    {validating ? (
                      <View style={styles.validatingOverlay}>
                        <ActivityIndicator color={colors.paper} />
                        <Text style={styles.validatingText}>Validando…</Text>
                      </View>
                    ) : null}
                  </>
                )}
              </View>
              <Text style={styles.instructions}>Encuadrá el código QR de la entrada dentro del marco.</Text>
              {scanError ? <Text style={styles.scanErrorText}>{scanError}</Text> : null}
            </>
          )
        ) : permission.status === 'denied' && !permission.canAskAgain ? (
          <View style={styles.centerBlock}>
            <MaterialIcons name="no-photography" size={40} color={colors.inkSoft} />
            <Text style={styles.errorText}>El permiso de cámara está denegado.</Text>
            <Text style={styles.hint}>Activalo desde los ajustes del sistema para poder escanear entradas.</Text>
            <View style={styles.actionSpacing}>
              <ActionButton label="Abrir configuración" onPress={() => Linking.openSettings()} />
            </View>
          </View>
        ) : (
          <View style={styles.centerBlock}>
            <MaterialIcons name="qr-code-scanner" size={40} color={colors.inkSoft} />
            <Text style={styles.errorText}>
              {permission.status === 'denied'
                ? 'El permiso de cámara fue denegado.'
                : 'HoyDonde? necesita la cámara para escanear entradas.'}
            </Text>
            <View style={styles.actionSpacing}>
              <ActionButton label="Solicitar permiso" onPress={() => requestPermission()} />
            </View>
          </View>
        )}
      </View>

      <View style={styles.manualLinkRow}>
        <ActionButton
          label="Ingresar código manualmente"
          variant="ghost"
          onPress={() => router.replace('/control/manual')}
        />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.paper,
  },
  frameArea: {
    flex: 1,
    paddingHorizontal: spacing.lg,
    paddingTop: spacing.lg,
    alignItems: 'center',
  },
  cameraFrame: {
    width: '100%',
    aspectRatio: 1,
    borderWidth: borderWidth.thick,
    borderColor: colors.ink,
    borderRadius: radii.md,
    overflow: 'hidden',
    backgroundColor: colors.ink,
  },
  viewfinder: {
    position: 'absolute',
    top: '18%',
    left: '18%',
    right: '18%',
    bottom: '18%',
    borderWidth: 2,
    borderColor: colors.lime,
    borderRadius: radii.md,
  },
  validatingOverlay: {
    ...StyleSheet.absoluteFillObject,
    backgroundColor: 'rgba(23, 21, 18, 0.72)',
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
  },
  validatingText: {
    fontFamily: fonts.bold,
    fontSize: 14,
    letterSpacing: 0.6,
    color: colors.paper,
    textTransform: 'uppercase',
  },
  instructions: {
    fontFamily: fonts.regular,
    fontSize: 13,
    color: colors.inkSoft,
    textAlign: 'center',
    marginTop: spacing.md,
  },
  scanErrorText: {
    fontFamily: fonts.medium,
    fontSize: 13,
    color: colors.error,
    textAlign: 'center',
    marginTop: spacing.sm,
  },
  centerBlock: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.sm,
    paddingHorizontal: spacing.lg,
  },
  errorText: {
    fontFamily: fonts.bold,
    fontSize: 15,
    color: colors.ink,
    textAlign: 'center',
  },
  hint: {
    fontFamily: fonts.regular,
    fontSize: 13,
    color: colors.inkSoft,
    textAlign: 'center',
  },
  actionSpacing: {
    marginTop: spacing.md,
    alignSelf: 'stretch',
  },
  manualLinkRow: {
    alignItems: 'center',
    paddingBottom: spacing.lg,
  },
});
