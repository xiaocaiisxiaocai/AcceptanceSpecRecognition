<script setup lang="ts">
import type { CombinedImportResult } from "../dataImport.types";

defineProps<{
  importResult: CombinedImportResult | null;
}>();
</script>

<template>
  <div class="step-panel">
    <slot />
  </div>
</template>
