<script setup lang="ts">
import { Check, Copy, WrapText } from 'lucide-vue-next'
import { computed, nextTick, onMounted, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { AppButton } from '../../ui'
import { indentGuides } from '../highlighting/guides'
import { highlight } from '../highlighting/highlighter'
import { languageForFile } from '../highlighting/languages'
import { lineHash, parseLineHash, type LineRange } from '../highlighting/lineRange'
import { lineHtml, splitLines } from '../highlighting/render'
import '../highlighting/syntax.css'

/**
 * Source text highlighted by the shared highlighter (spec code-highlighting), with line numbers, indent guides, a
 * toolbar (language, line count, wrap toggle, copy) and line links (`#L12`, shift-click for `#L12-L20`).
 */
const props = defineProps<{ name: string; text: string }>()
const { t } = useI18n()
const route = useRoute()
const router = useRouter()

const wrapKey = 'marshal.code.wrap'
const language = computed(() => languageForFile(props.name))
const lines = computed(() => splitLines(props.text))
const html = computed(() => {
  const spans = language.value ? highlight(language.value, lines.value) : []
  return lines.value.map((line, index) => lineHtml(line, spans[index] ?? []))
})
const guides = computed(() => (language.value?.guides ? indentGuides(lines.value) : null))
const selection = computed(() => parseLineHash(route.hash, lines.value.length))
const wrap = shallowRef(readWrap())
const copied = shallowRef(false)
let anchor: number | null = null

function readWrap(): boolean {
  try {
    return localStorage.getItem(wrapKey) === 'true'
  } catch {
    return false
  }
}

function toggleWrap() {
  wrap.value = !wrap.value
  try {
    localStorage.setItem(wrapKey, String(wrap.value))
  } catch {
    // Private mode or blocked storage: the toggle still works for this page.
  }
}

async function copy() {
  await navigator.clipboard?.writeText(props.text)
  copied.value = true
  setTimeout(() => (copied.value = false), 2000)
}

function select(line: number, event: MouseEvent) {
  const range: LineRange = event.shiftKey && anchor !== null ? { from: Math.min(anchor, line), to: Math.max(anchor, line) } : { from: line, to: line }
  if (!event.shiftKey) anchor = line
  void router.replace({ hash: lineHash(range) })
}

const selected = (line: number) => selection.value !== null && line >= selection.value.from && line <= selection.value.to

const guideStyle = (index: number) =>
  guides.value && guides.value.guides[index]! > 0 ? { '--guides': String(guides.value.guides[index]), '--unit': String(guides.value.unit) } : undefined

onMounted(async () => {
  if (!selection.value) return
  anchor = selection.value.from
  await nextTick()
  document.getElementById(`L${selection.value.from}`)?.scrollIntoView?.({ block: 'center' })
})
</script>

<template>
  <div class="viewer">
    <div class="toolbar">
      <span class="info">
        <span data-test="language">{{ language?.name ?? t('artifacts.code.plain') }}</span>
        <span class="ink-3" data-test="line-count">{{ t('artifacts.code.lines', lines.length) }}</span>
      </span>
      <span class="actions">
        <AppButton
          variant="ghost"
          size="sm"
          :icon="WrapText"
          :aria-pressed="wrap"
          data-test="wrap"
          @click="toggleWrap"
          >{{ t('artifacts.code.wrap') }}</AppButton
        >
        <AppButton variant="ghost" size="sm" :icon="copied ? Check : Copy" data-test="copy" @click="copy">{{
          copied ? t('artifacts.code.copied') : t('artifacts.code.copy')
        }}</AppButton>
      </span>
    </div>
    <div class="code" :class="{ wrap }" data-test="code" :data-language="language?.id ?? 'plaintext'">
      <table>
        <tbody>
          <tr v-for="(line, index) in html" :id="`L${index + 1}`" :key="index" :class="{ selected: selected(index + 1) }" data-test="line">
            <td class="number" data-test="line-number" @click="select(index + 1, $event)">{{ index + 1 }}</td>
            <!-- eslint-disable-next-line vue/no-v-html -- highlighter output: the text is escaped -->
            <td class="line" :style="guideStyle(index)" v-html="line || ' '"></td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style scoped>
.viewer {
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  background: var(--surface);
  overflow: hidden;
}
.toolbar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-2);
  padding: var(--space-1) var(--space-2) var(--space-1) var(--space-3);
  border-bottom: 1px solid var(--line);
  background: var(--surface-2);
}
.info {
  display: inline-flex;
  gap: var(--space-3);
  font: var(--text-label);
}
.actions {
  display: inline-flex;
  gap: var(--space-1);
}
.code {
  overflow-x: auto;
  font: 450 13px/21px var(--font-mono);
}
table {
  border-collapse: collapse;
  width: 100%;
}
.number {
  width: 1%;
  padding: 0 12px;
  color: var(--ink-3);
  text-align: right;
  user-select: none;
  vertical-align: top;
  cursor: pointer;
}
.number:hover {
  color: var(--ink);
}
.line {
  padding: 0 14px 0 0;
  white-space: pre;
  color: var(--ink);
  /* Indent guides: a 1px line every --unit columns, --guides times, without extra markup. */
  background-image: repeating-linear-gradient(to right, var(--syntax-guide) 0 1px, transparent 1px calc(var(--unit, 4) * 1ch));
  background-size: calc(var(--guides, 0) * var(--unit, 4) * 1ch) 100%;
  background-repeat: no-repeat;
}
.wrap .line {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.selected td {
  background-color: var(--accent-soft);
}
</style>
