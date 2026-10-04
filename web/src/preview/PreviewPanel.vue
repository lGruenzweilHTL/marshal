<script setup lang="ts">
import { FlaskConical } from 'lucide-vue-next'
import { shallowRef } from 'vue'
import type { Language } from '../i18n/catalog'
import { useLocale } from '../i18n/locale'
import { useAppearance, type Appearance } from '../ui/appearance'
import AppIcon from '../ui/AppIcon.vue'
import { scenarios, type ScenarioName } from './scenarios'
import { readSettings, storeSettings } from './settings'

/** Preview mode only: switches scenario (reloads the app), language and appearance. Never part of the production build. */
const settings = readSettings()
const scenario = shallowRef<ScenarioName>(settings.scenario)
const language = shallowRef<Language>(settings.language)
const open = shallowRef(false)
const locale = useLocale()
const { appearance, setAppearance } = useAppearance()

function changeScenario() {
  storeSettings({ scenario: scenario.value, language: language.value })
  location.reload()
}

function changeLanguage() {
  storeSettings({ scenario: scenario.value, language: language.value })
  locale.apply({ language: language.value, timeZone: locale.timeZone.value })
}
</script>

<template>
  <aside class="preview-panel" :class="{ open }" aria-label="Preview">
    <button type="button" class="toggle" :aria-expanded="open" @click="open = !open">
      <AppIcon :icon="FlaskConical" :size="16" /><span>Preview · {{ scenario }} · {{ language }}</span>
    </button>
    <div v-if="open" class="controls">
      <label>Scenario
        <select v-model="scenario" @change="changeScenario">
          <option v-for="name in scenarios" :key="name" :value="name">{{ name }}</option>
        </select>
      </label>
      <label>Language
        <select v-model="language" @change="changeLanguage">
          <option value="en">English</option>
          <option value="de">Deutsch</option>
        </select>
      </label>
      <label>Appearance
        <select :value="appearance" @change="setAppearance(($event.target as HTMLSelectElement).value as Appearance)">
          <option value="system">System</option>
          <option value="light">Light</option>
          <option value="dark">Dark</option>
        </select>
      </label>
    </div>
  </aside>
</template>

<style scoped>
.preview-panel {
  position: fixed;
  left: var(--space-3);
  bottom: calc(var(--tabbar-height) + var(--space-8) + env(safe-area-inset-bottom));
  z-index: 70;
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  max-width: calc(100vw - 2 * var(--space-3));
  font: var(--text-body-s);
}
@media (min-width: 900px) {
  .preview-panel {
    left: auto;
    right: var(--space-4);
    bottom: var(--space-4);
  }
}
.toggle {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  align-self: flex-start;
  min-height: 32px;
  padding: 0 var(--space-3);
  border: 1px dashed var(--line-strong);
  border-radius: var(--radius-full);
  background: var(--surface);
  color: var(--ink-2);
  box-shadow: var(--shadow-2);
  cursor: pointer;
}
.controls {
  display: flex;
  flex-direction: column;
  gap: var(--space-2);
  padding: var(--space-3);
  border-radius: var(--radius-md);
  background: var(--surface);
  box-shadow: var(--shadow-3);
}
label {
  display: flex;
  flex-direction: column;
  gap: 2px;
  color: var(--ink-3);
}
select {
  min-height: 36px;
  border: 1px solid var(--line-strong);
  border-radius: var(--radius-sm);
  background: var(--surface);
  color: var(--ink);
}
</style>
