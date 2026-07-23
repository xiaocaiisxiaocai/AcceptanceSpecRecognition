<script setup lang="ts">
import { emitter } from "@/utils/mitt";
import { useNav } from "@/layout/hooks/useNav";
import LaySearch from "../lay-search/index.vue";
import { responsiveStorageNameSpace } from "@/config";
import { ref, nextTick, computed, onMounted } from "vue";
import { storageLocal, isAllEmpty } from "@pureadmin/utils";
import { usePermissionStoreHook } from "@/store/modules/permission";
import type { menuType } from "@/layout/types";
import LaySidebarItem from "../lay-sidebar/components/SidebarItem.vue";
import LaySidebarFullScreen from "../lay-sidebar/components/SidebarFullScreen.vue";

import LogoutCircleRLine from "~icons/ri/logout-circle-r-line";
import Setting from "~icons/ri/settings-3-line";

const menuRef = ref();
const showLogo = ref(
  storageLocal().getItem<StorageConfigs>(
    `${responsiveStorageNameSpace()}configure`
  )?.showLogo ?? true
);
const wholeMenus = computed(
  () => usePermissionStoreHook().wholeMenus as unknown as menuType[]
);

const {
  route,
  title,
  logout,
  onPanel,
  getLogo,
  username,
  userAvatar,
  backTopMenu,
  avatarsStyle
} = useNav();

const defaultActive = computed(() =>
  !isAllEmpty(route.meta?.activePath) ? route.meta.activePath : route.path
);

nextTick(() => {
  menuRef.value?.handleResize();
});

onMounted(() => {
  emitter.on("logoChange", key => {
    showLogo.value = key;
  });
});
</script>

<template>
  <div v-loading="wholeMenus.length === 0" class="horizontal-header">
    <button
      v-if="showLogo"
      type="button"
      class="horizontal-header-left"
      :aria-label="`返回${title}首页`"
      @click="backTopMenu"
    >
      <img :src="getLogo()" alt="" />
      <span>{{ title }}</span>
    </button>
    <el-menu
      ref="menuRef"
      mode="horizontal"
      popper-class="pure-scrollbar"
      class="horizontal-header-menu"
      :default-active="defaultActive"
    >
      <LaySidebarItem
        v-for="route in wholeMenus"
        :key="route.path"
        :item="route"
        :base-path="route.path"
      />
    </el-menu>
    <div class="horizontal-header-right">
      <!-- 菜单搜索 -->
      <LaySearch id="header-search" />
      <!-- 全屏 -->
      <LaySidebarFullScreen id="full-screen" />
      <!-- 退出登录 -->
      <el-dropdown trigger="click">
        <button
          type="button"
          class="el-dropdown-link navbar-bg-hover"
          aria-haspopup="menu"
          :aria-label="`${username || '当前用户'}账户菜单`"
        >
          <img
            :src="userAvatar"
            :style="avatarsStyle"
            :alt="`${username || '当前用户'}头像`"
          />
          <p v-if="username" class="dark:text-white">{{ username }}</p>
        </button>
        <template #dropdown>
          <el-dropdown-menu class="logout">
            <el-dropdown-item @click="logout">
              <IconifyIconOffline
                :icon="LogoutCircleRLine"
                style="margin: 5px"
              />
              退出系统
            </el-dropdown-item>
          </el-dropdown-menu>
        </template>
      </el-dropdown>
      <button
        type="button"
        class="set-icon navbar-bg-hover"
        aria-label="打开系统配置"
        @click="onPanel"
      >
        <IconifyIconOffline :icon="Setting" aria-hidden="true" />
      </button>
    </div>
  </div>
</template>

<style lang="scss" scoped>
:deep(.el-loading-mask) {
  opacity: 0.45;
}

.logout {
  width: 120px;

  ::v-deep(.el-dropdown-menu__item) {
    display: inline-flex;
    flex-wrap: wrap;
    min-width: 100%;
  }
}
</style>
