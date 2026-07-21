<script setup lang="ts">
import MenuFold from "~icons/ri/menu-fold-fill";
import MenuUnfold from "~icons/ri/menu-unfold-fill";

interface Props {
  isActive?: boolean;
}

withDefaults(defineProps<Props>(), {
  isActive: false
});

const emit = defineEmits<{
  (e: "toggleClick"): void;
}>();

const toggleClick = () => {
  emit("toggleClick");
};
</script>

<template>
  <button
    type="button"
    class="px-3 mr-1 navbar-bg-hover"
    :aria-label="isActive ? '折叠侧边栏' : '展开侧边栏'"
    :aria-expanded="isActive"
    @click="toggleClick"
  >
    <IconifyIconOffline
      :icon="isActive ? MenuFold : MenuUnfold"
      class="inline-block! align-middle hover:text-primary dark:hover:text-white!"
    />
  </button>
</template>
