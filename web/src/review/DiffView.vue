<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ diff: string }>()

/** Unified diff lines with a class each: file headers first, so `+++` and `---` do not read as changes. */
const lines = computed(() =>
  props.diff.split('\n').map((text) => ({
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
  <pre class="diff"><span v-for="(line, index) in lines" :key="index" :class="line.kind">{{ line.text }}
</span></pre>
</template>

<style scoped>
.diff {
  overflow-x: auto;
  padding: 0.5rem;
  background: var(--surface);
  border-radius: 8px;
  font-size: 0.85rem;
}

.diff span {
  display: block;
  white-space: pre;
}

.added {
  color: var(--ok);
}

.removed {
  color: var(--error);
}

.meta,
.hunk {
  color: var(--muted);
}
</style>
