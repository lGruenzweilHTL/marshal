<script setup lang="ts">
/** A definition list of labelled values; each item is `{ label, value }` or a slot named after the item's `key`. */
defineProps<{ items: { key: string; label: string; value?: string | number | null; mono?: boolean }[] }>()
</script>

<template>
  <dl class="kv">
    <template v-for="item in items" :key="item.key">
      <dt>{{ item.label }}</dt>
      <dd :class="{ 't-mono': item.mono }" :data-test="item.key">
        <slot :name="item.key">{{ item.value ?? '—' }}</slot>
      </dd>
    </template>
  </dl>
</template>

<style scoped>
.kv {
  display: grid;
  grid-template-columns: minmax(96px, max-content) minmax(0, 1fr);
  gap: 10px var(--space-4);
  align-items: baseline;
}
dt {
  font: var(--text-body-s);
  color: var(--ink-3);
}
dd {
  min-width: 0;
  overflow-wrap: anywhere;
}
/* Monospace and plain values share one line height, so rows keep an even rhythm whatever their value style. */
dd.t-mono {
  line-height: 22px;
}
</style>
