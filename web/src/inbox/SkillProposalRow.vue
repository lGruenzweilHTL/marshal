<script setup lang="ts">
import { BookPlus } from 'lucide-vue-next'
import { useI18n } from 'vue-i18n'
import type { Schemas } from '../api/client'
import { useFormat } from '../i18n/locale'
import { Badge, IconTile, ListRow } from '../ui'

/** One skill proposal in a list: the skill's name and description, whether it was downloaded, and when. */
defineProps<{ proposal: Schemas['SkillProposalSummary'] }>()
const format = useFormat()
const { t } = useI18n()
</script>

<template>
  <ListRow :title="proposal.name" :to="`/inbox/skills/${proposal.id}`">
    <template #lead><IconTile :icon="BookPlus" tone="info" /></template>
    <template #subtitle>
      {{ proposal.description }} · <span class="num">{{ format.time(proposal.createdAt) }}</span>
    </template>
    <template v-if="proposal.status === 'downloaded'" #trail><Badge tone="success" :label="t('inbox.skill.status.downloaded')" /></template>
  </ListRow>
</template>
