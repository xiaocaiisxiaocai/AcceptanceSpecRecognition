<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import FileUpload from "@/views/data-import/components/FileUpload.vue";
import { deleteFile, getFileList, type FileUploadResponse, type WordFile } from "@/api/document";
import {
  generateMatchingKnowledgeDraft,
  type MatchingKnowledgeDraftCategory,
  type MatchingKnowledgeDraftItem
} from "@/api/matching-knowledge";

type DraftInputMode = "text" | "documents" | "temporaryUpload";
type TemporaryUploadMode = "temporary" | "keep";

interface EditableDraftRow {
  id: number;
  selected: boolean;
  key: string;
  value: string;
  evidenceSnippet: string;
  reason: string;
  status: string;
  statusMessage?: string;
  isManual: boolean;
}

const props = defineProps<{
  visible: boolean;
  category: MatchingKnowledgeDraftCategory;
}>();

const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
  (e: "import", payload: {
    category: MatchingKnowledgeDraftCategory;
    items: MatchingKnowledgeDraftItem[];
  }): void;
}>();

const dialogVisible = computed({
  get: () => props.visible,
  set: value => emit("update:visible", value)
});

const inputMode = ref<DraftInputMode>("text");
const inputText = ref("");
const existingFilesLoading = ref(false);
const existingFiles = ref<WordFile[]>([]);
const selectedExistingFileIds = ref<number[]>([]);
const temporaryUploadedFile = ref<FileUploadResponse | null>(null);
const temporaryUploadMode = ref<TemporaryUploadMode>("temporary");
const generating = ref(false);
const draftRows = ref<EditableDraftRow[]>([]);

let nextRowId = 1;

const allocateRowId = () => {
  const id = nextRowId;
  nextRowId += 1;
  return id;
};

const categoryMeta = computed(() => {
  switch (props.category) {
    case "entityAliases":
      return {
        title: "实体别名",
        description: "抽取品牌、组织、厂商等实体别名映射",
        keyLabel: "别名",
        valueLabel: "标准实体"
      };
    case "unitAliases":
      return {
        title: "单位规则",
        description: "抽取单位别名映射，不生成倍率或换算系数",
        keyLabel: "单位别名",
        valueLabel: "标准单位"
      };
    case "fieldAliases":
      return {
        title: "字段别名",
        description: "抽取业务字段、列名、缩写到标准字段的映射",
        keyLabel: "字段别名",
        valueLabel: "标准字段"
      };
    case "conflictPairs":
      return {
        title: "冲突词对",
        description: "抽取明确互斥、不能同时成立的对立语义",
        keyLabel: "左侧词",
        valueLabel: "右侧词"
      };
    default:
      return {
        title: "匹配知识",
        description: "生成匹配知识草稿候选",
        keyLabel: "键",
        valueLabel: "值"
      };
  }
});

const dialogTitle = computed(
  () => `AI 生成候选 - ${categoryMeta.value.title}`
);
const selectedDraftCount = computed(
  () => draftRows.value.filter(row => row.selected).length
);
const draftStatusSummary = computed(() => {
  const ready = draftRows.value.filter(row => row.status === "ready").length;
  const duplicate = draftRows.value.filter(
    row => row.status === "duplicate"
  ).length;
  const conflict = draftRows.value.filter(
    row => row.status === "conflict"
  ).length;

  return { ready, duplicate, conflict };
});

const temporaryUploadedFileModel = computed<FileUploadResponse | null>({
  get: () => temporaryUploadedFile.value,
  set: value => {
    const previous = temporaryUploadedFile.value;
    temporaryUploadedFile.value = value;

    if (
      previous &&
      previous.fileId !== value?.fileId &&
      temporaryUploadMode.value === "temporary"
    ) {
      void deleteTemporaryFile(previous.fileId);
    }
  }
});

watch(
  () => dialogVisible.value,
  visible => {
    if (visible) {
      void loadExistingFiles();
      return;
    }

    void resetDialogState();
  }
);

