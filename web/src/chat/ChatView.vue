<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref } from 'vue'
import { useApi } from '../api/inject'
import { problemMessage } from '../api/problem'
import { renderMarkdown } from './markdown'
import { readEvents } from './sse'
import type { TurnDelta, TurnFinal, TurnStatus } from './turnEvents'

interface Message {
  id: number
  role: 'user' | 'assistant'
  text: string
}

const session = useApi()
const messages = ref<Message[]>([])
const draft = ref('')
const status = ref<string | null>(null)
const error = ref<string | null>(null)
const awaitingAnswer = ref(false)
const busy = ref(false)
const log = ref<HTMLElement | null>(null)
let conversationId: string | null = null
let nextId = 0
let running: AbortController | null = null

function newChat() {
  running?.abort()
  messages.value = []
  conversationId = null
  status.value = null
  error.value = null
  awaitingAnswer.value = false
}

async function scrollToEnd() {
  await nextTick()
  log.value?.lastElementChild?.scrollIntoView?.({ block: 'end' })
}

async function send() {
  const text = draft.value.trim()
  if (!text || busy.value) return
  draft.value = ''
  error.value = null
  awaitingAnswer.value = false
  messages.value.push({ id: nextId++, role: 'user', text })
  messages.value.push({ id: nextId++, role: 'assistant', text: '' })
  // The reactive proxy, so streamed text re-renders.
  const current = messages.value[messages.value.length - 1]!
  void scrollToEnd()

  const abort = (running = new AbortController())
  busy.value = true
  try {
    const { error: problem, response } = await session.api.POST('/api/v1/conversation/turns', {
      body: { conversationId, text, inputMode: 'text', language: null, deviceId: null, satelliteId: null, areaId: null, haUserId: null, followupRef: null },
      parseAs: 'stream',
      signal: abort.signal,
    })
    if (!response.ok || !response.body) {
      messages.value.pop()
      error.value = problemMessage(problem, `The message could not be sent (HTTP ${response.status}).`)
      return
    }
    for await (const { event, data: payload } of readEvents(response.body)) {
      if (event === 'delta') current.text += (JSON.parse(payload) as TurnDelta).text
      else if (event === 'status') status.value = (JSON.parse(payload) as TurnStatus).text
      else if (event === 'final') {
        const final = JSON.parse(payload) as TurnFinal
        current.text = final.speech
        conversationId = final.conversationId
        awaitingAnswer.value = final.continueConversation
      }
      void scrollToEnd()
    }
  } catch {
    if (!abort.signal.aborted) error.value = 'The connection was interrupted.'
  } finally {
    if (running === abort) {
      running = null
      busy.value = false
      status.value = null
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
  <section class="chat">
    <header class="chat-header">
      <h1>Chat</h1>
      <button type="button" data-test="new-chat" @click="newChat">New chat</button>
    </header>
    <ol ref="log" class="messages" aria-live="polite">
      <li v-for="message in messages" :key="message.id" :class="['message', message.role]" :data-role="message.role">
        <p v-if="message.role === 'user'">{{ message.text }}</p>
        <!-- eslint-disable-next-line vue/no-v-html -- rendered with HTML disabled and same-origin images only -->
        <div v-else class="markdown" v-html="renderMarkdown(message.text)"></div>
      </li>
    </ol>
    <p v-if="status" role="status" class="status">{{ status }}</p>
    <p v-if="awaitingAnswer" data-test="awaiting-answer" class="hint">Marshal is waiting for your answer.</p>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <form class="composer" @submit.prevent="send">
      <textarea v-model="draft" rows="2" placeholder="Message Marshal" aria-label="Message" @keydown="onKeydown"></textarea>
      <button type="submit" :disabled="busy || !draft.trim()">Send</button>
    </form>
  </section>
</template>

<style scoped>
.chat {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  min-height: calc(100vh - 6rem);
}

.chat-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 0.5rem;
}

.chat-header h1 {
  margin: 0;
  font-size: 1.25rem;
}

.messages {
  flex: 1;
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.message {
  max-width: 100%;
  padding: 0.5rem 0.75rem;
  border-radius: 12px;
  overflow-wrap: anywhere;
}

.message.user {
  align-self: flex-end;
  max-width: 85%;
  background: var(--surface);
}

.message.user p {
  margin: 0;
  white-space: pre-wrap;
}

.markdown :deep(pre),
.markdown :deep(table) {
  display: block;
  max-width: 100%;
  overflow-x: auto;
}

.markdown :deep(img) {
  max-width: 100%;
}

.markdown :deep(> :first-child) {
  margin-top: 0;
}

.markdown :deep(> :last-child) {
  margin-bottom: 0;
}

.status,
.hint {
  margin: 0;
  color: var(--muted);
  font-style: italic;
}

.composer {
  position: sticky;
  bottom: 0;
  display: flex;
  gap: 0.5rem;
  padding: 0.5rem 0;
  background: var(--bg);
}

.composer textarea {
  flex: 1;
  resize: vertical;
}
</style>
