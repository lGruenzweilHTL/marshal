<script setup lang="ts">
import { ArrowUp, CircleDot, MessageSquare, Plus } from 'lucide-vue-next'
import { nextTick, onBeforeUnmount, reactive, ref, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ApiError } from '../api/errors'
import { useApi } from '../api/inject'
import { AppButton, AppIcon, Banner, ErrorBanner, LoadState, Logo, Page, PageHeader, useDesktop } from '../ui'
import { renderMarkdown } from './markdown'
import { readEvents } from './sse'
import type { TurnDelta, TurnFinal, TurnStatus } from './turnEvents'

interface Message {
  id: number
  role: 'user' | 'assistant'
  text: string
  /** Status events of the turn ("Checking the printer…"), kept as the reply's steps. */
  steps: string[]
}

/**
 * Written conversation with Marshal over the turn stream: status events become the reply's steps, deltas grow the
 * text, and the final event settles it. A question back (continueConversation) offers Yes / No; the answer is an
 * ordinary user turn, judged by the server like a spoken one.
 */
const api = useApi()
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
let conversationId: string | null = null
let nextId = 0
let running: AbortController | null = null

function newChat() {
  running?.abort()
  messages.value = []
  conversationId = null
  live.value = null
  error.value = null
  interrupted.value = false
  awaitingAnswer.value = false
}

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
  messages.value.push({ id: nextId++, role: 'user', text, steps: [] })
  const reply = reactive<Message>({ id: nextId++, role: 'assistant', text: '', steps: [] })
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
      } else if (event === 'final') {
        const final = JSON.parse(payload) as TurnFinal
        reply.text = final.speech
        conversationId = final.conversationId
        awaitingAnswer.value = final.continueConversation
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
  <Page width="narrow" class="chat">
    <PageHeader :title="t('chat.title')" :subtitle="desktop ? t('chat.subtitle') : undefined">
      <template #actions>
        <AppButton :icon="Plus" :icon-only="!desktop" :label="t('chat.newChat')" data-test="new-chat" @click="newChat">{{ t('chat.newChat') }}</AppButton>
      </template>
    </PageHeader>

    <LoadState v-if="messages.length === 0" state="empty" :icon="MessageSquare" :title="t('chat.empty.title')" :body="t('chat.empty.body')" />
    <ol ref="log" class="thread" aria-live="polite">
      <li v-for="message in messages" :key="message.id" class="message" :class="message.role" :data-role="message.role">
        <p v-if="message.role === 'user'" class="bubble">{{ message.text }}</p>
        <template v-else>
          <span class="avatar"><Logo variant="tile" :size="28" /></span>
          <div class="reply">
            <span v-for="(step, index) in message.steps" :key="index" class="step" data-test="step">
              <AppIcon :icon="CircleDot" :size="14" />{{ step }}
            </span>
            <!-- eslint-disable-next-line vue/no-v-html -- rendered with HTML disabled and same-origin images only -->
            <div v-if="message.text" class="markdown" v-html="renderMarkdown(message.text)"></div>
            <span v-else-if="busy && message === messages[messages.length - 1]" class="thinking">{{ t('chat.thinking') }}</span>
          </div>
        </template>
      </li>
    </ol>

    <p v-if="live" role="status" class="sr-only">{{ live }}</p>
    <Banner v-if="interrupted" tone="warning" :title="t('chat.interrupted')" />
    <ErrorBanner v-if="error" :error="error" :title="t('chat.failed')" />

    <div class="dock">
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
</template>

<style scoped>
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
  align-self: flex-end;
  max-width: 82%;
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
  min-height: 36px;
  max-height: 200px;
  padding: 7px 0;
  border: 0;
  outline: 0;
  resize: none;
  background: transparent;
  color: var(--ink);
  font: 400 16px/22px var(--font-sans);
  field-sizing: content;
}
</style>
