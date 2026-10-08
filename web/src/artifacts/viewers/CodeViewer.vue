<script setup lang="ts">
import { computed } from 'vue'
import { highlightLines, languageOf } from './highlight'

/** Source text with syntax highlighting (by extension) and line numbers. */
const props = defineProps<{ name: string; text: string }>()
const lines = computed(() => highlightLines(props.text, languageOf(props.name)))
</script>

<template>
  <div class="code" data-test="code" :data-language="languageOf(name)">
    <table>
      <tbody>
        <tr v-for="(line, index) in lines" :key="index">
          <td class="number" data-test="line-number">{{ index + 1 }}</td>
          <!-- eslint-disable-next-line vue/no-v-html -- highlight.js output: the text is escaped -->
          <td class="line" v-html="line || ' '"></td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.code {
  overflow-x: auto;
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  background: var(--surface);
  font: 450 13px/21px var(--font-mono);
}
table {
  border-collapse: collapse;
}
.number {
  padding: 0 12px;
  color: var(--ink-3);
  text-align: right;
  user-select: none;
  vertical-align: top;
}
.line {
  padding: 0 14px 0 0;
  white-space: pre;
  color: var(--ink);
}
.line :deep(.hljs-keyword),
.line :deep(.hljs-built_in),
.line :deep(.hljs-type) {
  color: var(--accent-text);
}
.line :deep(.hljs-string),
.line :deep(.hljs-number) {
  color: var(--success);
}
.line :deep(.hljs-comment) {
  color: var(--ink-3);
  font-style: italic;
}
.line :deep(.hljs-title),
.line :deep(.hljs-attr) {
  color: var(--info);
}
</style>
