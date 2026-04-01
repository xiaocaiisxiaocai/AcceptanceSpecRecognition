<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import MatchingKnowledgeDraftDialog from "./components/MatchingKnowledgeDraftDialog.vue";
import {
  clearMatchingKnowledge,
  getMatchingKnowledge,
  restoreDefaultMatchingKnowledge,
  type MatchingKnowledgeDraftCategory,
  type MatchingKnowledgeDraftItem,
  updateMatchingKnowledge,
  type ConflictPair,
  type MatchingKnowledgeLayer
} from "@/api/matching-knowledge";
import { hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";

defineOptions({
  name: "MatchingKnowledgeConfig"
});

interface EditableStringRow {
  id: number;
  key: string;
  value: string;
}

interface EditableNumberRow {
  id: number;
  key: string;
  value: number | null;
}

interface EditableConflictRow {
  id: number;
  left: string;
  right: string;
}

const loading = ref(false);
const activeTab = ref("entityAliases");
const entityAliasRows = ref<EditableStringRow[]>([]);
const unitAliasRows = ref<EditableStringRow[]>([]);
const unitFactorRows = ref<EditableNumberRow[]>([]);
const fieldAliasRows = ref<EditableStringRow[]>([]);
const conflictPairRows = ref<EditableConflictRow[]>([]);

const canUpdate = computed(() => hasPerms("btn:matching-knowledge:update"));
const canReset = computed(() => hasPerms("btn:matching-knowledge:reset"));
const canGenerateDraft = computed(() =>
  hasPerms("btn:matching-knowledge:generate-draft")
);
const draftDialogVisible = ref(false);
const draftDialogCategory = ref<MatchingKnowledgeDraftCategory>("entityAliases");

let nextRowId = 1;

const allocateRowId = () => {
  const id = nextRowId;
  nextRowId += 1;
  return id;
};

const createStringRow = (key = "", value = ""): EditableStringRow => ({
  id: allocateRowId(),
  key,
  value
});

const createNumberRow = (
  key = "",
  value: number | null = null
): EditableNumberRow => ({
  id: allocateRowId(),
  key,
  value
});

const createConflictRow = (left = "", right = ""): EditableConflictRow => ({
  id: allocateRowId(),
  left,
  right
});

const buildStringRows = (source?: Record<string, string>) =>
  Object.entries(source ?? {}).map(([key, value]) =>
    createStringRow(key, value)
  );

const buildNumberRows = (source?: Record<string, number>) =>
  Object.entries(source ?? {}).map(([key, value]) =>
    createNumberRow(key, value)
  );

const buildConflictRows = (source?: ConflictPair[]) =>
  (source ?? []).map(item => createConflictRow(item.left, item.right));

const toStringDictionary = (rows: EditableStringRow[]) => {
  const result: Record<string, string> = {};
  rows.forEach(row => {
    const key = row.key.trim();
    const value = row.value.trim();
    if (!key || !value) {
      return;
    }

    result[key] = value;
  });
  return result;
};

const toNumberDictionary = (rows: EditableNumberRow[]) => {
  const result: Record<string, number> = {};
  rows.forEach(row => {
    const key = row.key.trim();
    if (!key || row.value === null || Number.isNaN(row.value)) {
      return;
    }

    result[key] = Number(row.value);
  });
  return result;
};

const toConflictPairs = (rows: EditableConflictRow[]) =>
  rows
    .map(row => ({
      left: row.left.trim(),
      right: row.right.trim()
    }))
    .filter(row => row.left && row.right);

const applyConfig = (config: MatchingKnowledgeLayer) => {
  entityAliasRows.value = buildStringRows(config.entityAliases);
  unitAliasRows.value = buildStringRows(config.unitAliases);
  unitFactorRows.value = buildNumberRows(config.unitFactors);
  fieldAliasRows.value = buildStringRows(config.fieldAliases);
  conflictPairRows.value = buildConflictRows(config.conflictPairs);
};

const buildPayload = (): MatchingKnowledgeLayer => ({
  entityAliases: toStringDictionary(entityAliasRows.value),
  unitAliases: toStringDictionary(unitAliasRows.value),
  unitFactors: toNumberDictionary(unitFactorRows.value),
  fieldAliases: toStringDictionary(fieldAliasRows.value),
  conflictPairs: toConflictPairs(conflictPairRows.value)
});

const load = async () => {
  loading.value = true;
  try {
    const res = await getMatchingKnowledge();
    if (res.code === 0) {
      applyConfig(res.data);
    } else {
      ElMessage.error(res.message || "加载匹配知识失败");
    }
  } catch {
    ElMessage.error("加载匹配知识失败");
  } finally {
    loading.value = false;
  }
};

const addStringRow = (target: EditableStringRow[]) => {
  target.push(createStringRow());
};

const addNumberRow = () => {
  unitFactorRows.value.push(createNumberRow());
};

const addConflictRow = () => {
  conflictPairRows.value.push(createConflictRow());
};

const removeStringRow = (target: EditableStringRow[], id: number) => {
  const index = target.findIndex(row => row.id === id);
  if (index >= 0) {
    target.splice(index, 1);
  }
};

const removeNumberRow = (id: number) => {
  const index = unitFactorRows.value.findIndex(row => row.id === id);
  if (index >= 0) {
    unitFactorRows.value.splice(index, 1);
  }
};

const removeConflictRow = (id: number) => {
  const index = conflictPairRows.value.findIndex(row => row.id === id);
  if (index >= 0) {
    conflictPairRows.value.splice(index, 1);
  }
};

const normalizeValue = (value: string) => value.trim();
const normalizeKey = (value: string) => normalizeValue(value).toLowerCase();
const buildConflictPairKey = (left: string, right: string) => {
  const normalizedLeft = normalizeValue(left);
  const normalizedRight = normalizeValue(right);
  if (!normalizedLeft || !normalizedRight) {
    return "";
  }

  return [normalizedLeft, normalizedRight]
    .sort((a, b) => a.localeCompare(b, "zh-CN", { sensitivity: "accent" }))
    .map(item => item.toLowerCase())
    .join("__");
};

const openDraftDialog = (category: MatchingKnowledgeDraftCategory) => {
  if (
    !ensurePermission(
      "btn:matching-knowledge:generate-draft",
      "权限不足，无法生成匹配知识候选"
    )
  ) {
    return;
  }

  draftDialogCategory.value = category;
  draftDialogVisible.value = true;
};

const mergeMappingDraftItems = (
  targetRows: EditableStringRow[],
  items: MatchingKnowledgeDraftItem[]
) => {
  const existingValues = new Map<string, string>();
  targetRows.forEach(row => {
    const key = normalizeKey(row.key);
    const value = normalizeValue(row.value);
    if (!key || !value || existingValues.has(key)) {
      return;
    }

    existingValues.set(key, value);
  });

  let imported = 0;
  let duplicate = 0;
  let conflict = 0;
  let invalid = 0;

  items.forEach(item => {
    const key = normalizeValue(item.key);
    const value = normalizeValue(item.value);
    if (!key || !value) {
      invalid += 1;
      return;
    }

    const normalizedKey = key.toLowerCase();
    const currentValue = existingValues.get(normalizedKey);
    if (currentValue) {
      if (normalizeKey(currentValue) === normalizeKey(value)) {
        duplicate += 1;
      } else {
        conflict += 1;
      }

      return;
    }

    targetRows.push(createStringRow(key, value));
    existingValues.set(normalizedKey, value);
    imported += 1;
  });

  return { imported, duplicate, conflict, invalid };
};

const mergeConflictDraftItems = (items: MatchingKnowledgeDraftItem[]) => {
  const existingPairs = new Set<string>();
  conflictPairRows.value.forEach(row => {
    const pairKey = buildConflictPairKey(row.left, row.right);
    if (pairKey) {
      existingPairs.add(pairKey);
    }
  });

  let imported = 0;
  let duplicate = 0;
  let invalid = 0;

  items.forEach(item => {
    const left = normalizeValue(item.key);
    const right = normalizeValue(item.value);
    const pairKey = buildConflictPairKey(left, right);
    if (!pairKey) {
      invalid += 1;
      return;
    }

    if (existingPairs.has(pairKey)) {
      duplicate += 1;
      return;
    }

    conflictPairRows.value.push(createConflictRow(left, right));
    existingPairs.add(pairKey);
    imported += 1;
  });

  return { imported, duplicate, conflict: 0, invalid };
};

const handleDraftImport = (payload: {
  category: MatchingKnowledgeDraftCategory;
  items: MatchingKnowledgeDraftItem[];
}) => {
  if (
    !ensurePermission(
      "btn:matching-knowledge:update",
      "权限不足，无法导入到当前配置"
    )
  ) {
    return;
  }

  const result =
    payload.category === "entityAliases"
      ? mergeMappingDraftItems(entityAliasRows.value, payload.items)
      : payload.category === "unitAliases"
        ? mergeMappingDraftItems(unitAliasRows.value, payload.items)
        : payload.category === "fieldAliases"
          ? mergeMappingDraftItems(fieldAliasRows.value, payload.items)
          : mergeConflictDraftItems(payload.items);

  const messages: string[] = [];
  if (result.imported > 0) {
    messages.push(`已导入 ${result.imported} 条`);
  }
  if (result.duplicate > 0) {
    messages.push(`${result.duplicate} 条重复已忽略`);
  }
  if (result.conflict > 0) {
    messages.push(`${result.conflict} 条冲突未导入`);
  }
  if (result.invalid > 0) {
    messages.push(`${result.invalid} 条空值未导入`);
  }

  ElMessage[result.imported > 0 ? "success" : "warning"](
    messages.join("，") || "没有可导入的候选"
  );
};

const save = async () => {
  if (
    !ensurePermission(
      "btn:matching-knowledge:update",
      "权限不足，无法保存匹配知识配置"
    )
  ) {
    return;
  }

  loading.value = true;
  try {
    const res = await updateMatchingKnowledge(buildPayload());
    if (res.code === 0) {
      applyConfig(res.data);
      ElMessage.success("保存成功");
    } else {
      ElMessage.error(res.message || "保存匹配知识失败");
    }
  } catch {
    ElMessage.error("保存匹配知识失败");
  } finally {
    loading.value = false;
  }
};

const clearCurrent = async () => {
  if (
    !ensurePermission(
      "btn:matching-knowledge:reset",
      "权限不足，无法清空当前配置"
    )
  ) {
    return;
  }

  try {
    await ElMessageBox.confirm("确定清空当前生效配置吗？该操作会立即影响运行时匹配。", "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
  } catch {
    return;
  }

  loading.value = true;
  try {
    const res = await clearMatchingKnowledge();
    if (res.code === 0) {
      applyConfig(res.data);
      ElMessage.success("已清空当前配置");
    } else {
      ElMessage.error(res.message || "清空当前配置失败");
    }
  } catch {
    ElMessage.error("清空当前配置失败");
  } finally {
    loading.value = false;
  }
};

const restoreDefaults = async () => {
  if (
    !ensurePermission(
      "btn:matching-knowledge:reset",
      "权限不足，无法恢复默认配置"
    )
  ) {
    return;
  }

  try {
    await ElMessageBox.confirm("确定恢复默认配置吗？当前修改会被默认种子覆盖。", "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
  } catch {
    return;
  }

  loading.value = true;
  try {
    const res = await restoreDefaultMatchingKnowledge();
    if (res.code === 0) {
      applyConfig(res.data);
      ElMessage.success("已恢复默认配置");
    } else {
      ElMessage.error(res.message || "恢复默认配置失败");
    }
  } catch {
    ElMessage.error("恢复默认配置失败");
  } finally {
    loading.value = false;
  }
};

onMounted(load);
</script>

<template>
  <div class="page config-page">
    <div class="page-header">
      <div>
        <div class="page-title">匹配知识配置</div>
        <div class="page-subtitle">
          当前生效配置即运行时配置，保存、删除、清空后会直接影响后续匹配。
        </div>
      </div>
      <div class="page-actions">
        <el-button v-if="canReset" :loading="loading" @click="clearCurrent">
          清空当前配置
        </el-button>
        <el-button v-if="canReset" :loading="loading" @click="restoreDefaults">
          恢复默认配置
        </el-button>
        <el-button
          v-if="canUpdate"
          type="primary"
          :loading="loading"
          @click="save"
        >
          保存当前配置
        </el-button>
      </div>
    </div>

    <el-alert
      type="info"
      show-icon
      :closable="false"
      title="页面展示并直接编辑当前生效配置；AI 候选导入会写入当前配置；清空与恢复默认都会立即影响运行时。"
    />

    <el-tabs v-model="activeTab" v-loading="loading" class="knowledge-tabs">
      <el-tab-pane label="实体别名" name="entityAliases">
        <el-card class="knowledge-card">
          <template #header>
            <div class="card-header">
              <div>
                <div class="card-title">实体别名</div>
                <div class="card-subtitle">维护品牌、组织与厂商的标准化映射。</div>
              </div>
              <div class="card-toolbar">
                <el-button
                  v-if="canGenerateDraft"
                  type="primary"
                  link
                  @click="openDraftDialog('entityAliases')"
                >
                  AI 生成候选
                </el-button>
                <el-button
                  v-if="canUpdate"
                  type="primary"
                  link
                  @click="addStringRow(entityAliasRows)"
                >
                  新增
                </el-button>
              </div>
            </div>
          </template>
          <el-table :data="entityAliasRows" row-key="id" empty-text="暂无实体别名">
            <el-table-column label="别名" min-width="220">
              <template #default="{ row }">
                <el-input v-model="row.key" placeholder="输入别名" />
              </template>
            </el-table-column>
            <el-table-column label="标准实体" min-width="220">
              <template #default="{ row }">
                <el-input v-model="row.value" placeholder="输入标准实体名称" />
              </template>
            </el-table-column>
            <el-table-column v-if="canUpdate" label="操作" width="100" fixed="right">
              <template #default="{ row }">
                <el-button type="danger" link @click="removeStringRow(entityAliasRows, row.id)">
                  删除
                </el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-tab-pane>

      <el-tab-pane label="单位规则" name="unitRules">
        <div class="knowledge-grid">
          <el-card class="knowledge-card">
            <template #header>
              <div class="card-header">
                <div>
                  <div class="card-title">单位别名</div>
                  <div class="card-subtitle">维护行业缩写、中文写法与标准单位的映射。</div>
                </div>
                <div class="card-toolbar">
                  <el-button
                    v-if="canGenerateDraft"
                    type="primary"
                    link
                    @click="openDraftDialog('unitAliases')"
                  >
                    AI 生成候选
                  </el-button>
                  <el-button
                    v-if="canUpdate"
                    type="primary"
                    link
                    @click="addStringRow(unitAliasRows)"
                  >
                    新增
                  </el-button>
                </div>
              </div>
            </template>
            <el-table :data="unitAliasRows" row-key="id" empty-text="暂无单位别名">
              <el-table-column label="别名" min-width="220">
                <template #default="{ row }">
                  <el-input v-model="row.key" placeholder="输入单位别名" />
                </template>
              </el-table-column>
              <el-table-column label="标准单位" min-width="220">
                <template #default="{ row }">
                  <el-input v-model="row.value" placeholder="输入标准单位" />
                </template>
              </el-table-column>
              <el-table-column v-if="canUpdate" label="操作" width="100" fixed="right">
                <template #default="{ row }">
                  <el-button type="danger" link @click="removeStringRow(unitAliasRows, row.id)">
                    删除
                  </el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-card>

          <el-card class="knowledge-card">
            <template #header>
              <div class="card-header">
                <div>
                  <div class="card-title">单位换算</div>
                  <div class="card-subtitle">维护标准单位到归一倍率的映射。</div>
                </div>
                <div class="card-toolbar">
                  <el-button v-if="canUpdate" type="primary" link @click="addNumberRow">
                    新增
                  </el-button>
                </div>
              </div>
            </template>
            <el-table :data="unitFactorRows" row-key="id" empty-text="暂无单位换算">
              <el-table-column label="标准单位" min-width="220">
                <template #default="{ row }">
                  <el-input v-model="row.key" placeholder="输入标准单位" />
                </template>
              </el-table-column>
              <el-table-column label="归一系数" min-width="220">
                <template #default="{ row }">
                  <el-input-number v-model="row.value" :controls="false" style="width: 100%" />
                </template>
              </el-table-column>
              <el-table-column v-if="canUpdate" label="操作" width="100" fixed="right">
                <template #default="{ row }">
                  <el-button type="danger" link @click="removeNumberRow(row.id)">
                    删除
                  </el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </div>
      </el-tab-pane>

      <el-tab-pane label="字段别名" name="fieldAliases">
        <el-card class="knowledge-card">
          <template #header>
            <div class="card-header">
              <div>
                <div class="card-title">字段别名</div>
                <div class="card-subtitle">维护客户内部术语、缩写与标准字段的映射。</div>
              </div>
              <div class="card-toolbar">
                <el-button
                  v-if="canGenerateDraft"
                  type="primary"
                  link
                  @click="openDraftDialog('fieldAliases')"
                >
                  AI 生成候选
                </el-button>
                <el-button
                  v-if="canUpdate"
                  type="primary"
                  link
                  @click="addStringRow(fieldAliasRows)"
                >
                  新增
                </el-button>
              </div>
            </div>
          </template>
          <el-table :data="fieldAliasRows" row-key="id" empty-text="暂无字段别名">
            <el-table-column label="别名" min-width="220">
              <template #default="{ row }">
                <el-input v-model="row.key" placeholder="输入字段别名" />
              </template>
            </el-table-column>
            <el-table-column label="标准字段" min-width="220">
              <template #default="{ row }">
                <el-input v-model="row.value" placeholder="输入标准字段" />
              </template>
            </el-table-column>
            <el-table-column v-if="canUpdate" label="操作" width="100" fixed="right">
              <template #default="{ row }">
                <el-button type="danger" link @click="removeStringRow(fieldAliasRows, row.id)">
                  删除
                </el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-tab-pane>

      <el-tab-pane label="冲突词对" name="conflictPairs">
        <el-card class="knowledge-card">
          <template #header>
            <div class="card-header">
              <div>
                <div class="card-title">冲突词对</div>
                <div class="card-subtitle">维护业务中明确互斥的对立语义。</div>
              </div>
              <div class="card-toolbar">
                <el-button
                  v-if="canGenerateDraft"
                  type="primary"
                  link
                  @click="openDraftDialog('conflictPairs')"
                >
                  AI 生成候选
                </el-button>
                <el-button
                  v-if="canUpdate"
                  type="primary"
                  link
                  @click="addConflictRow"
                >
                  新增
                </el-button>
              </div>
            </div>
          </template>
          <el-table :data="conflictPairRows" row-key="id" empty-text="暂无冲突词对">
            <el-table-column label="左侧词" min-width="220">
              <template #default="{ row }">
                <el-input v-model="row.left" placeholder="输入左侧词" />
              </template>
            </el-table-column>
            <el-table-column label="右侧词" min-width="220">
              <template #default="{ row }">
                <el-input v-model="row.right" placeholder="输入右侧词" />
              </template>
            </el-table-column>
            <el-table-column v-if="canUpdate" label="操作" width="100" fixed="right">
              <template #default="{ row }">
                <el-button type="danger" link @click="removeConflictRow(row.id)">
                  删除
                </el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-tab-pane>
    </el-tabs>

    <MatchingKnowledgeDraftDialog
      v-model:visible="draftDialogVisible"
      :category="draftDialogCategory"
      @import="handleDraftImport"
    />
  </div>
</template>

<style scoped>
.page {
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.page-actions {
  display: flex;
  gap: 12px;
  align-items: center;
  flex-wrap: wrap;
}

.knowledge-tabs {
  margin-top: 4px;
}

.knowledge-grid {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.knowledge-card {
  border-radius: 16px;
}

.card-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
}

.card-toolbar {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
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

@media (max-width: 768px) {
  .page-header {
    flex-direction: column;
    align-items: flex-start;
  }

  .page-actions {
    width: 100%;
    justify-content: flex-start;
  }

  .card-header {
    flex-direction: column;
    align-items: flex-start;
  }

  .card-toolbar {
    width: 100%;
    justify-content: flex-start;
  }
}
</style>
