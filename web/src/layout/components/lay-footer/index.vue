<script setup lang="ts">
import { getConfig } from "@/config";

const TITLE = getConfig("Title");
const YEAR_TEXT = new Date().getFullYear().toString();
</script>

<template>
  <footer
    class="layout-footer text-[rgba(0,0,0,0.6)] dark:text-[rgba(220,220,242,0.8)]"
  >
    Copyright © {{ YEAR_TEXT }}
    <router-link class="hover:text-primary!" to="/dashboard">
      &nbsp;{{ TITLE }}
    </router-link>
  </footer>
</template>

<style lang="scss" scoped>
.layout-footer {
  display: flex;
  flex-shrink: 0;
  align-items: center;
  justify-content: center;
  width: 100%;
  min-height: var(--app-layout-footer-height, 29px);
  padding: 0 0 8px;
  font-size: 14px;
}
</style>
