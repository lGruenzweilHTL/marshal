<script setup lang="ts">
import { computed } from 'vue'
import { createMarkdownRenderer } from '../../chat/markdown'
import { fenceHtml } from '../highlighting/render'
import '../highlighting/syntax.css'

/** A Markdown artifact rendered with the chat's rules, fenced code highlighted (lazy: keeps the highlighter out of chat). */
const props = defineProps<{ text: string }>()
const render = createMarkdownRenderer({ highlight: fenceHtml })
const html = computed(() => render(props.text))
</script>

<template>
  <!-- eslint-disable-next-line vue/no-v-html -- rendered with HTML disabled and same-origin images only -->
  <div class="markdown" data-test="markdown" v-html="html"></div>
</template>

<style scoped>
.markdown :deep(a) {
  color: var(--accent-text);
}
.markdown :deep(pre) {
  overflow-x: auto;
  padding: var(--space-3) 14px;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: var(--surface-2);
  font: var(--text-mono);
}
</style>
