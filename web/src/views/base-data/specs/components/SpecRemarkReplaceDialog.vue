<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  executeSpecRemarkReplace,
  previewSpecRemarkReplace,
  type SpecRemarkReplacePreviewResponse,
  type SpecRemarkReplaceResult
} from "@/api/spec";
import {
  getRequestErrorMessage,
  isGloballyHandledAuthError
} from "@/utils/error-message";

const props = defineProps<{
  modelValue: boolean;
  orgUnitId: number;
  scopeLabel: string;
}>();

const emit = defineEmits<{
  "update:modelValue": [value: boolean];
  success: [result: SpecRemarkReplaceResult];
}>();

const visible = computed({
  get: () => props.modelValue,
  set: value => emit("update:modelValue", value)
});
const form = reactive({
  searchText: "",
  replacementText: ""
});
const preview = ref<SpecRemarkReplacePreviewResponse | null>(null);
const previewPageSize = 10;
const previewLoading = ref(false);
const executeLoading = ref(false);

const reset = () => {
  form.searchText = "";
  form.replacementText = "";
  preview.value = null;
  previewLoading.value = false;
  executeLoading.value = false;
};

watch(
  () => props.modelValue,
  opened => {
    if (opened) reset();
  }
);

const invalidatePreview = () => {
  preview.value = null;
};

const handlePreview = async (page = 1) => {
  if (!form.searchText.trim()) {
    ElMessage.warning("请输入要查找的备注内容");
    return;
  }

  previewLoading.value = true;
  try {
    const res = await previewSpecRemarkReplace({
      orgUnitId: props.orgUnitId,
      searchText: form.searchText,
      replacementText: form.replacementText,
      page,
      pageSize: previewPageSize
    });
    if (res.code !== 0) {
      ElMessage.error(res.message);
      return;
    }
    preview.value = res.data;
    if (res.data.affectedSpecCount === 0) {
      ElMessage.info("当前部门没有包含该内容的备注");
    }
  } catch (error) {
    if (!isGloballyHandledAuthError(error)) {
      ElMessage.error(getRequestErrorMessage(error, "预览替换影响失败"));
    }
  } finally {
    previewLoading.value = false;
  }
};

const handlePreviewPageChange = (page: number) => {
  void handlePreview(page);
};

const handleExecute = async () => {
  if (!preview.value || preview.value.affectedSpecCount === 0) return;

  try {
    await ElMessageBox.confirm(
      `将修改“${props.scopeLabel}”内 ${preview.value.affectedSpecCount} 条规格，共替换 ${preview.value.matchCount} 处备注内容。是否继续？`,
      "确认批量替换",
      {
        confirmButtonText: "确认替换",
        cancelButtonText: "返回检查",
        type: "warning"
      }
    );
  } catch {
    return;
  }

  executeLoading.value = true;
  try {
    const res = await executeSpecRemarkReplace({
      orgUnitId: props.orgUnitId,
      searchText: form.searchText,
      replacementText: form.replacementText,
      expectedAffectedSpecCount: preview.value.affectedSpecCount,
      expectedMatchCount: preview.value.matchCount,
      confirmationToken: preview.value.confirmationToken
    });
    if (res.code !== 0) {
      ElMessage.error(res.message);
      return;
    }
    ElMessage.success(
      `已更新 ${res.data.updatedSpecCount} 条规格，替换 ${res.data.replacedMatchCount} 处`
    );
    visible.value = false;
    emit("success", res.data);
  } catch (error) {
    if (isGloballyHandledAuthError(error)) return;
    const message = getRequestErrorMessage(error, "批量替换失败");
    if (message.includes("重新预览")) {
      preview.value = null;
      ElMessage.warning(message);
      return;
    }
    ElMessage.error(message);
  } finally {
    executeLoading.value = false;
  }
};
</script>

