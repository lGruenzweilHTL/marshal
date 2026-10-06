<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { Logo, useDesktop } from '../ui'
import Constellation from './Constellation.vue'

/** Frame of the pages before sign-in: a card with the wordmark; desktop adds the constellation panel on the right. */
const desktop = useDesktop()
const { t } = useI18n()
</script>

<template>
  <div class="auth" :class="{ two: desktop }">
    <main class="side">
      <div class="card">
        <Logo variant="wordmark" />
        <slot />
      </div>
    </main>
    <aside v-if="desktop" class="panel">
      <Constellation />
      <p class="tagline">{{ t('auth.tagline') }}</p>
    </aside>
  </div>
</template>

<style scoped>
.auth {
  min-height: 100dvh;
  background: var(--bg);
}
.auth.two {
  display: grid;
  grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
}
.side {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100dvh;
  padding: var(--space-6) var(--page-gutter);
}
.card {
  display: flex;
  flex-direction: column;
  gap: 22px;
  width: 100%;
  max-width: 420px;
  padding: var(--space-8);
  border-radius: var(--radius-xl);
  background: var(--surface);
  box-shadow: var(--shadow-2);
}
.panel {
  position: relative;
  display: flex;
  flex-direction: column;
  padding: 56px;
  overflow: hidden;
  background: var(--panel);
  color: var(--panel-ink);
}
.tagline {
  position: relative;
  max-width: 380px;
  font: var(--text-title-1);
  letter-spacing: var(--tracking-title-1);
}
</style>
