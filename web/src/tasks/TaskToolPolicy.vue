<script setup lang="ts">
import { Check, Pencil, X } from 'lucide-vue-next'
import { computed, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApi } from '../api/inject'
import { useSubmit } from '../api/useSubmit'
import { AppButton, Card, Checkbox, ErrorBanner } from '../ui'

/**
 * A task's tool policy: the globs it may use, or every tool its template allows when it has none.
 * The editor offers one glob per namespace of the available tools plus the single tool ids already in the policy.
 */
const props = defineProps<{ taskId: string; policy: string[] | null; available: string[] }>()
const emit = defineEmits<{ saved: [] }>()
const api = useApi()
const { t } = useI18n()

const options = computed(() => {
  const namespaces = props.available.map((tool) => `${tool.split('.')[0]}.*`)
  return [...new Set([...namespaces, ...(props.policy ?? [])])].sort()
})

const editing = shallowRef(false)
const selected = shallowRef<string[]>([])

function edit() {
  selected.value = [...(props.policy ?? [])]
  editing.value = true
}

const save = useSubmit(async () => {
  const tools = options.value.filter((option) => selected.value.includes(option))
  const result = await api.call((client) => client.PUT('/api/v1/tasks/{id}/tool-policy', { params: { path: { id: props.taskId } }, body: { tools } }))
  if (result.data) {
    editing.value = false
    emit('saved')
  }
  return result
})
</script>

<template>
  <Card data-test="tool-policy">
    <span class="t-overline">{{ t('tasks.detail.policy.title') }}</span>
    <form v-if="editing" data-test="policy-form" class="form" @submit.prevent="save.submit()">
      <Checkbox v-for="option in options" :key="option" v-model="selected" :value="option" :label="option" mono />
      <ErrorBanner v-if="save.error.value" :error="save.error.value" />
      <div class="buttons">
        <AppButton type="submit" variant="primary" size="sm" :icon="Check" :loading="save.busy.value" :disabled="!selected.length">
          {{ t('tasks.detail.policy.save') }}
        </AppButton>
        <AppButton size="sm" variant="ghost" :icon="X" @click="editing = false">{{ t('tasks.detail.discard') }}</AppButton>
      </div>
    </form>
    <template v-else>
      <ul v-if="policy" class="globs">
        <li v-for="glob in policy" :key="glob"><code class="t-mono-s">{{ glob }}</code></li>
      </ul>
      <p v-else class="ink-3 empty">{{ t('tasks.detail.policy.none') }}</p>
      <AppButton size="sm" :icon="Pencil" data-test="edit-policy" @click="edit">{{ t('tasks.detail.policy.edit') }}</AppButton>
    </template>
  </Card>
</template>

<style scoped>
.form {
  display: flex;
  flex-direction: column;
  gap: var(--space-1);
  margin-top: var(--space-2);
}
.buttons {
  display: flex;
  gap: var(--space-2);
  margin-top: var(--space-2);
}
.globs {
  display: flex;
  flex-wrap: wrap;
  gap: var(--space-2);
  margin: var(--space-2) 0 var(--space-3);
  padding: 0;
  list-style: none;
}
.empty {
  margin: var(--space-2) 0 var(--space-3);
}
</style>
