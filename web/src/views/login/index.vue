<script setup lang="ts">
import Motion from "./utils/motion";
import { useRoute, useRouter } from "vue-router";
import { message } from "@/utils/message";
import { loginRules } from "./utils/rule";
import { ref, reactive } from "vue";
import { debounce } from "@pureadmin/utils";
import { useNav } from "@/layout/hooks/useNav";
import { useEventListener } from "@vueuse/core";
import type { FormInstance } from "element-plus";
import { useUserStoreHook } from "@/store/modules/user";
import { initRouter, getTopMenu } from "@/router/utils";
import { useRenderIcon } from "@/components/ReIcon/src/hooks";
import { useDataThemeChange } from "@/layout/hooks/useDataThemeChange";
import { getRequestErrorMessage } from "@/utils/error-message";

import dayIcon from "@/assets/svg/day.svg?component";
import darkIcon from "@/assets/svg/dark.svg?component";
import Lock from "~icons/ri/lock-fill";
import User from "~icons/ri/user-3-fill";
import Eye from "~icons/ri/eye-line";
import EyeOff from "~icons/ri/eye-off-line";

defineOptions({
  name: "Login"
});

const router = useRouter();
const route = useRoute();
const loading = ref(false);
const disabled = ref(false);
const ruleFormRef = ref<FormInstance>();
const passwordVisible = ref(false);

const { dataTheme, overallStyle, dataThemeChange } = useDataThemeChange();
dataThemeChange(overallStyle.value);
const { title } = useNav();
const loginLogoUrl = "/SAA.svg";

const ruleForm = reactive({
  username: "",
  password: ""
});

const getRedirectPath = () => {
  const rawRedirect = route.query.redirect;
  if (
    typeof rawRedirect === "string" &&
    rawRedirect.startsWith("/") &&
    rawRedirect !== "/login"
  ) {
    return rawRedirect;
  }

  return null;
};

const onLogin = async (formEl: FormInstance | undefined) => {
  if (!formEl) return;
  await formEl.validate(valid => {
    if (valid) {
      loading.value = true;
      disabled.value = true;
      useUserStoreHook()
        .loginByUsername({
          username: ruleForm.username,
          password: ruleForm.password
        })
        .then(async res => {
          if (!res.success) {
            message("登录失败", { type: "error" });
            return;
          }

          await initRouter();
          const redirectPath = getRedirectPath();
          if (redirectPath) {
            await router.push(redirectPath);
            message("登录成功", { type: "success" });
            return;
          }

          const topMenu = getTopMenu(true);
          if (!topMenu?.path) {
            message("登录成功，但当前账号无可访问菜单，请联系管理员", {
              type: "warning"
            });
            return;
          }

          await router.push(topMenu.path);
          message("登录成功", { type: "success" });
        })
        .catch((error: unknown) => {
          message(
            getRequestErrorMessage(error, "登录成功后跳转失败，请稍后重试"),
            {
              type: "error"
            }
          );
        })
        .finally(() => {
          loading.value = false;
          disabled.value = false;
        });
    }
  });
};

const immediateDebounce: any = debounce(
  formRef => onLogin(formRef),
  1000,
  true
);

useEventListener(document, "keydown", ({ code }) => {
  if (
    ["Enter", "NumpadEnter"].includes(code) &&
    !disabled.value &&
    !loading.value
  )
    immediateDebounce(ruleFormRef.value);
});
</script>

<template>
  <div class="select-none login-page">
    <div class="flex-c absolute right-5 top-3">
      <!-- 主题 -->
      <el-switch
        v-model="dataTheme"
        inline-prompt
        :active-icon="dayIcon"
        :inactive-icon="darkIcon"
        @change="
          (val: string | number | boolean) => dataThemeChange(String(val))
        "
      />
    </div>
    <div class="login-container">
      <div class="login-box">
        <div class="login-form">
          <img class="avatar" :src="loginLogoUrl" alt="SAA" />
          <Motion>
            <h2 class="outline-hidden">{{ title }}</h2>
          </Motion>

          <el-form
            ref="ruleFormRef"
            :model="ruleForm"
            :rules="loginRules"
            size="large"
          >
            <Motion :delay="100">
              <el-form-item
                :rules="[
                  {
                    required: true,
                    message: '请输入账号',
                    trigger: 'blur'
                  }
                ]"
                prop="username"
              >
                <el-input
                  v-model="ruleForm.username"
                  clearable
                  placeholder="账号"
                  :prefix-icon="useRenderIcon(User)"
                />
              </el-form-item>
            </Motion>

            <Motion :delay="150">
              <el-form-item prop="password">
                <el-input
                  v-model="ruleForm.password"
                  clearable
                  :type="passwordVisible ? 'text' : 'password'"
                  placeholder="密码"
                  :prefix-icon="useRenderIcon(Lock)"
                >
                  <template #suffix>
                    <button
                      type="button"
                      class="password-visibility-toggle"
                      :aria-label="passwordVisible ? '隐藏密码' : '显示密码'"
                      @click="passwordVisible = !passwordVisible"
                    >
                      <el-icon>
                        <component :is="passwordVisible ? EyeOff : Eye" />
                      </el-icon>
                    </button>
                  </template>
                </el-input>
              </el-form-item>
            </Motion>

            <Motion :delay="250">
              <el-button
                class="w-full mt-4!"
                size="default"
                type="primary"
                :loading="loading"
                :disabled="disabled"
                @click="onLogin(ruleFormRef)"
              >
                登录
              </el-button>
            </Motion>
          </el-form>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
@import url("@/style/login.css");
</style>

<style lang="scss" scoped>
:deep(.el-input-group__append, .el-input-group__prepend) {
  padding: 0;
}

.password-visibility-toggle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 4px;
  color: var(--app-text-secondary);
  cursor: pointer;
  background: transparent;
  border: 0;
  border-radius: 4px;
}

.password-visibility-toggle:focus-visible {
  outline: 2px solid var(--app-primary);
  outline-offset: 1px;
}
</style>
