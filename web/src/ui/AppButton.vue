<script setup lang="ts">
import { computed, type Component } from 'vue'
import { RouterLink, type RouteLocationRaw } from 'vue-router'
import AppIcon from './AppIcon.vue'
import Spinner from './Spinner.vue'

export type ButtonVariant = 'primary' | 'secondary' | 'tonal' | 'ghost' | 'danger'

const props = withDefaults(
  defineProps<{
    variant?: ButtonVariant
    size?: 'sm' | 'md' | 'lg'
    icon?: Component
    /** Only the icon is shown; `label` becomes the accessible name. */
    iconOnly?: boolean
    label?: string
    loading?: boolean
    disabled?: boolean
    block?: boolean
    type?: 'button' | 'submit'
    to?: RouteLocationRaw
    href?: string
  }>(),
  { variant: 'secondary', size: 'md', icon: undefined, iconOnly: false, label: undefined, loading: false, disabled: false, block: false, type: 'button', to: undefined, href: undefined },
)

const tag = computed(() => (props.to ? RouterLink : props.href ? 'a' : 'button'))
const attrs = computed(() =>
  props.to ? { to: props.to } : props.href ? { href: props.href } : { type: props.type, disabled: props.disabled || props.loading },
)
const iconSize = computed(() => (props.size === 'sm' ? 16 : 18))
</script>

<template>
  <component
    :is="tag"
    v-bind="attrs"
    class="button"
    :class="[props.variant, props.size, { block: props.block, 'icon-only': props.iconOnly, disabled: props.disabled }]"
    :aria-label="props.iconOnly ? props.label : undefined"
    :aria-busy="props.loading || undefined"
  >
    <Spinner v-if="props.loading" :size="iconSize" />
    <AppIcon v-else-if="props.icon" :icon="props.icon" :size="iconSize" :stroke="2" />
    <span v-if="!props.iconOnly"><slot>{{ props.label }}</slot></span>
  </component>
</template>

<style scoped>
.button {
  --h: var(--target);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--space-2);
  height: var(--h);
  min-width: var(--h);
  padding: 0 var(--space-4);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  background: var(--fill);
  color: var(--fg);
  font: 600 15px/1 var(--font-sans);
  letter-spacing: 0.005em;
  white-space: nowrap;
  text-decoration: none;
  cursor: pointer;
  transition:
    background var(--transition),
    border-color var(--transition);
}
.button:focus-visible {
  outline: none;
  box-shadow: var(--focus-ring);
}
.sm {
  --h: 36px;
  padding: 0 var(--space-3);
  border-radius: var(--radius-sm);
  font-size: 14px;
}
.lg {
  --h: 52px;
  padding: 0 22px;
  font-size: 16px;
}
.icon-only {
  width: var(--h);
  padding: 0;
}
.block {
  width: 100%;
}
.primary {
  --fill: var(--accent);
  --fg: var(--accent-ink);
  --border: var(--accent);
}
.primary:hover {
  --fill: var(--accent-hover);
  --border: var(--accent-hover);
}
.secondary {
  --fill: var(--surface);
  --fg: var(--ink);
  --border: var(--line-strong);
}
.secondary:hover {
  --fill: var(--surface-2);
}
.tonal {
  --fill: var(--accent-soft);
  --fg: var(--accent-text);
  --border: transparent;
}
.ghost {
  --fill: transparent;
  --fg: var(--ink-2);
  --border: transparent;
}
.ghost:hover {
  --fill: var(--surface-3);
}
.danger {
  --fill: var(--danger-fill);
  --fg: var(--danger-ink);
  --border: var(--danger-fill);
}
.button:disabled,
.button.disabled {
  --fill: var(--surface-3);
  --fg: var(--ink-3);
  --border: transparent;
  cursor: not-allowed;
}
.button[aria-busy='true'] {
  cursor: progress;
}
</style>
