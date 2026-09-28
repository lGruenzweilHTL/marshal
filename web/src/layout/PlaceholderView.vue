<script setup lang="ts">
import { useRoute } from 'vue-router'

const route = useRoute()
</script>

<template>
  <section class="page">
    <h1>{{ route.meta.title }}</h1>
  </section>
</template>
