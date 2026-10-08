<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import { useApi } from '../api/inject'
import type { TurnArtifact } from '../chat/turnEvents'
import { IconTile } from '../ui'
import { kindIcons } from './artifactKinds'

/** An artifact shown in a chat reply: images inline, every other kind as a card that opens the viewer. */
const props = defineProps<{ artifact: TurnArtifact }>()
const api = useApi()
const { t } = useI18n()
// Always Marshal's own content route, whatever origin the event's URL names.
const content = `${api.baseUrl}/api/v1/artifacts/${encodeURIComponent(props.artifact.id)}/content`
</script>

<template>
  <RouterLink v-if="artifact.kind === 'image'" class="inline" :to="{ name: 'artifact', params: { id: artifact.id } }" data-test="artifact-image">
    <img :src="content" :alt="artifact.name" />
  </RouterLink>
  <RouterLink v-else class="card" :to="{ name: 'artifact', params: { id: artifact.id } }" data-test="artifact-card" :data-kind="artifact.kind">
    <IconTile :icon="kindIcons[artifact.kind]" :size="36" />
    <span class="text">
      <span class="name">{{ artifact.name }}</span>
      <span class="kind">{{ t(`artifacts.kind.${artifact.kind}`) }}</span>
    </span>
  </RouterLink>
</template>

<style scoped>
.inline img {
  display: block;
  max-width: 100%;
  max-height: 320px;
  border-radius: var(--radius-md);
  background: var(--surface-2);
}
.card {
  display: flex;
  align-items: center;
  gap: var(--space-3);
  align-self: flex-start;
  padding: var(--space-3) var(--space-4) var(--space-3) var(--space-3);
  border: 1px solid var(--line);
  border-radius: var(--radius-md);
  background: var(--surface);
  color: var(--ink);
  text-decoration: none;
}
.card:hover {
  background: var(--surface-2);
}
.text {
  display: flex;
  flex-direction: column;
  min-width: 0;
}
.name {
  font: 550 14px/20px var(--font-sans);
  overflow-wrap: anywhere;
}
.kind {
  color: var(--ink-2);
  font: 400 13px/18px var(--font-sans);
}
</style>
