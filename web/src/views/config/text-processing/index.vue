<script setup lang="ts">
import { onMounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  ChineseConversionMode,
  getTextProcessingConfig,
  resetTextProcessingConfig,
  saveTextProcessingConfig,
  type UpdateTextProcessingConfigRequest
} from "@/api/text-processing";

defineOptions({
  name: "TextProcessingConfig"
});

const loading = ref(false);

const formData = reactive<UpdateTextProcessingConfigRequest>({
  enableChineseConversion: false,
  conversionMode: ChineseConversionMode.None,
  enableSynonym: true,
  enableOkNgConversion: true,
  okStandardFormat: "OK",
  ngStandardFormat: "NG",
  enableKeywordHighlight: false,
  highlightColorHex: "#FFFF00"
});

const conversionModeOptions = [
  { label: "不转换", value: ChineseConversionMode.None },
  { label: "简体 → 台湾繁体", value: ChineseConversionMode.HansToTW },
  { label: "台湾繁体 → 简体", value: ChineseConversionMode.TWToHans }
];

const load = async () => {
  loading.value = true;
  try {
    const res = await getTextProcessingConfig();
    if (res.code === 0) {
      Object.assign(formData, res.data);
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
  if (!formData.okStandardFormat?.trim() || !formData.ngStandardFormat?.trim()) {
    ElMessage.warning("OK/NG 标准格式不能为空");
    return;
  }
  try {
    const res = await saveTextProcessingConfig({
      ...formData,
      okStandardFormat: formData.okStandardFormat.trim(),
      ngStandardFormat: formData.ngStandardFormat.trim(),
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
  try {
    await ElMessageBox.confirm("确定重置为默认配置吗？", "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
    const res = await resetTextProcessingConfig();
    if (res.code === 0) {
      ElMessage.success("已重置");
      Object.assign(formData, res.data);
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
  <div class="main">
    <el-card>
      <template #header>
        <div class="flex justify-between items-center">
          <span>文本处理配置</span>
          <div class="flex gap-2">
            <el-button @click="reset">重置默认</el-button>
            <el-button type="primary" @click="save">保存</el-button>
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
            class="w-full"
            :disabled="!formData.enableChineseConversion"
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
        <el-form-item label="OK 标准格式" required>
          <el-input v-model="formData.okStandardFormat" style="max-width: 240px" />
        </el-form-item>
        <el-form-item label="NG 标准格式" required>
          <el-input v-model="formData.ngStandardFormat" style="max-width: 240px" />
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
.main {
  padding: 20px;
}
</style>

