<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import type { Schemas } from '../api/client'
import { useApiSession } from '../api/inject'
import { problemMessage } from '../api/problem'
import ProjectCard from './ProjectCard.vue'

const session = useApiSession()
const projects = ref<Schemas['ProjectView'][] | null>(null)
const error = ref<string | null>(null)
const draft = reactive({ name: '', description: '' })

async function load() {
  const { data } = await session.raw.GET('/api/v1/memory/projects')
  projects.value = data ?? null
  error.value = data ? null : 'The projects could not be loaded.'
}

async function create() {
  const { data, error: problem } = await session.raw.POST('/api/v1/memory/projects', { body: { name: draft.name, description: draft.description } })
  if (!data) {
    error.value = problemMessage(problem, 'The project could not be created.')
    return
  }
  Object.assign(draft, { name: '', description: '' })
  await load()
}

onMounted(load)
</script>

<template>
  <section class="page">
    <h1>Projects</h1>
    <p class="muted">Naming a project or one of its aliases loads its memory; Marshal also infers projects from what you say.</p>
    <p v-if="error" role="alert" class="error">{{ error }}</p>
    <p v-if="projects && !projects.length" class="muted">No projects yet.</p>
    <ProjectCard v-for="project in projects" :key="project.id" :project="project" @changed="load" />

    <form class="stack" data-test="new-project" @submit.prevent="create">
      <h2>New project</h2>
      <label>Name <input v-model="draft.name" name="name" required maxlength="100" /></label>
      <label>Description <textarea v-model="draft.description" name="description" rows="2" maxlength="2000"></textarea></label>
      <button type="submit">Create</button>
    </form>
  </section>
</template>
