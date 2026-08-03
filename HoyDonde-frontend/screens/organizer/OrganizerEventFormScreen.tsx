import React, { useCallback, useEffect, useState } from 'react';
import { KeyboardAvoidingView, Platform, Pressable, ScrollView, StyleSheet, Text, View } from 'react-native';
import { router, useLocalSearchParams } from 'expo-router';
import type { Href } from 'expo-router';

import { ActionButton } from '@/components/ui/ActionButton';
import { AsyncStateView } from '@/components/ui/AsyncStateView';
import { EditorialHeader } from '@/components/ui/EditorialHeader';
import { SectionDivider } from '@/components/ui/SectionDivider';
import {
  TicketGroupsField,
  createEmptyTicketGroupDraft,
} from '@/components/forms/TicketGroupsField';
import type { TicketGroupDraft, TicketGroupFieldErrors } from '@/components/forms/TicketGroupsField';
import { SegmentedDateTimeField } from '@/components/forms/SegmentedDateTimeField';
import { colors, fonts, spacing } from '@/constants/theme';
import { ApiError, EventCategory, EventWriteRequest, eventService } from '@/services/APIService';
import { parseLocalDateTime, splitIsoToLocalParts, toUtcIso } from '@/utils/datetime';
import FormInput from '@/components/FormInput';

const CATEGORIAS: { value: EventCategory; label: string }[] = [
  { value: 'Musica', label: 'Música' },
  { value: 'Deportes', label: 'Deportes' },
  { value: 'Tecnologia', label: 'Tecnología' },
  { value: 'Arte', label: 'Arte' },
  { value: 'Otros', label: 'Otros' },
];

interface FormErrors {
  nombre?: string;
  ubicacion?: string;
  fechaInicio?: string;
  fechaFin?: string;
}

function loadErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 404) return 'El evento ya no existe.';
    if (error.isForbidden) return 'No tenés permiso para editar este evento.';
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    return error.message || 'No se pudo cargar el evento.';
  }
  return 'No se pudo cargar el evento. Verificá tu conexión.';
}

function submitErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    switch (error.code) {
      case 'VALIDATION_ERROR':
      case 'EVENT_VALIDATION_ERROR': {
        const first = error.errors ? Object.values(error.errors).flat()[0] : undefined;
        return first ?? error.message;
      }
      case 'EVENT_NOT_EDITABLE':
        return 'Este evento ya no está en Borrador: no se puede editar.';
      case 'EVENT_NOT_FOUND':
        return 'El evento ya no existe.';
      default:
        break;
    }
    if (error.isUnauthorized) return 'Tu sesión expiró. Iniciá sesión de nuevo.';
    if (error.isForbidden) return 'No tenés permiso para hacer esto sobre este evento.';
    return error.message || 'No se pudo guardar el evento.';
  }
  return 'No se pudo guardar el evento. Verificá tu conexión.';
}

/**
 * Crear (/organizer/new) y editar (/organizer/[id]/edit) comparten esta única pantalla: el modo
 * se deriva de la presencia de `id` en la ruta, no de una prop. Edición solo permitida en
 * Borrador (CLAUDE.md "Event lifecycle"); TicketGroups siempre reemplaza la colección completa.
 */
