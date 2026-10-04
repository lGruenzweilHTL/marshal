<script setup lang="ts">
import { AlertCircle, ChevronDown } from 'lucide-vue-next'
import { computed, useId } from 'vue'
import AppIcon from './AppIcon.vue'

/**
 * A labelled form control with hint and error. `kind` picks the control; options of a select come from the default
 * slot (`<option>`). The value is bound with `v-model`.
 */
const props = withDefaults(
  defineProps<{
    label: string
    kind?: 'text' | 'password' | 'email' | 'number' | 'search' | 'time' | 'date' | 'datetime-local' | 'url' | 'select' | 'textarea'
    hint?: string
    error?: string | null
    placeholder?: string
    disabled?: boolean
    required?: boolean
    mono?: boolean
    rows?: number
    autocomplete?: string
    name?: string
    /** Hides the label visually (it stays the accessible name). */
    hideLabel?: boolean
  }>(),
  {
    kind: 'text',
    hint: undefined,
    error: null,
    placeholder: undefined,
    disabled: false,
    required: false,
    mono: false,
    rows: 4,
    autocomplete: undefined,
    name: undefined,
    hideLabel: false,
  },
)
const model = defineModel<string | number | null>()
const id = useId()
const describedBy = computed(() => [props.error ? `${id}-error` : null, props.hint ? `${id}-hint` : null].filter(Boolean).join(' ') || undefined)
</script>

<template>
  <div class="field" :class="{ invalid: !!error, disabled, mono }">
    <label :for="id" class="label" :class="{ 'sr-only': hideLabel }">{{ label }}</label>
    <span class="control">
      <textarea
        v-if="kind === 'textarea'"
        :id="id"
        v-model="model"
        :name="name"
        :rows="rows"
        :placeholder="placeholder"
        :disabled="disabled"
        :required="required"
        :aria-invalid="!!error || undefined"
        :aria-describedby="describedBy"
      />
      <template v-else-if="kind === 'select'">
        <select :id="id" v-model="model" :name="name" :disabled="disabled" :required="required" :aria-invalid="!!error || undefined" :aria-describedby="describedBy">
          <slot />
        </select>
        <AppIcon :icon="ChevronDown" :size="18" class="trail" />
      </template>
      <input
        v-else
        :id="id"
        v-model="model"
        :name="name"
        :type="kind"
        :placeholder="placeholder"
        :disabled="disabled"
        :required="required"
        :autocomplete="autocomplete"
        :aria-invalid="!!error || undefined"
        :aria-describedby="describedBy"
      />
    </span>
    <span v-if="error" :id="`${id}-error`" class="error" role="alert"><AppIcon :icon="AlertCircle" :size="16" />{{ error }}</span>
    <span v-if="hint" :id="`${id}-hint`" class="hint">{{ hint }}</span>
  </div>
</template>

<style scoped>
.field {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}
.label {
  font: var(--text-label);
  color: var(--ink);
}
.control {
  position: relative;
  display: flex;
  align-items: center;
}
input,
select,
textarea {
  width: 100%;
  min-height: var(--target);
  padding: 0 14px;
  border: 1px solid var(--line-strong);
  border-radius: var(--radius-md);
  background: var(--surface);
  color: var(--ink);
  font: var(--text-body);
  outline: none;
  transition:
    border-color var(--transition),
    box-shadow var(--transition);
}
.mono input,
.mono textarea {
  font: var(--text-mono);
}
textarea {
  min-height: 104px;
  padding: var(--space-3) 14px;
  resize: vertical;
}
select {
  appearance: none;
  padding-right: var(--space-10);
}
input::placeholder,
textarea::placeholder {
  color: var(--ink-3);
}
input:focus-visible,
select:focus-visible,
textarea:focus-visible {
  border-color: var(--focus);
  box-shadow:
    0 0 0 1px var(--focus),
    0 0 0 4px var(--info-soft);
}
.invalid input,
.invalid select,
.invalid textarea {
  border-color: var(--danger);
  box-shadow: 0 0 0 1px var(--danger);
}
.disabled input,
.disabled select,
.disabled textarea {
  background: var(--surface-2);
  color: var(--ink-3);
  cursor: not-allowed;
}
.trail {
  position: absolute;
  right: var(--space-3);
  color: var(--ink-3);
  pointer-events: none;
}
.error {
  display: flex;
  gap: 6px;
  align-items: flex-start;
  font: var(--text-body-s);
  color: var(--danger);
}
.hint {
  font: var(--text-body-s);
  color: var(--ink-3);
}
</style>
