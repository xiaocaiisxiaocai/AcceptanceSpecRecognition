<script setup lang="ts">
import { ref, watch } from "vue";
import { useNav } from "@/layout/hooks/useNav";

const screenIcon = ref();
const { toggle, isFullscreen, Fullscreen, ExitFullscreen } = useNav();

isFullscreen.value = !!(
  document.fullscreenElement ||
  document.webkitFullscreenElement ||
  document.mozFullScreenElement ||
  document.msFullscreenElement
);

watch(
  isFullscreen,
  full => {
    screenIcon.value = full ? ExitFullscreen : Fullscreen;
  },
  {
    immediate: true
  }
);
</script>

<template>
  <button
    type="button"
    class="fullscreen-icon navbar-bg-hover"
    :aria-label="isFullscreen ? '退出全屏' : '进入全屏'"
    @click="toggle"
  >
    <IconifyIconOffline :icon="screenIcon" aria-hidden="true" />
  </button>
</template>
