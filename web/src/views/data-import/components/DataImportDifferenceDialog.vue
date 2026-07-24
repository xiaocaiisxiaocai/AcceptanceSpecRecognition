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

<style scoped>
:global(.difference-dialog) {
  display: flex;
  flex-direction: column;
  max-height: 88vh;
  overflow: hidden;
  border-radius: 10px;
}

:global(.difference-dialog .el-dialog__header) {
  flex: none;
  padding: 16px 20px 14px;
  margin-right: 0;
  border-bottom: 1px solid var(--app-border-light);
}

:global(.difference-dialog .el-dialog__title) {
  font-size: 17px;
  font-weight: 600;
  color: var(--app-text-primary);
}

:global(.difference-dialog .el-dialog__body) {
  display: flex;
  flex: 1;
  flex-direction: column;
  min-height: 0;
  padding: 14px 16px 12px;
  overflow: hidden;
}

:global(.difference-dialog .el-dialog__footer) {
  flex: none;
  padding: 12px 16px;
  background: var(--el-fill-color-extra-light);
  border-top: 1px solid var(--app-border-light);
}

@media (width <= 600px) {
  :global(.difference-dialog .el-dialog__header) {
    padding: 14px 16px 12px;
  }

  :global(.difference-dialog .el-dialog__body) {
    padding: 12px;
  }
}
</style>
