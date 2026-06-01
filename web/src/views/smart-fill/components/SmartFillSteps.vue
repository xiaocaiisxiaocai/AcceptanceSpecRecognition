<script setup lang="ts">
defineProps<{
  steps: Array<{
    title: string;
    description: string;
  }>;
  currentStep: number;
}>();
</script>

<template>
  <el-card class="mb-4">
    <el-steps :active="currentStep" finish-status="success">
      <el-step
        v-for="(step, index) in steps"
        :key="index"
        :title="step.title"
        :description="step.description"
      />
    </el-steps>
  </el-card>
</template>
