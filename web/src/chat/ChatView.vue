<script setup lang="ts">
import { ArrowUp, CircleDot, History, Hourglass, MessageSquare, Mic, Plus, SearchX } from 'lucide-vue-next'
import { computed, nextTick, onBeforeUnmount, reactive, ref, shallowRef, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import type { Schemas } from '../api/client'
import type { ApiError } from '../api/errors'
import { useApi } from '../api/inject'
import { satelliteName } from '../format'
import { useLiveEvent, useLiveReconnect } from '../live/liveEvents'
import { AppButton, AppIcon, Banner, ErrorBanner, LoadState, Logo, Page, PageHeader, useDesktop } from '../ui'
import { renderMarkdown } from './markdown'
import { readEvents } from './sse'
import ArtifactCard from '../artifacts/ArtifactCard.vue'
import ConversationList from './ConversationList.vue'
import type { TurnArtifact, TurnDelta, TurnFinal, TurnStatus } from './turnEvents'

interface Message {
  id: number
  role: 'user' | 'assistant'
  text: string
  /** Status events of the turn ("Checking the printer…"), kept as the reply's steps. */
  steps: string[]
  /** Artifacts the turn showed: images inline, every other kind as a card. */
  artifacts: TurnArtifact[]
  /** A spoken user turn: the satellite (or area) it came from, '' when unknown. */
  spoken?: string
  /** The result of a background task this conversation handed off. */
  taskId?: string
  /** A turn that ended without a recorded reply. */
  noReply?: boolean
}

type Entry = Schemas['ConversationDetail']['entries'][number]

/**
 * Written conversation with Marshal over the turn stream: status events become the reply's steps, deltas grow the
 * text, and the final event settles it. A question back (continueConversation) offers Yes / No; the answer is an
 * ordinary user turn, judged by the server like a spoken one. `/chat/{id}` opens an earlier conversation: web chats
 * continue, voice and API conversations are read-only. Results of work moved to the background arrive live.
 */
const api = useApi()
const route = useRoute()
const router = useRouter()
const desktop = useDesktop()
const { t } = useI18n()
const messages = ref<Message[]>([])
const draft = ref('')
const live = shallowRef<string | null>(null)
const error = shallowRef<ApiError | null>(null)
const interrupted = shallowRef(false)
const awaitingAnswer = shallowRef(false)
const busy = shallowRef(false)
const log = ref<HTMLElement | null>(null)
const list = ref<InstanceType<typeof ConversationList> | null>(null)
/** The loaded conversation; null for a new chat. */
const conversation = shallowRef<Schemas['ConversationSummary'] | null>(null)
const opening = shallowRef<'idle' | 'loading' | 'notFound' | 'error'>('idle')
const openError = shallowRef<ApiError | null>(null)
let conversationId: string | null = null
let nextId = 0
let running: AbortController | null = null

const routeId = computed(() => (typeof route.params.id === 'string' && route.params.id ? route.params.id : null))
const readOnly = computed(() => (conversation.value && !conversation.value.canContinue ? conversation.value : null))
const readOnlyNote = computed(() => {
  const source = readOnly.value?.source
  return source === 'voice' ? t('chat.readOnly.voice') : source === 'api' ? t('chat.readOnly.api') : t('chat.readOnly.closed')
})

function reset() {
  running?.abort()
  messages.value = []
  conversationId = null
  conversation.value = null
  opening.value = 'idle'
  openError.value = null
  live.value = null
  error.value = null
  interrupted.value = false
  awaitingAnswer.value = false
}

function newChat() {
  reset()
  if (routeId.value) void router.push('/chat')
}

function toMessages(entry: Entry): Message[] {
  if (entry.kind === 'backgroundResult') return [{ id: nextId++, role: 'assistant', text: entry.body, steps: [], artifacts: [], taskId: entry.taskId }]
  if (entry.kind !== 'turn') return []
  const turn = entry
  const result: Message[] = []
  if (turn.input) {
    const spoken = turn.inputMode === 'voice' ? (turn.satelliteId ? satelliteName(turn.satelliteId) : (turn.areaId ?? '')) : undefined
    result.push({ id: nextId++, role: 'user', text: turn.input, steps: [], artifacts: [], spoken })
  }
  result.push({ id: nextId++, role: 'assistant', text: turn.reply ?? '', steps: [], artifacts: [...turn.artifacts], noReply: turn.reply === null })
  return result
}

function apply(detail: Schemas['ConversationDetail']) {
  conversation.value = detail.conversation
  messages.value = detail.entries.flatMap(toMessages)
  const last = detail.entries.at(-1)
  awaitingAnswer.value = detail.conversation.canContinue && last?.kind === 'turn' && last.continueConversation === true
  opening.value = 'idle'
  void scrollToEnd()
}

async function fetchDetail(id: string) {
  return api.call((client) => client.GET('/api/v1/conversations/{id}', { params: { path: { id } } }))
}

async function open(id: string) {
  reset()
  conversationId = id
  opening.value = 'loading'
  const result = await fetchDetail(id)
  if (conversationId !== id) return
  if (result.error || !result.data) {
    const notFound = result.error?.kind === 'problem' && result.error.status === 404
    opening.value = notFound ? 'notFound' : 'error'
    openError.value = result.error ?? null
    return
  }
  apply(result.data)
}

/** Loads the open conversation again in the background (after a reconnect of the live stream). */
async function reload() {
  const id = conversationId
  if (!id || busy.value || opening.value !== 'idle') return
  const result = await fetchDetail(id)
  if (conversationId === id && !busy.value && result.data) apply(result.data)
}

watch(
  routeId,
  (id) => {
    if (id === conversationId) return
    if (id) void open(id)
    else reset()
  },
  { immediate: true },
)

useLiveEvent('conversations', 'conversation.result', (result) => {
  if (result.sessionId !== conversationId || messages.value.some((message) => message.taskId === result.taskId)) return
  messages.value.push({ id: nextId++, role: 'assistant', text: result.body, steps: [], artifacts: [], taskId: result.taskId })
  void scrollToEnd()
})
useLiveReconnect(() => void reload())

async function scrollToEnd() {
  await nextTick()
  log.value?.lastElementChild?.scrollIntoView?.({ block: 'end' })
}

async function send(text = draft.value.trim()) {
  if (!text || busy.value) return
  draft.value = ''
  error.value = null
  interrupted.value = false
  awaitingAnswer.value = false
  messages.value.push({ id: nextId++, role: 'user', text, steps: [], artifacts: [] })
  const reply = reactive<Message>({ id: nextId++, role: 'assistant', text: '', steps: [], artifacts: [] })
  messages.value.push(reply)
  void scrollToEnd()

  const abort = (running = new AbortController())
  busy.value = true
  const result = await api.call((client) =>
    client.POST('/api/v1/conversation/turns', {
      body: { conversationId, text, inputMode: 'text', language: null, deviceId: null, satelliteId: null, areaId: null, haUserId: null, followupRef: null },
      parseAs: 'stream',
      signal: abort.signal,
    }),
  )
  try {
    if (result.error || !result.data) {
      // Nothing reached Marshal: take the turn back and return the text to the composer.
      if (abort.signal.aborted) return
      messages.value.splice(-2, 2)
      draft.value = text
      error.value = result.error ?? { kind: 'unexpected', cause: 'empty stream' }
      return
    }
    for await (const { event, data: payload } of readEvents(result.data as unknown as ReadableStream<Uint8Array>)) {
      if (event === 'delta') reply.text += (JSON.parse(payload) as TurnDelta).text
      else if (event === 'status') {
        const step = (JSON.parse(payload) as TurnStatus).text
        live.value = step
        reply.steps.push(step)
      } else if (event === 'artifact') reply.artifacts.push(JSON.parse(payload) as TurnArtifact)
      else if (event === 'final') {
        const final = JSON.parse(payload) as TurnFinal
        reply.text = final.speech
        conversationId = final.conversationId
        awaitingAnswer.value = final.continueConversation
        // The conversation gets its own URL with its first turn; the history shows it first.
        if (routeId.value !== final.conversationId) void router.replace(`/chat/${final.conversationId}`)
        list.value?.refresh()
      }
      void scrollToEnd()
    }
  } catch {
    if (!abort.signal.aborted) interrupted.value = true
  } finally {
    if (running === abort) {
      running = null
      busy.value = false
      live.value = null
    }
  }
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
    event.preventDefault()
    void send()
  }
}

