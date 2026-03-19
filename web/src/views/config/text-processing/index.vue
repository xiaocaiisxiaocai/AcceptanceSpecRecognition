<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  ChineseConversionMode,
  getTextProcessingConfig,
  resetTextProcessingConfig,
  saveTextProcessingConfig,
  type UpdateTextProcessingConfigRequest
} from "@/api/text-processing";
import { hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";

defineOptions({
  name: "TextProcessingConfig"
});

const loading = ref(false);

const formData = reactive<UpdateTextProcessingConfigRequest>({
  enableChineseConversion: false,
  conversionMode: ChineseConversionMode.None,
  enableSynonym: true,
  enableOkNgConversion: true,
  enableKeywordHighlight: false,
  highlightColorHex: "#FFFF00"
});

const conversionModeOptions = [
  { label: "不转换", value: ChineseConversionMode.None },
  { label: "简体 → 台湾繁体", value: ChineseConversionMode.HansToTW },
  { label: "台湾繁体 → 简体", value: ChineseConversionMode.TWToHans }
];

const canUpdate = computed(() => hasPerms("btn:text-processing-config:update"));
const canReset = computed(() => hasPerms("btn:text-processing-config:reset"));

const load = async () => {
  loading.value = true;
  try {
    const res = await getTextProcessingConfig();
    if (res.code === 0) {
      const { okStandardFormat, ngStandardFormat, ...rest } = res.data;
      Object.assign(formData, rest);
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("加载配置失败");
  } finally {
    loading.value = false;
  }
};

const save = async () => {
  if (!ensurePermission("btn:text-processing-config:update", "权限不足，无法保存文本处理配置")) {
    return;
  }
  try {
    const res = await saveTextProcessingConfig({
      ...formData,
      okStandardFormat: null,
      ngStandardFormat: null,
      highlightColorHex: formData.highlightColorHex?.trim() || "#FFFF00"
    });
    if (res.code === 0) {
      ElMessage.success("保存成功");
      Object.assign(formData, res.data);
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    ElMessage.error("保存失败");
  }
};

const reset = async () => {
  if (!ensurePermission("btn:text-processing-config:reset", "权限不足，无法重置文本处理配置")) {
    return;
  }
  try {
    await ElMessageBox.confirm("确定重置为默认配置吗？", "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
    const res = await resetTextProcessingConfig();
    if (res.code === 0) {
      ElMessage.success("已重置");
      const { okStandardFormat, ngStandardFormat, ...rest } = res.data;
      Object.assign(formData, rest);
    } else {
      ElMessage.error(res.message);
    }
  } catch {
    // cancelled
  }
};

onMounted(load);
</script>

<template>
  <div class="page config-page">
    <div class="page-header">
      <div>
        <div class="page-title">文本预处理</div>
        <div class="page-subtitle">配置清洗与处理策略</div>
      </div>
    </div>
    <el-card>
      <template #header>
        <div class="flex justify-between items-center">
          <span>文本处理配置</span>
          <div class="flex gap-2">
            <el-button v-if="canReset" @click="reset">重置默认</el-button>
            <el-button v-if="canUpdate" type="primary" @click="save">
              保存
            </el-button>
          </div>
        </div>
      </template>

      <el-form v-loading="loading" label-width="160px">
        <el-divider content-position="left">简繁转换</el-divider>
        <el-form-item label="启用简繁转换">
          <el-switch v-model="formData.enableChineseConversion" />
        </el-form-item>
        <el-form-item label="转换模式">
          <el-select
            v-model="formData.conversionMode"
            :disabled="!formData.enableChineseConversion"
            popper-class="config-select-popper"
          >
            <el-option
              v-for="opt in conversionModeOptions"
              :key="opt.value"
              :label="opt.label"
              :value="opt.value"
            />
          </el-select>
        </el-form-item>

        <el-divider content-position="left">同义词 / OKNG</el-divider>
        <el-form-item label="启用同义词替换">
          <el-switch v-model="formData.enableSynonym" />
        </el-form-item>
        <el-form-item label="启用 OK/NG 格式转换">
          <el-switch v-model="formData.enableOkNgConversion" />
        </el-form-item>
        <el-form-item label="OK/NG 格式">
          <span class="form-tip">由上传文件定义，无需在此配置</span>
        </el-form-item>

        <el-divider content-position="left">关键字高亮（预留）</el-divider>
        <el-form-item label="启用关键字高亮">
          <el-switch v-model="formData.enableKeywordHighlight" />
        </el-form-item>
        <el-form-item label="高亮颜色">
          <el-color-picker v-model="formData.highlightColorHex" />
          <span class="ml-2 text-sm text-gray-500">{{ formData.highlightColorHex }}</span>
        </el-form-item>
      </el-form>
    </el-card>
  </div>
</template>

<style scoped>
.page {
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.form-tip {
  font-size: 12px;
  color: #6b7280;
}

</style>

