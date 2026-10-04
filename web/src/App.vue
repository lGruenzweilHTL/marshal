<script setup lang="ts">
import { RouterView } from 'vue-router'
import GlobalBanner from './layout/GlobalBanner.vue'
import ToastHost from './ui/ToastHost.vue'
</script>

<template>
  <GlobalBanner />
  <RouterView />
  <ToastHost />
</template>