export default function OrganizerEventFormScreen() {
  const { id } = useLocalSearchParams<{ id?: string }>();
  const mode: 'create' | 'edit' = id ? 'edit' : 'create';

  const [loadingEvent, setLoadingEvent] = useState(mode === 'edit');
  const [loadError, setLoadError] = useState<string | null>(null);
  const [notEditable, setNotEditable] = useState(false);

  const [nombre, setNombre] = useState('');
  const [descripcion, setDescripcion] = useState('');
  const [ubicacion, setUbicacion] = useState('');
  const [categoria, setCategoria] = useState<EventCategory>('Musica');
  const [fechaInicioDate, setFechaInicioDate] = useState('');
  const [fechaInicioTime, setFechaInicioTime] = useState('');
  const [fechaFinDate, setFechaFinDate] = useState('');
  const [fechaFinTime, setFechaFinTime] = useState('');
  const [ticketGroups, setTicketGroups] = useState<TicketGroupDraft[]>(
    mode === 'create' ? [createEmptyTicketGroupDraft()] : []
  );

  const [errors, setErrors] = useState<FormErrors>({});
  const [ticketGroupErrors, setTicketGroupErrors] = useState<(TicketGroupFieldErrors | undefined)[]>([]);
  const [ticketGroupsError, setTicketGroupsError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const loadExisting = useCallback(async () => {
    if (mode !== 'edit' || !id) return;
    setLoadingEvent(true);
    setLoadError(null);
    try {
      const data = await eventService.getOwnedById(id);
      if (data.estado !== 'Borrador') {
        setNotEditable(true);
      } else {
        setNotEditable(false);
        setNombre(data.nombre);
        setDescripcion(data.descripcion);
        setUbicacion(data.ubicacion);
        setCategoria(data.categoria as EventCategory);

        const inicio = splitIsoToLocalParts(data.fechaInicio);
        setFechaInicioDate(inicio.date);
        setFechaInicioTime(inicio.time);

        const fin = splitIsoToLocalParts(data.fechaFin);
        setFechaFinDate(fin.date);
        setFechaFinTime(fin.time);

        setTicketGroups(
          data.ticketGroups.map((tg) => ({
            key: `existing-${tg.id}`,
            nombre: tg.nombre,
            precio: String(tg.precio),
            cantidadDisponible: String(tg.cantidadDisponible),
          }))
        );
      }
    } catch (err) {
      setLoadError(loadErrorMessage(err));
    } finally {
      setLoadingEvent(false);
    }
  }, [mode, id]);

  useEffect(() => {
    loadExisting();
  }, [loadExisting]);

  const validate = (): { inicio: Date; fin: Date } | null => {
    const newErrors: FormErrors = {};

    if (!nombre.trim()) newErrors.nombre = 'El nombre del evento es obligatorio.';
    if (!ubicacion.trim()) newErrors.ubicacion = 'La ubicación del evento es obligatoria.';

    const inicio = parseLocalDateTime(fechaInicioDate, fechaInicioTime);
    const fin = parseLocalDateTime(fechaFinDate, fechaFinTime);

    if (!inicio) {
      newErrors.fechaInicio = 'Ingresá una fecha y hora de inicio válidas (DD/MM/AAAA y HH:MM).';
    } else if (inicio.getTime() <= Date.now()) {
      newErrors.fechaInicio = 'La fecha de inicio debe ser futura.';
    }

    if (!fin) {
      newErrors.fechaFin = 'Ingresá una fecha y hora de fin válidas (DD/MM/AAAA y HH:MM).';
    } else if (inicio && fin.getTime() <= inicio.getTime()) {
      newErrors.fechaFin = 'La fecha de fin debe ser posterior a la fecha de inicio.';
    }

    const newTicketGroupErrors: (TicketGroupFieldErrors | undefined)[] = ticketGroups.map((tg) => {
      const rowErrors: TicketGroupFieldErrors = {};
      if (!tg.nombre.trim()) rowErrors.nombre = 'El nombre del tipo de ticket es obligatorio.';

      const precio = Number(tg.precio);
      if (tg.precio.trim() === '' || Number.isNaN(precio) || precio < 0) {
        rowErrors.precio = 'El precio no puede ser negativo.';
      }

      const cantidad = Number(tg.cantidadDisponible);
      if (tg.cantidadDisponible.trim() === '' || !Number.isInteger(cantidad) || cantidad < 1) {
        rowErrors.cantidadDisponible = 'La cantidad disponible debe ser al menos 1.';
      }

      return Object.keys(rowErrors).length > 0 ? rowErrors : undefined;
    });
    const hasTicketGroupErrors = newTicketGroupErrors.some(Boolean);

    // Solo la creación exige al menos un tipo de ticket (EventCreateRequest.TicketGroups
    // tiene [MinLength(1)]; EventUpdateRequest no — API_Documentation.md §7).
    const generalTicketGroupsError =
      mode === 'create' && ticketGroups.length === 0
        ? 'El evento debe tener al menos un tipo de ticket.'
        : null;

    setErrors(newErrors);
    setTicketGroupErrors(newTicketGroupErrors);
    setTicketGroupsError(generalTicketGroupsError);

    const isValid = Object.keys(newErrors).length === 0 && !hasTicketGroupErrors && !generalTicketGroupsError;
    return isValid && inicio && fin ? { inicio, fin } : null;
  };

  const handleSubmit = async () => {
    setFormError(null);
    if (submitting) return;

    const parsed = validate();
    if (!parsed) return;

    const payload: EventWriteRequest = {
      nombre: nombre.trim(),
      descripcion: descripcion.trim(),
      ubicacion: ubicacion.trim(),
      categoria,
      fechaInicio: toUtcIso(parsed.inicio),
      fechaFin: toUtcIso(parsed.fin),
      ticketGroups: ticketGroups.map((tg) => ({
        nombre: tg.nombre.trim(),
        precio: Number(tg.precio),
        cantidadDisponible: Number(tg.cantidadDisponible),
      })),
    };

    setSubmitting(true);
    try {
      if (mode === 'create') {
        const created = await eventService.create(payload);
        router.replace({ pathname: '/organizer/[id]', params: { id: created.id } } as unknown as Href);
      } else if (id) {
        await eventService.update(id, payload);
        router.replace({ pathname: '/organizer/[id]', params: { id } } as unknown as Href);
      }
    } catch (err) {
      setFormError(submitErrorMessage(err));
    } finally {
      setSubmitting(false);
    }
  };

  if (mode === 'edit' && loadingEvent) {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="ORGANIZACIÓN" title="Editar evento" showBack />
        <AsyncStateView variant="loading" message="Cargando" />
      </View>
    );
  }

  if (mode === 'edit' && loadError) {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="ORGANIZACIÓN" title="Editar evento" showBack />
        <AsyncStateView variant="error" message={loadError} onRetry={loadExisting} />
      </View>
    );
  }

  if (mode === 'edit' && notEditable) {
    return (
      <View style={styles.container}>
        <EditorialHeader eyebrow="ORGANIZACIÓN" title="No editable" showBack />
        <AsyncStateView variant="error" message="Este evento ya no está en Borrador: no se puede editar." />
      </View>
    );
  }

  return (
    <View style={styles.container}>
      <EditorialHeader eyebrow="ORGANIZACIÓN" title={mode === 'create' ? 'Nuevo evento' : 'Editar evento'} showBack />
      <KeyboardAvoidingView style={styles.flex} behavior={Platform.OS === 'ios' ? 'padding' : undefined}>
        <ScrollView contentContainerStyle={styles.scroll} keyboardShouldPersistTaps="handled">
          <SectionDivider index="01" label="Datos generales" style={styles.firstDivider} />

          <FormInput label="Nombre" value={nombre} onChangeText={setNombre} placeholder="Nombre del evento" error={errors.nombre} />
          <FormInput
            label="Descripción"
            value={descripcion}
            onChangeText={setDescripcion}
            placeholder="Descripción (opcional)"
          />
          <FormInput label="Ubicación" value={ubicacion} onChangeText={setUbicacion} placeholder="Dirección o lugar" error={errors.ubicacion} />

          <Text style={styles.chipLabel}>Categoría</Text>
          <View style={styles.chipRow}>
            {CATEGORIAS.map((cat) => {
              const active = categoria === cat.value;
              return (
                <Pressable
                  key={cat.value}
                  accessibilityRole="button"
                  accessibilityState={{ selected: active }}
                  accessibilityLabel={`Categoría ${cat.label}`}
                  onPress={() => setCategoria(cat.value)}
                  style={[styles.chip, active && styles.chipActive]}
                >
                  <Text style={[styles.chipText, active && styles.chipTextActive]}>{cat.label}</Text>
                </Pressable>
              );
            })}
          </View>

          <SectionDivider index="02" label="Fecha y hora" />
          <SegmentedDateTimeField
            label="Inicio"
            dateValue={fechaInicioDate}
            timeValue={fechaInicioTime}
            onChangeDate={setFechaInicioDate}
            onChangeTime={setFechaInicioTime}
            error={errors.fechaInicio}
            testIDPrefix="fecha-inicio"
          />
          <SegmentedDateTimeField
            label="Fin"
            dateValue={fechaFinDate}
            timeValue={fechaFinTime}
            onChangeDate={setFechaFinDate}
            onChangeTime={setFechaFinTime}
            error={errors.fechaFin}
            testIDPrefix="fecha-fin"
          />

          <SectionDivider index="03" label="Entradas" />
          {ticketGroupsError ? <Text style={styles.fieldError}>{ticketGroupsError}</Text> : null}
          <TicketGroupsField value={ticketGroups} onChange={setTicketGroups} errors={ticketGroupErrors} />

          {formError ? <Text style={styles.formError}>{formError}</Text> : null}

          <View style={styles.buttonSpacing}>
            <ActionButton
              label={mode === 'create' ? 'Crear evento' : 'Guardar cambios'}
              onPress={handleSubmit}
              loading={submitting}
            />
          </View>
        </ScrollView>
      </KeyboardAvoidingView>
    </View>
  );
}