watch(inputMode, (next, previous) => {
  if (previous === "temporaryUpload" && next !== "temporaryUpload") {
    void cleanupTemporaryUpload();
  }

  if (next === "documents") {
    void loadExistingFiles();
  }
});

const createDraftRow = (
  item?: Partial<MatchingKnowledgeDraftItem> & { selected?: boolean; isManual?: boolean }
): EditableDraftRow => ({
  id: allocateRowId(),
  selected: item?.selected ?? item?.status === "ready",
  key: item?.key ?? "",
  value: item?.value ?? "",
  evidenceSnippet: item?.evidenceSnippet ?? "",
  reason: item?.reason ?? "",
  status: item?.status ?? "ready",
  statusMessage: item?.statusMessage,
  isManual: item?.isManual ?? false
});

const loadExistingFiles = async () => {
  if (existingFilesLoading.value) {
    return;
  }

  existingFilesLoading.value = true;
  try {
    const res = await getFileList({ page: 1, pageSize: 100 });
    if (res.code === 0) {
      existingFiles.value = res.data.items;
    } else {
      ElMessage.error(res.message || "加载已上传文档失败");
    }
  } catch {
    ElMessage.error("加载已上传文档失败");
  } finally {
    existingFilesLoading.value = false;
  }
};

const deleteTemporaryFile = async (fileId: number) => {
  try {
    await deleteFile(fileId);
  } catch {
    ElMessage.warning("临时上传文档清理失败，请稍后手动删除");
  }
};

const cleanupTemporaryUpload = async () => {
  const file = temporaryUploadedFile.value;
  const shouldDelete = temporaryUploadMode.value === "temporary";
  temporaryUploadedFile.value = null;

  if (file && shouldDelete) {
    await deleteTemporaryFile(file.fileId);
  }
};

const resetDialogState = async () => {
  await cleanupTemporaryUpload();
  inputMode.value = "text";
  inputText.value = "";
  selectedExistingFileIds.value = [];
  temporaryUploadMode.value = "temporary";
  draftRows.value = [];
  generating.value = false;
};

const handleExistingFileSelectionChange = (rows: WordFile[]) => {
  selectedExistingFileIds.value = rows.map(row => row.id);
};

const getStatusTagType = (status: string) => {
  if (status === "ready") {
    return "success";
  }

  if (status === "duplicate") {
    return "info";
  }

  if (status === "conflict") {
    return "warning";
  }

  return "info";
};

const validateSource = () => {
  if (inputMode.value === "text") {
    if (!inputText.value.trim()) {
      ElMessage.warning("请先输入用于生成候选的文本");
      return false;
    }

    return true;
  }

  if (inputMode.value === "documents") {
    if (selectedExistingFileIds.value.length === 0) {
      ElMessage.warning("请至少选择一份已上传文档");
      return false;
    }

    return true;
  }

  if (!temporaryUploadedFile.value?.fileId) {
    ElMessage.warning("请先上传临时文档");
    return false;
  }

  return true;
};

const handleGenerate = async () => {
  if (!validateSource()) {
    return;
  }

  if (draftRows.value.length > 0) {
    try {
      await ElMessageBox.confirm(
        "重新生成会覆盖当前草稿中的编辑结果，是否继续？",
        "提示",
        {
          type: "warning",
          confirmButtonText: "继续生成",
          cancelButtonText: "取消"
        }
      );
    } catch {
      return;
    }
  }

  generating.value = true;
  try {
    const res = await generateMatchingKnowledgeDraft({
      category: props.category,
      sourceType: inputMode.value === "text" ? "text" : "documents",
      inputText: inputMode.value === "text" ? inputText.value.trim() : undefined,
      fileIds:
        inputMode.value === "documents"
          ? selectedExistingFileIds.value
          : inputMode.value === "temporaryUpload" && temporaryUploadedFile.value
            ? [temporaryUploadedFile.value.fileId]
            : undefined
    });

    if (res.code !== 0) {
      ElMessage.error(res.message || "生成候选失败");
      return;
    }

    draftRows.value = res.data.items.map(item => createDraftRow(item));
    ElMessage.success(
      `已生成 ${draftRows.value.length} 条候选，待确认 ${draftStatusSummary.value.ready} 条`
    );
  } catch {
    ElMessage.error("生成候选失败");
  } finally {
    generating.value = false;
  }
};

