<script setup lang="ts">
import { useBoolean } from "../../hooks/useBoolean";
import SearchModal from "./components/SearchModal.vue";

const { bool: show, toggle } = useBoolean();
function handleSearch() {
  toggle();
}
</script>

<template>
  <div>
    <button
      type="button"
      class="search-container h-[48px] flex-c navbar-bg-hover"
      aria-label="搜索菜单"
      aria-haspopup="dialog"
      :aria-expanded="show"
      @click="handleSearch"
    >
      <IconifyIconOffline icon="ri/search-line" aria-hidden="true" />
    </button>
    <SearchModal v-model:value="show" />
  </div>
</template>
