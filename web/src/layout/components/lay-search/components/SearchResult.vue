<script setup lang="ts">
// @ts-nocheck
import type { Props } from "../types";
import { useResizeObserver } from "@pureadmin/utils";
import { useEpThemeStoreHook } from "@/store/modules/epTheme";
import { useRenderIcon } from "@/components/ReIcon/src/hooks";
import { ref, computed, getCurrentInstance, onMounted } from "vue";
import EnterOutlined from "@/assets/svg/enter_outlined.svg?component";

interface Emits {
  (e: "update:value", val: string): void;
  (e: "enter"): void;
}

const resultRef = ref();
const innerHeight = ref();
const emit = defineEmits<Emits>();
const instance = getCurrentInstance()!;
const props = withDefaults(defineProps<Props>(), {});

const itemStyle = computed(() => {
  return item => {
    return {
      background:
        item?.path === active.value ? useEpThemeStoreHook().epThemeColor : "",
      color: item.path === active.value ? "#fff" : "",
      fontSize: item.path === active.value ? "16px" : "14px"
    };
  };
});

const active = computed({
  get() {
    return props.value;
  },
  set(val: string) {
    emit("update:value", val);
  }
});

/** 鼠标移入 */
async function handleMouse(item) {
  active.value = item.path;
}

function handleTo(item) {
  active.value = item.path;
  emit("enter");
}

function resizeResult() {
  // el-scrollbar max-height="calc(90vh - 140px)"
  innerHeight.value = window.innerHeight - window.innerHeight / 10 - 140;
}

useResizeObserver(resultRef, resizeResult);

function handleScroll(index: number) {
  const curInstance = instance?.proxy?.$refs[`resultItemRef${index}`];
  if (!curInstance) return 0;
  const curRef = curInstance[0] as ElRef;
  const scrollTop = curRef.offsetTop + 128; // 128 两个result-item（56px+56px=112px）高度加上下margin（8px+8px=16px）
  return scrollTop > innerHeight.value ? scrollTop - innerHeight.value : 0;
}

onMounted(() => {
  resizeResult();
});

defineExpose({ handleScroll });
</script>

<template>
  <div ref="resultRef" class="result" role="list" aria-label="菜单搜索结果">
    <button
      v-for="(item, index) in options"
      :key="item.path"
      :ref="'resultItemRef' + index"
      type="button"
      class="result-item dark:bg-[#1d1d1d]"
      :style="itemStyle(item)"
      :aria-current="item.path === active ? 'true' : undefined"
      @click="handleTo(item)"
      @mouseenter="handleMouse(item)"
    >
      <component :is="useRenderIcon(item.meta?.icon)" aria-hidden="true" />
      <span class="result-item-title">
        {{ item.meta?.title }}
      </span>
      <EnterOutlined aria-hidden="true" />
    </button>
  </div>
</template>

<style lang="scss" scoped>
.result {
  padding-bottom: 12px;

  &-item {
    display: flex;
    align-items: center;
    width: 100%;
    height: 56px;
    padding: 14px;
    margin-top: 8px;
    font-family: inherit;
    color: inherit;
    text-align: left;
    cursor: pointer;
    background: transparent;
    border: 0.1px solid #ccc;
    border-radius: 4px;
    transition: font-size 0.16s;

    &:focus-visible {
      outline: 3px solid var(--el-color-primary-light-5);
      outline-offset: 2px;
    }

    &-title {
      display: flex;
      flex: 1;
      margin-left: 5px;
    }
  }
}
</style>
