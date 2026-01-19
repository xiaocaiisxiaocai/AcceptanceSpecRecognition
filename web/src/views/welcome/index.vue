<script setup lang="ts">
import { onMounted } from "vue";
import { useRouter } from "vue-router";

defineOptions({
  name: "Welcome"
});

const router = useRouter();

onMounted(() => {
  router.replace("/dashboard");
});
</script>

<template>
  <div style="padding: 20px">正在跳转到仪表盘...</div>
</template>
