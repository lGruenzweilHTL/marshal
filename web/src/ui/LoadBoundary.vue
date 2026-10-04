<script setup lang="ts" generic="T">
import type { Component } from 'vue'
import type { Load } from '../api/useLoad'
import LoadState from './LoadState.vue'

/**
 * Renders a `useLoad` result: the skeleton while loading, "could not load" with retry on failure, the empty state, and
 * the default slot (with the data) when ready. Every view that loads data goes through this, so no load can fail
 * silently.
 */
defineProps<{
  load: Load<T>
  errorTitle?: string
  emptyTitle?: string
  emptyBody?: string
  emptyIcon?: Component
  emptyAction?: string
  size?: 'card' | 'page'
}>()
const emit = defineEmits<{ emptyAction: [] }>()
</script>

<template>
  <LoadState v-if="load.state.value === 'loading'" state="loading" :size="size" />
  <LoadState v-else-if="load.state.value === 'error'" state="error" :title="errorTitle" :error="load.error.value" :size="size" @retry="load.retry()" />
  <template v-else-if="load.state.value === 'empty' && !$slots.empty">
    <LoadState state="empty" :title="emptyTitle" :body="emptyBody" :icon="emptyIcon" :action-label="emptyAction" :size="size" @action="emit('emptyAction')" />
  </template>
  <slot v-else-if="load.state.value === 'empty'" name="empty" />
  <slot v-else :data="load.data.value as T" />
</template>
