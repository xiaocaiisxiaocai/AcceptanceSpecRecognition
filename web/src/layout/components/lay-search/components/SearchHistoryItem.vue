<script setup lang="ts">
// @ts-nocheck
import type { optionsItem } from "../types";
import { useRenderIcon } from "@/components/ReIcon/src/hooks";
import StarIcon from "~icons/ep/star";
import CloseIcon from "~icons/ep/close";

interface Props {
  item: optionsItem;
}

interface Emits {
  (e: "collectItem", val: optionsItem): void;
  (e: "deleteItem", val: optionsItem): void;
}

const emit = defineEmits<Emits>();
withDefaults(defineProps<Props>(), {});

function handleCollect(item) {
  emit("collectItem", item);
}

function handleDelete(item) {
  emit("deleteItem", item);
}
</script>

<template>
  <component :is="useRenderIcon(item.meta?.icon)" />
  <span class="history-item-title">
    {{ item.meta?.title }}
  </span>
  <button
    v-show="item.type === 'history'"
    type="button"
    class="history-item-action mr-2 hover:text-[#d7d5d4]"
    :aria-label="`收藏${item.meta?.title ?? '菜单'}`"
    @keydown.enter.stop
    @keydown.space.stop
    @click.stop="handleCollect(item)"
  >
    <IconifyIconOffline :icon="StarIcon" aria-hidden="true" />
  </button>
  <button
    type="button"
    class="history-item-action hover:text-[#d7d5d4]"
    :aria-label="`删除${item.meta?.title ?? '菜单'}记录`"
    @keydown.enter.stop
    @keydown.space.stop
    @click.stop="handleDelete(item)"
  >
    <IconifyIconOffline :icon="CloseIcon" aria-hidden="true" />
  </button>
</template>

<style lang="scss" scoped>
.history-item-title {
  display: flex;
  flex: 1;
  margin-left: 5px;
}

.history-item-action {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 32px;
  min-height: 32px;
  padding: 0;
  color: inherit;
  cursor: pointer;
  background: transparent;
  border: 0;
  border-radius: 4px;
}

.history-item-action svg {
  width: 18px;
  height: 18px;
}

.history-item-action:focus-visible {
  outline: 3px solid var(--el-color-primary-light-5);
  outline-offset: 1px;
}
</style>
