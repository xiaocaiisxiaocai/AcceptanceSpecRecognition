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
  <div class="page welcome">正在跳转到仪表盘...</div>
</template>

<style scoped>
.welcome {
  font-size: 14px;
  color: #6b7280;
}
</style>