onBeforeUnmount(() => running?.abort())
</script>

<template>
  <div class="layout" :class="{ split: desktop }">
    <aside v-if="desktop" class="side" :aria-label="t('chat.history.title')">
      <h2 class="t-title-3 side-title">{{ t('chat.history.title') }}</h2>
      <ConversationList ref="list" :active-id="conversation?.id ?? routeId" />
    </aside>

    <Page width="narrow" class="chat">
      <PageHeader :title="t('chat.title')" :subtitle="desktop ? t('chat.subtitle') : undefined">
        <template #actions>
          <AppButton v-if="!desktop" :icon="History" icon-only :label="t('chat.history.open')" to="/chat/history" data-test="history" />
          <AppButton :icon="Plus" :icon-only="!desktop" :label="t('chat.newChat')" data-test="new-chat" @click="newChat">{{ t('chat.newChat') }}</AppButton>
        </template>
      </PageHeader>

      <LoadState v-if="opening === 'loading'" state="loading" />
      <LoadState
        v-else-if="opening === 'notFound'"
        state="empty"
        :icon="SearchX"
        :title="t('chat.notFound.title')"
        :body="t('chat.notFound.body')"
        data-test="not-found"
      >
        <AppButton :icon="Plus" data-test="not-found-new-chat" @click="newChat">{{ t('chat.newChat') }}</AppButton>
      </LoadState>
      <LoadState v-else-if="opening === 'error'" state="error" :title="t('chat.loadFailed')" :error="openError" @retry="routeId && open(routeId)" />
      <LoadState v-else-if="messages.length === 0" state="empty" :icon="MessageSquare" :title="t('chat.empty.title')" :body="t('chat.empty.body')" />
      <ol ref="log" class="thread" aria-live="polite">
        <li v-for="message in messages" :key="message.id" class="message" :class="message.role" :data-role="message.role">
          <template v-if="message.role === 'user'">
            <span v-if="message.spoken !== undefined" class="spoken" data-test="spoken">
              <AppIcon :icon="Mic" :size="14" />{{ [t('chat.spoken'), message.spoken].filter(Boolean).join(' · ') }}
            </span>
            <p class="bubble">{{ message.text }}</p>
          </template>
          <template v-else>
            <span class="avatar"><Logo variant="tile" :size="28" /></span>
            <div class="reply">
              <span v-if="message.taskId" class="background" data-test="background"><AppIcon :icon="Hourglass" :size="14" />{{ t('chat.background') }}</span>
              <span v-for="(step, index) in message.steps" :key="index" class="step" data-test="step">
                <AppIcon :icon="CircleDot" :size="14" />{{ step }}
              </span>
              <!-- eslint-disable-next-line vue/no-v-html -- rendered with HTML disabled and same-origin images only -->
              <div v-if="message.text" class="markdown" v-html="renderMarkdown(message.text)"></div>
              <span v-else-if="busy && message === messages[messages.length - 1]" class="thinking">{{ t('chat.thinking') }}</span>
              <span v-else-if="message.noReply" class="thinking">{{ t('chat.noReply') }}</span>
              <ArtifactCard v-for="artifact in message.artifacts" :key="artifact.id" :artifact="artifact" />
            </div>
          </template>
        </li>
      </ol>

      <p v-if="live" role="status" class="sr-only">{{ live }}</p>
      <Banner v-if="interrupted" tone="warning" :title="t('chat.interrupted')" />
      <ErrorBanner v-if="error" :error="error" :title="t('chat.failed')" />

      <div v-if="opening === 'idle' && readOnly" class="dock read-only" data-test="read-only">
        <span>{{ readOnlyNote }}</span>
        <AppButton size="sm" variant="primary" :icon="Plus" data-test="read-only-new-chat" @click="newChat">{{ t('chat.newChat') }}</AppButton>
      </div>
      <div v-else-if="opening === 'idle'" class="dock">
        <div v-if="awaitingAnswer" class="answer" data-test="awaiting-answer">
          <span>{{ t('chat.awaiting') }}</span>
          <span class="quick">
            <AppButton size="sm" data-test="no" :disabled="busy" @click="send(t('chat.no'))">{{ t('chat.no') }}</AppButton>
            <AppButton size="sm" variant="primary" data-test="yes" :disabled="busy" @click="send(t('chat.yes'))">{{ t('chat.yes') }}</AppButton>
          </span>
        </div>
        <form class="composer" @submit.prevent="send()">
          <label class="sr-only" for="chat-message">{{ t('chat.message') }}</label>
          <textarea id="chat-message" v-model="draft" rows="1" :placeholder="t('chat.placeholder')" @keydown="onKeydown"></textarea>
          <AppButton type="submit" variant="primary" :icon="ArrowUp" icon-only :label="t('chat.send')" :loading="busy" :disabled="!draft.trim()" />
        </form>
      </div>
    </Page>
  </div>