const addManualRow = () => {
  draftRows.value.unshift(
    createDraftRow({
      key: "",
      value: "",
      evidenceSnippet: "",
      reason: "手动补充",
      status: "ready",
      selected: true,
      isManual: true
    })
  );
};

const removeDraftRow = (id: number) => {
  const index = draftRows.value.findIndex(row => row.id === id);
  if (index >= 0) {
    draftRows.value.splice(index, 1);
  }
};

const handleImport = () => {
  const selectedRows = draftRows.value.filter(row => row.selected);
  if (selectedRows.length === 0) {
    ElMessage.warning("请至少选择一条候选再导入");
    return;
  }

  const incomplete = selectedRows.find(
    row => !row.key.trim() || !row.value.trim()
  );
  if (incomplete) {
    ElMessage.warning("存在未填写完整的候选项，请补全后再导入");
    return;
  }

  emit("import", {
    category: props.category,
    items: selectedRows.map(row => ({
      key: row.key.trim(),
      value: row.value.trim(),
      evidenceSnippet: row.evidenceSnippet.trim(),
      reason: row.reason.trim(),
      status: row.status,
      statusMessage: row.statusMessage
    }))
  });
  dialogVisible.value = false;
};
</script>

<template>
  <el-dialog
    v-model="dialogVisible"
    :title="dialogTitle"
    width="1100px"
    destroy-on-close
  >
    <div class="draft-dialog">
      <el-alert
        type="info"
        show-icon
        :closable="false"
        :title="`${categoryMeta.description}。生成结果只会导入到“自定义扩展”，不会改动系统内置规则。`"
      />

      <el-card class="source-card" shadow="never">
        <template #header>
          <div class="card-header">
            <div>
              <div class="card-title">输入来源</div>
              <div class="card-subtitle">
                每次只生成当前分类候选，可从粘贴文本、已上传文档或临时上传文档中抽取
              </div>
            </div>
            <el-button type="primary" :loading="generating" @click="handleGenerate">
              生成当前分类候选
            </el-button>
          </div>
        </template>

        <el-radio-group v-model="inputMode" class="source-mode-group">
          <el-radio-button label="粘贴文本" value="text" />
          <el-radio-button label="已上传文档" value="documents" />
          <el-radio-button label="临时上传文档" value="temporaryUpload" />
        </el-radio-group>

        <div v-if="inputMode === 'text'" class="source-panel">
          <el-input
            v-model="inputText"
            type="textarea"
            :rows="8"
            resize="vertical"
            placeholder="粘贴术语、规格、项目说明或客户文档片段，AI 将只为当前分类生成候选。"
          />
        </div>

        <div v-else-if="inputMode === 'documents'" class="source-panel">
          <div class="existing-files-toolbar">
            <div class="helper-text">
              可多选已上传文档，系统会抽取表头与预览文本用于生成当前分类候选
            </div>
            <el-button link type="primary" :loading="existingFilesLoading" @click="loadExistingFiles">
              刷新列表
            </el-button>
          </div>
          <el-table
            :data="existingFiles"
            row-key="id"
            max-height="280"
            border
            @selection-change="handleExistingFileSelectionChange"
          >
            <el-table-column type="selection" width="52" />
            <el-table-column prop="fileName" label="文件名" min-width="260" />
            <el-table-column label="类型" width="110">
              <template #default="{ row }">
                {{ row.fileType === 1 ? "Excel" : "Word" }}
              </template>
            </el-table-column>
            <el-table-column prop="specCount" label="已导入验规数" width="120" />
            <el-table-column prop="uploadedAt" label="上传时间" min-width="180" />
          </el-table>
        </div>

        <div v-else class="source-panel">
          <div class="upload-mode-row">
            <span class="helper-text">临时文档用完可自动删除，也可以保留到系统文档列表</span>
            <el-radio-group v-model="temporaryUploadMode">
              <el-radio label="仅本次使用" value="temporary" />
              <el-radio label="保存到已上传文档" value="keep" />
            </el-radio-group>
          </div>
          <FileUpload v-model="temporaryUploadedFileModel" />
        </div>
      </el-card>

      <el-card class="draft-card" shadow="never">
        <template #header>
          <div class="card-header">
            <div>
              <div class="card-title">草稿候选</div>
              <div class="card-subtitle">
                可勾选、编辑、删除或手动新增；导入时只会写入自定义扩展
              </div>
            </div>
            <div class="draft-actions">
              <span class="helper-text">
                已选 {{ selectedDraftCount }} 条，待确认 {{ draftStatusSummary.ready }} 条，重复
                {{ draftStatusSummary.duplicate }} 条，冲突 {{ draftStatusSummary.conflict }} 条
              </span>
              <el-button link type="primary" @click="addManualRow">新增一条</el-button>
            </div>
          </div>
        </template>

        <el-empty
          v-if="draftRows.length === 0"
          description="选择输入来源后点击“生成当前分类候选”，或手动新增草稿条目。"
        />

        <el-table
          v-else
          :data="draftRows"
          row-key="id"
          border
          max-height="360"
        >
          <el-table-column label="导入" width="72" align="center">
            <template #default="{ row }">
              <el-checkbox v-model="row.selected" />
            </template>
          </el-table-column>
          <el-table-column :label="categoryMeta.keyLabel" min-width="180">
            <template #default="{ row }">
              <el-input v-model="row.key" :placeholder="`输入${categoryMeta.keyLabel}`" />
            </template>
          </el-table-column>
          <el-table-column :label="categoryMeta.valueLabel" min-width="180">
            <template #default="{ row }">
              <el-input v-model="row.value" :placeholder="`输入${categoryMeta.valueLabel}`" />
            </template>
          </el-table-column>
          <el-table-column label="状态" width="120">
            <template #default="{ row }">
              <el-tag :type="getStatusTagType(row.status)" effect="plain">
                {{ row.status }}
              </el-tag>
            </template>
          </el-table-column>
          <el-table-column label="状态说明" min-width="220" show-overflow-tooltip>
            <template #default="{ row }">
              {{ row.statusMessage || (row.isManual ? "手动新增候选" : "可直接导入") }}
            </template>
          </el-table-column>
          <el-table-column label="证据片段" min-width="220" show-overflow-tooltip>
            <template #default="{ row }">
              {{ row.evidenceSnippet || "-" }}
            </template>
          </el-table-column>
          <el-table-column label="生成理由" min-width="220" show-overflow-tooltip>
            <template #default="{ row }">
              {{ row.reason || "-" }}
            </template>
          </el-table-column>
          <el-table-column label="操作" width="90" fixed="right">
            <template #default="{ row }">
              <el-button type="danger" link @click="removeDraftRow(row.id)">
                删除
              </el-button>
            </template>
          </el-table-column>
        </el-table>
      </el-card>
    </div>

    <template #footer>
      <div class="footer-actions">
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button type="primary" @click="handleImport">
          导入到自定义扩展
        </el-button>
      </div>
    </template>
  </el-dialog>
</template>

<style scoped>
.draft-dialog {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.card-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.card-title {
  font-size: 15px;
  font-weight: 600;
  color: var(--el-text-color-primary);
}

.card-subtitle {
  margin-top: 4px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--el-text-color-secondary);
}

.source-card,
.draft-card {
  border-radius: 16px;
}

.source-mode-group {
  margin-bottom: 16px;
}

.source-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.existing-files-toolbar,
.upload-mode-row,
.draft-actions,
.footer-actions {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
}

.helper-text {
  font-size: 12px;
  color: var(--el-text-color-secondary);
}

@media (max-width: 960px) {
  .card-header,
  .existing-files-toolbar,
  .upload-mode-row,
  .draft-actions,
  .footer-actions {
    flex-direction: column;
    align-items: flex-start;
  }
}
</style>
