<script setup lang="ts">
import { computed } from "vue";
import { useGlobal } from "@pureadmin/utils";
import { useNav } from "@/layout/hooks/useNav";

import MenuFold from "~icons/ri/menu-fold-fill";

interface Props {
  isActive?: boolean;
}

withDefaults(defineProps<Props>(), {
  isActive: false
});

const { tooltipEffect } = useNav();

const iconClass = computed(() => {
  return [
    "ml-4",
    "mb-1",
    "w-[16px]",
    "h-[16px]",
    "inline-block!",
    "align-middle",
    "cursor-pointer",
    "duration-[100ms]"
  ];
});

const { $storage } = useGlobal<GlobalPropertiesApi>();
const themeColor = computed(() => $storage.layout?.themeColor);

const emit = defineEmits<{
  (e: "toggleClick"): void;
}>();

const toggleClick = () => {
  emit("toggleClick");
};
</script>

<template>
  <div class="left-collapse">
    <button
      v-tippy="{
        content: isActive ? '点击折叠' : '点击展开',
        theme: tooltipEffect,
        hideOnClick: 'toggle',
        placement: 'right'
      }"
      type="button"
      class="left-collapse-button"
      :aria-label="isActive ? '折叠侧边栏' : '展开侧边栏'"
      :aria-expanded="isActive"
      :style="{ transform: isActive ? 'none' : 'rotateY(180deg)' }"
      @click="toggleClick"
    >
      <IconifyIconOffline
        :icon="MenuFold"
        :class="[iconClass, themeColor === 'light' ? '' : 'text-primary']"
        aria-hidden="true"
      />
    </button>
  </div>
</template>

<style lang="scss" scoped>
.left-collapse {
  position: absolute;
  bottom: 0;
  width: 100%;
  height: 44px;
  line-height: 44px;
  box-shadow: 0 0 6px -3px var(--el-color-primary);
}

.left-collapse-button {
  width: 100%;
  min-height: 44px;
  padding: 0;
  color: inherit;
  cursor: pointer;
  background: transparent;
  border: 0;
}
</style>