</template>

<style scoped>
/* Desktop: the conversation list is a side column that scrolls on its own; the chat keeps its column. */
.split {
  display: grid;
  grid-template-columns: 300px minmax(0, 1fr);
}
.side {
  position: sticky;
  top: 0;
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  height: 100dvh;
  overflow-y: auto;
  padding: var(--space-10) 0 var(--space-6) var(--page-gutter);
}
.side-title {
  min-height: 40px;
  display: flex;
  align-items: center;
}
/* The page fills the viewport so the composer rests at the bottom even before the first message. */
.chat {
  min-height: calc(100dvh - var(--tabbar-height) - var(--space-6) - env(safe-area-inset-bottom));
  padding-bottom: var(--space-4);
}
@media (min-width: 900px) {
  .chat {
    min-height: 100dvh;
  }
}
.thread {
  display: flex;
  flex-direction: column;
  gap: var(--space-4);
  padding: 0;
  list-style: none;
}
.message.user {
  display: flex;
  flex-direction: column;
  align-items: flex-end;
  gap: var(--space-1);
  align-self: flex-end;
  max-width: 82%;
}
.spoken,
.background {
  display: inline-flex;
  align-self: flex-end;
  align-items: center;
  gap: 6px;
  color: var(--ink-3);
  font: 500 12px/16px var(--font-sans);
}
.background {
  align-self: flex-start;
  color: var(--accent-text);
}
.bubble {
  padding: 10px 14px;
  border-radius: 18px 18px 6px 18px;
  background: var(--ink);
  color: var(--bg);
  font: var(--text-body);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.message.assistant {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  max-width: 92%;
}
.avatar {
  flex-shrink: 0;
}
.reply {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  min-width: 0;
  font: var(--text-body);
}
.step {
  display: inline-flex;
  align-self: flex-start;
  align-items: center;
  gap: var(--space-2);
  padding: 6px 10px;
  border: 1px solid var(--line);
  border-radius: var(--radius-sm);
  background: var(--surface-2);
  color: var(--ink-2);
  font: 400 13px/18px var(--font-sans);
}
.thinking {
  color: var(--ink-3);
}
.markdown :deep(p) {
  margin: 0 0 var(--space-2);
}
.markdown :deep(p:last-child) {
  margin-bottom: 0;
}
.markdown :deep(code) {
  padding: 1px 5px;
  border-radius: 5px;
  background: var(--surface-3);
  font: var(--text-mono-s);
}
.markdown :deep(pre) {
  overflow-x: auto;
  padding: var(--space-3);
  border-radius: var(--radius-sm);
  background: var(--surface-2);
}
.markdown :deep(a) {
  color: var(--accent-text);
}
.dock {
  position: sticky;
  bottom: var(--space-4);
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  margin-top: auto;
}
.answer {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: var(--space-2);
  padding: 10px 10px 10px var(--space-4);
  border-radius: var(--radius-lg);
  background: var(--accent-soft);
  color: var(--accent-text);
  font: 550 14px/20px var(--font-sans);
}
.read-only {
  flex-direction: row;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  padding: 10px 10px 10px var(--space-4);
  border: 1px solid var(--line);
  border-radius: var(--radius-lg);
  background: var(--surface);
  box-shadow: var(--shadow-1);
  color: var(--ink-2);
  font: 450 14px/20px var(--font-sans);
}
.quick {
  display: flex;
  gap: var(--space-2);
}
.composer {
  display: flex;
  align-items: flex-end;
  gap: var(--space-2);
  padding: var(--space-2) var(--space-2) var(--space-2) var(--space-4);
  border: 1px solid var(--line-strong);
  border-radius: 22px;
  background: var(--surface);
  box-shadow: var(--shadow-2);
}
.composer:focus-within {
  outline: var(--focus-ring);
}
textarea {
  flex-grow: 1;
  /* The control height, so a one-line draft is centred on the send button and the button stays at the bottom edge
     when the draft grows. */
  min-height: var(--target);
  max-height: 200px;
  padding: 11px 0;
  border: 0;
  outline: 0;
  resize: none;
  background: transparent;
  color: var(--ink);
  font: 400 16px/22px var(--font-sans);
  field-sizing: content;
}
</style>
