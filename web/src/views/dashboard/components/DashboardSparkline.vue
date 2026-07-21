<script setup lang="ts">
import { computed } from "vue";
import { buildSparklineGeometry } from "./dashboard-sparkline";

const props = withDefaults(
  defineProps<{
    values: number[];
    labels: string[];
    colorToken?: string;
    loading?: boolean;
  }>(),
  {
    colorToken: "--app-primary",
    loading: false
  }
);

const geometry = computed(() => buildSparklineGeometry(props.values));
const accessibleDescription = computed(() => {
  if (props.loading) return "趋势数据加载中";
  if (props.values.length === 0) return "所选周期暂无趋势数据";

  return props.values
    .map(
      (value, index) =>
        `${props.labels[index] ?? `第 ${index + 1} 天`}：${value}`
    )
    .join("，");
});
</script>

<template>
  <div
    v-loading="loading"
    class="dashboard-sparkline"
    role="img"
    :aria-label="accessibleDescription"
    :style="{ '--sparkline-color': `var(${colorToken})` }"
  >
    <svg
      v-if="values.length > 0"
      viewBox="0 0 100 36"
      preserveAspectRatio="none"
      aria-hidden="true"
      focusable="false"
    >
      <path class="sparkline-area" :d="geometry.areaPath" />
      <polyline class="sparkline-line" :points="geometry.points" />
    </svg>
    <span v-else class="sparkline-empty">暂无数据</span>
  </div>
</template>

<style scoped>
.dashboard-sparkline {
  --sparkline-color: var(--app-primary);

  display: flex;
  align-items: center;
  width: 100%;
  height: 44px;
}

.dashboard-sparkline svg {
  width: 100%;
  height: 36px;
  overflow: visible;
}

.sparkline-area {
  fill: color-mix(in srgb, var(--sparkline-color) 16%, transparent);
}

.sparkline-line {
  fill: none;
  stroke: var(--sparkline-color);
  stroke-width: 2;
  stroke-linecap: round;
  stroke-linejoin: round;
  vector-effect: non-scaling-stroke;
}

.sparkline-empty {
  font-size: 12px;
  color: var(--app-text-secondary);
}
</style>
