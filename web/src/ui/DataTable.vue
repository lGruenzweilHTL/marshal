<script setup lang="ts" generic="T">
/**
 * A table for desktop list views. Columns name a key and a label; each cell renders the slot `cell-<key>` with the
 * row, or the row's value under that key. Phones get a list instead (views switch with `useDesktop`).
 */
defineProps<{
  columns: { key: string; label: string; align?: 'left' | 'right'; width?: string }[]
  rows: T[]
  rowKey: (row: T) => string
  label: string
  /** Data attribute name set on each row with its key (for links and tests), e.g. `task` gives `data-task`. */
  rowAttribute?: string
}>()
</script>

<template>
  <div class="scroll">
    <table :aria-label="label">
      <thead>
        <tr>
          <th v-for="column in columns" :key="column.key" scope="col" :class="column.align" :style="column.width ? { width: column.width } : undefined">
            {{ column.label }}
          </th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="row in rows" :key="rowKey(row)" v-bind="rowAttribute ? { [`data-${rowAttribute}`]: rowKey(row) } : {}">
          <td v-for="column in columns" :key="column.key" :class="column.align">
            <slot :name="`cell-${column.key}`" :row="row">{{ (row as Record<string, unknown>)[column.key] }}</slot>
          </td>
        </tr>
      </tbody>
      <tfoot v-if="$slots.foot"><slot name="foot" /></tfoot>
    </table>
  </div>
</template>

<style scoped>
.scroll {
  overflow-x: auto;
  border-radius: var(--radius-lg);
  background: var(--surface);
  box-shadow: var(--shadow-1);
}
table {
  width: 100%;
  border-collapse: collapse;
  font: var(--text-body-s);
}
th {
  padding: var(--space-3) var(--space-4);
  border-bottom: 1px solid var(--line);
  color: var(--ink-3);
  font: var(--text-overline);
  letter-spacing: 0.06em;
  text-align: left;
  text-transform: uppercase;
  white-space: nowrap;
}
td {
  padding: var(--space-3) var(--space-4);
  border-bottom: 1px solid var(--line);
  color: var(--ink);
  vertical-align: middle;
}
tbody tr:last-child td {
  border-bottom: 0;
}
tfoot :deep(td) {
  padding: var(--space-3) var(--space-4);
  border-top: 1px solid var(--line-strong);
  font-weight: 600;
}
tfoot :deep(.right) {
  text-align: right;
}
tbody tr:hover {
  background: var(--surface-2);
}
.right {
  text-align: right;
}
</style>
