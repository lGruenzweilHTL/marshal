<script setup lang="ts">
import { GitBranch } from 'lucide-vue-next'
import type { Schemas } from '../api/client'
import { useFormat } from '../i18n/locale'
import { IconTile, ListRow } from '../ui'

/** One change proposal in a list: title, target and branch, and when it was proposed. */
defineProps<{ proposal: Schemas['ProposalRecord'] }>()
const format = useFormat()
</script>

<template>
  <ListRow :title="proposal.title" :to="`/inbox/proposals/${proposal.id}`">
    <template #lead><IconTile :icon="GitBranch" tone="info" /></template>
    <template #subtitle>
      <span class="t-mono-s">{{ proposal.target }} · {{ proposal.branch }}</span> · <span class="num">{{ format.time(proposal.createdAt) }}</span>
    </template>
  </ListRow>
</template>