<template>
  <el-dialog
    v-model="visible"
    title="批量替换备注"
    width="min(760px, calc(100vw - 32px))"
    destroy-on-close
    :close-on-click-modal="!executeLoading"
  >
    <div class="remark-replace">
      <div class="scope-line">
        <span class="scope-line__label">操作范围</span>
        <el-tag type="primary" effect="plain">{{ scopeLabel }}</el-tag>
        <span>仅修改该部门的备注列，不影响其他部门。</span>
      </div>

      <el-form label-position="top">
        <el-form-item label="查找内容" required>
          <el-input
            v-model="form.searchText"
            maxlength="2000"
            placeholder="输入备注中需要统一替换的固定文本"
            @input="invalidatePreview"
          />
        </el-form-item>
        <el-form-item label="替换为">
          <el-input
            v-model="form.replacementText"
            maxlength="2000"
            placeholder="留空表示删除查找内容"
            @input="invalidatePreview"
          />
        </el-form-item>
      </el-form>

      <template v-if="preview">
        <div class="impact-line">
          <span
            >影响规格 <strong>{{ preview.affectedSpecCount }}</strong> 条</span
          >
          <span class="impact-line__divider" />
          <span
            >替换位置 <strong>{{ preview.matchCount }}</strong> 处</span
          >
          <span class="impact-line__note">重新修改文本后需要重新预览</span>
        </div>

        <el-table
          v-if="preview.samples.length"
          v-loading="previewLoading"
          :data="preview.samples"
          border
          max-height="min(440px, calc(100vh - 420px))"
          class="sample-table"
        >
          <el-table-column
            prop="project"
            label="项目"
            width="150"
            show-overflow-tooltip
          />
          <el-table-column
            prop="beforePreview"
            label="替换前"
            min-width="220"
            show-overflow-tooltip
          />
          <el-table-column
            prop="afterPreview"
            label="替换后"
            min-width="220"
            show-overflow-tooltip
          />
        </el-table>
        <div v-if="preview.samples.length" class="sample-pagination">
          <span>
            本页 {{ preview.samples.length }} 条，共
            {{ preview.sampleTotal }} 条
          </span>
          <el-pagination
            v-if="preview.sampleTotal > preview.samplePageSize"
            background
            layout="prev, pager, next"
            :current-page="preview.samplePage"
            :page-size="preview.samplePageSize"
            :total="preview.sampleTotal"
            :disabled="previewLoading"
            @current-change="handlePreviewPageChange"
          />
        </div>
        <el-empty
          v-else
          description="当前部门没有可替换的备注"
          :image-size="64"
        />
      </template>
    </div>

    <template #footer>
      <el-button :disabled="executeLoading" @click="visible = false"
        >取消</el-button
      >
      <el-button
        v-if="!preview"
        type="primary"
        :loading="previewLoading"
        @click="handlePreview()"
      >
        预览影响
      </el-button>
      <el-button v-else :loading="previewLoading" @click="handlePreview()">
        重新预览
      </el-button>
      <el-button
        v-if="preview"
        type="danger"
        :disabled="preview.affectedSpecCount === 0"
        :loading="executeLoading"
        @click="handleExecute"
      >
        确认替换
      </el-button>
    </template>
  </el-dialog>
</template>

<style scoped>
.remark-replace {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.scope-line {
  display: flex;
  align-items: center;
  min-height: 40px;
  padding: 8px 12px;
  color: var(--el-text-color-regular);
  background: var(--el-fill-color-light);
  border-left: 3px solid var(--el-color-primary);
}

.scope-line__label {
  margin-right: 10px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.scope-line .el-tag {
  margin-right: 10px;
}

.impact-line {
  display: flex;
  align-items: center;
  min-height: 44px;
  padding: 0 14px;
  color: var(--el-text-color-regular);
  border: 1px solid var(--el-border-color);
  border-radius: 4px;
}

.impact-line strong {
  margin: 0 2px;
  font-size: 18px;
  color: var(--el-color-primary);
}

.impact-line__divider {
  width: 1px;
  height: 18px;
  margin: 0 16px;
  background: var(--el-border-color);
}

.impact-line__note {
  margin-left: auto;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

.sample-table {
  width: 100%;
}

.sample-pagination {
  display: flex;
  align-items: center;
  justify-content: space-between;
  min-height: 32px;
  margin-top: 10px;
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

@media (width <= 640px) {
  .scope-line,
  .impact-line {
    flex-wrap: wrap;
    gap: 6px;
    align-items: flex-start;
  }

  .impact-line {
    padding: 10px 12px;
  }

  .impact-line__note {
    width: 100%;
    margin-left: 0;
  }

  .sample-pagination {
    flex-wrap: wrap;
    gap: 8px;
  }
}
</style>
