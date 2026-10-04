<script setup lang="ts">
import { X } from 'lucide-vue-next'
import { nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import AppIcon from './AppIcon.vue'

/**
 * Modal surface used by Sheet (bottom sheet on phones) and Dialog (centred). Traps Tab inside, closes on Escape and
 * on the scrim, returns focus to the element that opened it.
 */
const props = defineProps<{ open: boolean; title: string; kind: 'sheet' | 'dialog'; description?: string }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()
const panel = ref<HTMLElement | null>(null)
let opener: HTMLElement | null = null

function focusables(): HTMLElement[] {
  return [...(panel.value?.querySelectorAll<HTMLElement>('a[href], button:not([disabled]), input:not([disabled]), select, textarea, [tabindex]:not([tabindex="-1"])') ?? [])]
}

function onKey(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    event.preventDefault()
    emit('close')
  } else if (event.key === 'Tab') {
    const items = focusables()
    if (items.length === 0) return
    const first = items[0]!
    const last = items[items.length - 1]!
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault()
      last.focus()
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault()
      first.focus()
    }
  }
}

watch(
  () => props.open,
  async (open) => {
    if (open) {
      opener = document.activeElement as HTMLElement | null
      await nextTick()
      ;(focusables()[1] ?? focusables()[0] ?? panel.value)?.focus()
    } else {
      opener?.focus?.()
    }
  },
  { immediate: true },
)
onBeforeUnmount(() => opener?.focus?.())
</script>

<template>
  <Teleport to="body">
    <div v-if="open" class="overlay" :class="kind" @keydown="onKey">
      <div class="scrim" data-test="scrim" @click="emit('close')" />
      <div ref="panel" class="panel" role="dialog" aria-modal="true" :aria-label="title" tabindex="-1">
        <span v-if="kind === 'sheet'" class="grip" aria-hidden="true" />
        <header class="head">
          <span class="heading">
            <h2 class="t-title-2">{{ title }}</h2>
            <p v-if="description" class="description">{{ description }}</p>
          </span>
          <button type="button" class="close" data-test="close" :aria-label="t('actions.close')" @click="emit('close')"><AppIcon :icon="X" /></button>
        </header>
        <div class="content"><slot /></div>
        <footer v-if="$slots.actions" class="actions"><slot name="actions" /></footer>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
.overlay {
  position: fixed;
  inset: 0;
  z-index: 50;
  display: flex;
  justify-content: center;
}
.dialog {
  align-items: center;
  padding: var(--space-4);
}
.sheet {
  align-items: flex-end;
}
.scrim {
  position: absolute;
  inset: 0;
  background: var(--scrim);
}
.panel {
  position: relative;
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  width: 100%;
  max-height: 92dvh;
  overflow: auto;
  padding: var(--space-5);
  background: var(--surface);
  box-shadow: var(--shadow-3);
  outline: none;
}
.dialog .panel {
  max-width: 480px;
  border-radius: var(--radius-xl);
}
.sheet .panel {
  max-width: 560px;
  padding-top: var(--space-2);
  padding-bottom: calc(var(--space-5) + env(safe-area-inset-bottom));
  border-radius: var(--radius-xl) var(--radius-xl) 0 0;
}
.grip {
  align-self: center;
  width: 36px;
  height: 5px;
  border-radius: var(--radius-full);
  background: var(--line-strong);
}
.head {
  display: flex;
  align-items: flex-start;
  gap: var(--space-3);
}
.heading {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  flex-grow: 1;
}
.description {
  color: var(--ink-2);
}
.close {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--target);
  height: var(--target);
  margin: -6px -6px 0 0;
  border: 0;
  border-radius: var(--radius-full);
  background: transparent;
  color: var(--ink-2);
  cursor: pointer;
}
.close:hover {
  background: var(--surface-3);
}
.actions {
  display: flex;
  gap: 10px;
  justify-content: flex-end;
  flex-wrap: wrap;
}
.sheet .actions {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(0, 1fr));
}
</style>
