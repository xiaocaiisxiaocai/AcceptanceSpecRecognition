<script setup lang="ts">
import type { CombinedImportResult } from "../dataImport.types";

defineProps<{
  importResult: CombinedImportResult | null;
}>();
</script>

<template>
  <div class="step-panel">
    <h3 class="step-title">确认导入</h3>
    <p class="step-desc">请确认以下导入信息</p>
    <slot />
  </div>
</template>
