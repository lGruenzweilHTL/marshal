<script setup lang="ts">
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { ErrorBanner } from '../ui'
import InputRequestForm from '../inbox/InputRequestForm.vue'

/** A request for input an MCP server sent during this reply, answered right in the chat. */
const props = defineProps<{ id: string }>()
const api = useApi()
const request = useLoad(() => api.call((client) => client.GET('/api/v1/approvals/inputs/{id}', { params: { path: { id: props.id } } })), { isEmpty: () => false })
</script>

<template>
  <InputRequestForm v-if="request.data.value" :request="request.data.value" density="compact" />
  <ErrorBanner v-else-if="request.error.value" :error="request.error.value" />
</template>