const styles = StyleSheet.create({
  container: {
    flex: 1,
    backgroundColor: colors.paper,
  },
  flex: {
    flex: 1,
  },
  scroll: {
    padding: spacing.lg,
    paddingBottom: spacing.xxl,
  },
  firstDivider: {
    marginTop: 0,
  },
  chipLabel: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 0.8,
    textTransform: 'uppercase',
    color: colors.inkSoft,
    marginBottom: spacing.xs,
  },
  chipRow: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.sm,
    marginBottom: spacing.md,
  },
  chip: {
    borderWidth: 1,
    borderColor: colors.ink,
    paddingVertical: 6,
    paddingHorizontal: 12,
  },
  chipActive: {
    backgroundColor: colors.ink,
  },
  chipText: {
    fontFamily: fonts.bold,
    fontSize: 12,
    letterSpacing: 0.5,
    color: colors.ink,
    textTransform: 'uppercase',
  },
  chipTextActive: {
    color: colors.paper,
  },
  fieldError: {
    fontFamily: fonts.medium,
    color: colors.error,
    fontSize: 13,
    marginTop: -spacing.sm,
    marginBottom: spacing.sm,
  },
  formError: {
    fontFamily: fonts.medium,
    color: colors.error,
    marginTop: spacing.md,
  },
  buttonSpacing: {
    marginTop: spacing.lg,
  },
});
