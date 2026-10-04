<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { ApiError } from '../api/errors'
import { errorMessage } from '../api/errorMessage'
import Banner from './Banner.vue'

/** A failed call next to a form or in the global banner: its message in the interface language and the trace id. */
const props = withDefaults(defineProps<{ error: ApiError; title?: string; dismissible?: boolean }>(), { title: undefined, dismissible: false })
const emit = defineEmits<{ dismiss: [] }>()
const i18n = useI18n()
const message = computed(() => errorMessage(props.error, i18n))
const trace = computed(() => (props.error.kind === 'problem' ? props.error.traceId : undefined))
</script>

<template>
  <Banner
    :tone="error.kind === 'network' ? 'offline' : 'danger'"
    :title="title ?? message"
    :body="title ? message : undefined"
    :trace="trace"
    :dismissible="dismissible"
    @dismiss="emit('dismiss')"
  >
    <template v-if="$slots.action" #action><slot name="action" /></template>
  </Banner>
</template>
