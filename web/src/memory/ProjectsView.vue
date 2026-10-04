<script setup lang="ts">
import { FolderClosed, Plus } from 'lucide-vue-next'
import { reactive, shallowRef } from 'vue'
import { useI18n } from 'vue-i18n'
import { useApi } from '../api/inject'
import { useLoad } from '../api/useLoad'
import { useSubmit } from '../api/useSubmit'
import { AppButton, Card, ErrorBanner, Field, LoadBoundary, Page, PageHeader, useDesktop } from '../ui'
import MemoryTabs from './MemoryTabs.vue'
import ProjectCard from './ProjectCard.vue'

/** Projects with their aliases; naming one in a request loads its memory. */
const api = useApi()
const desktop = useDesktop()
const { t } = useI18n()

const projects = useLoad(() => api.call((client) => client.GET('/api/v1/memory/projects')))
const adding = shallowRef(false)
const draft = reactive({ name: '', description: '' })
const create = useSubmit(async () => {
  const result = await api.call((client) => client.POST('/api/v1/memory/projects', { body: { name: draft.name, description: draft.description } }))
  if (result.data) {
    Object.assign(draft, { name: '', description: '' })
    adding.value = false
    projects.refresh()
  }
  return result
})
</script>

<template>
  <Page width="wide">
    <PageHeader :title="t('memory.title')" :subtitle="desktop ? t('memory.projects.intro') : undefined">
      <template #actions>
        <AppButton variant="primary" :icon="Plus" :icon-only="!desktop" :label="t('memory.projects.newProject')" data-test="show-new" @click="adding = !adding">
          {{ t('memory.projects.newProject') }}
        </AppButton>
      </template>
    </PageHeader>
    <MemoryTabs />

    <Card v-if="adding" :title="t('memory.projects.newProject')">
      <form class="form" data-test="new-project" @submit.prevent="create.submit()">
        <Field v-model="draft.name" name="name" :label="t('memory.projects.name')" required />
        <Field v-model="draft.description" kind="textarea" :rows="2" name="description" :label="t('memory.projects.description')" />
        <ErrorBanner v-if="create.error.value" :error="create.error.value" />
        <div><AppButton type="submit" variant="primary" :icon="Plus" :loading="create.busy.value">{{ t('memory.projects.create') }}</AppButton></div>
      </form>
    </Card>

    <LoadBoundary :load="projects" size="page" :empty-title="t('memory.projects.empty.title')" :empty-body="t('memory.projects.empty.body')" :empty-icon="FolderClosed">
      <template #default="{ data }">
        <div class="grid" :class="{ two: desktop }">
          <ProjectCard v-for="project in data" :key="project.id" :project="project" @changed="projects.refresh()" />
        </div>
      </template>
    </LoadBoundary>
  </Page>
</template>

<style scoped>
.form {
  display: flex;
  flex-direction: column;
  gap: var(--space-3);
}
.grid {
  display: grid;
  gap: var(--space-4);
}
.grid.two {
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: var(--space-5);
  align-items: start;
}
</style>
