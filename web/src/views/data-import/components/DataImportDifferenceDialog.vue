<script setup lang="ts">
defineProps<{
  modelValue: boolean;
}>();

const emit = defineEmits<{
  (e: "update:modelValue", value: boolean): void;
}>();
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    title="确认是否覆盖已有记录"
    width="min(1100px, calc(100vw - 32px))"
    top="6vh"
    class="difference-dialog"
    :close-on-click-modal="false"
    :close-on-press-escape="false"
    @update:model-value="value => emit('update:modelValue', value)"
  >
    <slot />
    <template #footer>
      <slot name="footer" />
    </template>
  </el-dialog>
</template>
