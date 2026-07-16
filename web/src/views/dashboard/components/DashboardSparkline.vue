<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from "vue";
import echarts from "@/plugins/echarts";

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

const chartElement = ref<HTMLDivElement>();
let chart: ReturnType<typeof echarts.init> | undefined;
let resizeObserver: ResizeObserver | undefined;
let themeObserver: MutationObserver | undefined;

const renderChart = () => {
  if (!chartElement.value) return;

  chart ??= echarts.init(chartElement.value, undefined, { renderer: "svg" });
  const styles = getComputedStyle(document.documentElement);
  const lineColor =
    styles.getPropertyValue(props.colorToken).trim() || "#409eff";

  chart.setOption(
    {
      animationDuration: 360,
      grid: { top: 5, right: 2, bottom: 5, left: 2 },
      tooltip: {
        trigger: "axis",
        confine: true,
        formatter: (items: Array<{ axisValue: string; value: number }>) => {
          const item = items[0];
          return item
            ? `${item.axisValue}<br/>${item.value.toLocaleString("zh-CN")}`
            : "";
        }
      },
      xAxis: {
        type: "category",
        boundaryGap: false,
        data: props.labels,
        show: false
      },
      yAxis: {
        type: "value",
        min: 0,
        show: false
      },
      series: [
        {
          type: "line",
          data: props.values,
          showSymbol: false,
          smooth: 0.35,
          silent: props.loading,
          lineStyle: { width: 2, color: lineColor },
          areaStyle: { color: lineColor, opacity: 0.16 }
        }
      ]
    },
    true
  );
};

watch(
  () => [props.values, props.labels, props.loading],
  () => void nextTick(renderChart),
  { deep: true }
);

onMounted(() => {
  renderChart();
  resizeObserver = new ResizeObserver(() => chart?.resize());
  resizeObserver.observe(chartElement.value!);
  themeObserver = new MutationObserver(renderChart);
  themeObserver.observe(document.documentElement, {
    attributes: true,
    attributeFilter: ["class", "data-theme"]
  });
});

onBeforeUnmount(() => {
  resizeObserver?.disconnect();
  themeObserver?.disconnect();
  chart?.dispose();
});
</script>

<template>
  <div
    ref="chartElement"
    v-loading="loading"
    class="dashboard-sparkline"
    role="img"
    :aria-label="`最近 7 天趋势：${values.join('、')}`"
  />
</template>

<style scoped>
.dashboard-sparkline {
  width: 100%;
  height: 44px;
}
</style>
