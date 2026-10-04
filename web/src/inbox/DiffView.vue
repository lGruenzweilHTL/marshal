<script setup lang="ts">
import { computed } from 'vue'

/** A unified diff, one row per line; file headers first, so `+++` and `---` do not read as changes. */
const props = defineProps<{ diff: string }>()

const lines = computed(() =>
  props.diff
    .replace(/\n$/, '')
    .split('\n')
    .map((text) => ({
      text,
      kind:
        text.startsWith('+++') || text.startsWith('---') || text.startsWith('diff ') || text.startsWith('index ')
          ? 'meta'
          : text.startsWith('@@')
            ? 'hunk'
            : text.startsWith('+')
              ? 'added'
              : text.startsWith('-')
                ? 'removed'
                : 'context',
    })),
)
</script>

<template>
  <pre class="diff"><span v-for="(line, index) in lines" :key="index" :class="line.kind">{{ line.text || ' ' }}</span></pre>
</template>

<style scoped>
.diff {
  overflow-x: auto;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  background: var(--surface);
  font: 450 13px/21px var(--font-mono);
}
.diff span {
  display: block;
  padding: 1px 14px;
  white-space: pre;
  color: var(--ink-2);
}
.meta {
  color: var(--ink-3);
}
.hunk {
  background: var(--info-soft);
  color: var(--info);
}
.diff .added {
  background: var(--success-soft);
  color: var(--success);
}
.diff .removed {
  background: var(--danger-soft);
  color: var(--danger);
}
</style>
