<script setup lang="ts">
import { computed } from 'vue'
import { highlightDiff } from '../highlighting/diff'
import { lineHtml } from '../highlighting/render'
import '../highlighting/syntax.css'

/** A unified diff with each file's code highlighted (spec code-highlighting); added and removed rows tinted. */
const props = defineProps<{ diff: string }>()

const rows = computed(() => {
  const lines = props.diff.replace(/\r\n/g, '\n').replace(/\n$/, '').split('\n')
  return highlightDiff(lines).map((row, index) => {
    const text = lines[index]!
    const marked = row.kind === 'added' || row.kind === 'removed'
    return {
      kind: row.kind,
      marker: marked ? text.slice(0, 1) : '',
      html: marked ? lineHtml(text.slice(1), row.spans.map(([start, length, token]) => [start - 1, length, token] as const)) : lineHtml(text, row.spans),
    }
  })
})
</script>

<template>
  <!-- eslint-disable vue/no-v-html -- highlighter output: the text is escaped -->
  <pre
    class="diff"
    data-test="diff"
  ><span v-for="(row, index) in rows" :key="index" :class="row.kind"><span v-if="row.marker" class="marker">{{ row.marker }}</span><span v-html="row.html || ' '"></span></span></pre>
</template>

<style scoped>
.diff {
  overflow-x: auto;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  background: var(--surface);
  font: 450 13px/21px var(--font-mono);
}
.diff > span {
  display: block;
  padding: 1px 14px;
  white-space: pre;
  color: var(--ink);
}
.meta {
  color: var(--ink-3);
}
.diff > .hunk {
  background: var(--info-soft);
  color: var(--info);
}
.diff > .added {
  background: var(--success-soft);
}
.diff > .removed {
  background: var(--danger-soft);
}
.added .marker {
  color: var(--success);
}
.removed .marker {
  color: var(--danger);
}
</style>
