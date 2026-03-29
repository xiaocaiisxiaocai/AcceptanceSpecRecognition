<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  getMatchingKnowledge,
  resetMatchingKnowledge,
  updateMatchingKnowledge,
  type ConflictPair,
  type MatchingKnowledgeLayer,
  type MatchingKnowledgeView
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

interface EditableConflictRow {
  id: number;
  left: string;
  right: string;
}

const loading = ref(false);
const activeTab = ref("entityAliases");

const builtInEntityAliasRows = ref<EditableStringRow[]>([]);
const builtInUnitAliasRows = ref<EditableStringRow[]>([]);
const builtInFieldAliasRows = ref<EditableStringRow[]>([]);
const builtInConflictPairRows = ref<EditableConflictRow[]>([]);

const customEntityAliasRows = ref<EditableStringRow[]>([]);
const customUnitAliasRows = ref<EditableStringRow[]>([]);
const customFieldAliasRows = ref<EditableStringRow[]>([]);
const customConflictPairRows = ref<EditableConflictRow[]>([]);

const canUpdate = computed(() => hasPerms("btn:matching-knowledge:update"));
const canReset = computed(() => hasPerms("btn:matching-knowledge:reset"));

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

const createConflictRow = (left = "", right = ""): EditableConflictRow => ({
  id: allocateRowId(),
  left,
  right
});

const buildStringRows = (source?: Record<string, string>) =>
  Object.entries(source ?? {}).map(([key, value]) =>
    createStringRow(key, value)
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

const toConflictPairs = (rows: EditableConflictRow[]) =>
  rows
    .map(row => ({
      left: row.left.trim(),
      right: row.right.trim()
    }))
    .filter(row => row.left && row.right);

const applyLayer = (
  layer: MatchingKnowledgeLayer,
  targets: {
    entityAliases: typeof builtInEntityAliasRows;
    unitAliases: typeof builtInUnitAliasRows;
    fieldAliases: typeof builtInFieldAliasRows;
    conflictPairs: typeof builtInConflictPairRows;
  }
) => {
  targets.entityAliases.value = buildStringRows(layer.entityAliases);
  targets.unitAliases.value = buildStringRows(layer.unitAliases);
  targets.fieldAliases.value = buildStringRows(layer.fieldAliases);
  targets.conflictPairs.value = buildConflictRows(layer.conflictPairs);
};

const applyConfig = (view: MatchingKnowledgeView) => {
  applyLayer(view.builtIn, {
    entityAliases: builtInEntityAliasRows,
    unitAliases: builtInUnitAliasRows,
    fieldAliases: builtInFieldAliasRows,
    conflictPairs: builtInConflictPairRows
  });

  applyLayer(view.custom, {
    entityAliases: customEntityAliasRows,
    unitAliases: customUnitAliasRows,
    fieldAliases: customFieldAliasRows,
    conflictPairs: customConflictPairRows
  });
};

const buildCustomPayload = (): MatchingKnowledgeLayer => ({
  entityAliases: toStringDictionary(customEntityAliasRows.value),
  unitAliases: toStringDictionary(customUnitAliasRows.value),
  unitFactors: {},
  fieldAliases: toStringDictionary(customFieldAliasRows.value),
  conflictPairs: toConflictPairs(customConflictPairRows.value)
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

const addStringRow = (target: typeof customEntityAliasRows.value) => {
  target.push(createStringRow());
};

const addConflictRow = () => {
  customConflictPairRows.value.push(createConflictRow());
};

const removeStringRow = (
  target: typeof customEntityAliasRows.value,
  id: number
) => {
  const index = target.findIndex(row => row.id === id);
  if (index >= 0) {
    target.splice(index, 1);
  }
};

const removeConflictRow = (id: number) => {
  const index = customConflictPairRows.value.findIndex(row => row.id === id);
  if (index >= 0) {
    customConflictPairRows.value.splice(index, 1);
  }
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
    const res = await updateMatchingKnowledge(buildCustomPayload());

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

const reset = async () => {
  if (
    !ensurePermission(
      "btn:matching-knowledge:reset",
      "权限不足，无法清空自定义扩展"
    )
  ) {
    return;
  }

  try {
    await ElMessageBox.confirm(
      "确定清空所有自定义扩展并恢复为仅系统内置规则吗？",
      "提示",
      {
        confirmButtonText: "确定",
        cancelButtonText: "取消",
        type: "warning"
      }
    );
  } catch {
    return;
  }

  loading.value = true;
  try {
    const res = await resetMatchingKnowledge();
    if (res.code === 0) {
      applyConfig(res.data);
      ElMessage.success("已清空自定义扩展");
    } else {
      ElMessage.error(res.message || "清空自定义扩展失败");
    }
  } catch {
    ElMessage.error("清空自定义扩展失败");
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
          系统内置常见电气、机械、芯片半导体术语，页面仅维护自定义扩展规则
        </div>
      </div>
      <div class="page-actions">
        <el-button v-if="canReset" :loading="loading" @click="reset">
          清空自定义
        </el-button>
        <el-button
          v-if="canUpdate"
          type="primary"
          :loading="loading"
          @click="save"
        >
          保存自定义扩展
        </el-button>
      </div>
    </div>

    <el-alert
      type="info"
      show-icon
      :closable="false"
      title="常见电气、机械、芯片半导体术语由系统内置；常见单位换算由系统内部自动处理，不在页面展示；此页面只维护增量扩展，空行会在保存时自动忽略。"
    />

    <el-tabs v-model="activeTab" v-loading="loading" class="knowledge-tabs">
      <el-tab-pane label="实体别名" name="entityAliases">
        <div class="knowledge-grid">
          <div class="section-label">系统内置（只读）</div>
          <el-card class="knowledge-card">
            <template #header>
              <div class="card-header">
                <div>
                  <div class="card-title">实体别名</div>
                  <div class="card-subtitle">
                    系统维护品牌、组织与厂商常用写法
                  </div>
                </div>
              </div>
            </template>
            <el-table
              :data="builtInEntityAliasRows"
              row-key="id"
              empty-text="暂无系统内置实体别名"
            >
              <el-table-column label="别名" min-width="220">
                <template #default="{ row }">
                  <span class="readonly-cell">{{ row.key }}</span>
                </template>
              </el-table-column>
              <el-table-column label="标准实体" min-width="220">
                <template #default="{ row }">
                  <span class="readonly-cell">{{ row.value }}</span>
                </template>
              </el-table-column>
            </el-table>
          </el-card>

          <div class="section-label">自定义扩展</div>
          <el-card class="knowledge-card">
            <template #header>
              <div class="card-header">
                <div>
                  <div class="card-title">实体别名</div>
                  <div class="card-subtitle">
                    仅在系统内置未覆盖客户自定义写法时补充
                  </div>
                </div>
                <el-button
                  v-if="canUpdate"
                  type="primary"
                  link
                  @click="addStringRow(customEntityAliasRows)"
                >
                  新增
                </el-button>
              </div>
            </template>
            <el-table
              :data="customEntityAliasRows"
              row-key="id"
              empty-text="暂无自定义实体别名"
            >
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
              <el-table-column
                v-if="canUpdate"
                label="操作"
                width="100"
                fixed="right"
              >
                <template #default="{ row }">
                  <el-button
                    type="danger"
                    link
                    @click="removeStringRow(customEntityAliasRows, row.id)"
                  >
                    删除
                  </el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </div>
      </el-tab-pane>

      <el-tab-pane label="单位规则" name="unitRules">
        <div class="knowledge-grid">
          <div class="section-label">系统内置（只读）</div>
          <el-card class="knowledge-card">
            <template #header>
              <div class="card-header">
                <div>
                  <div class="card-title">单位别名</div>
                  <div class="card-subtitle">
                    系统内置常见长度、电压、电流、频率、功率等单位写法
                  </div>
                </div>
              </div>
            </template>
            <el-table
              :data="builtInUnitAliasRows"
              row-key="id"
              empty-text="暂无系统内置单位别名"
            >
              <el-table-column label="别名" min-width="220">
                <template #default="{ row }">
                  <span class="readonly-cell">{{ row.key }}</span>
                </template>
              </el-table-column>
              <el-table-column label="标准单位" min-width="220">
                <template #default="{ row }">
                  <span class="readonly-cell">{{ row.value }}</span>
                </template>
              </el-table-column>
            </el-table>
          </el-card>

          <div class="section-label">自定义扩展</div>
          <el-card class="knowledge-card">
            <template #header>
              <div class="card-header">
                <div>
                  <div class="card-title">单位别名</div>
                  <div class="card-subtitle">
                    仅在客户使用系统未内置的行业缩写时补充
                  </div>
                </div>
                <el-button
                  v-if="canUpdate"
                  type="primary"
                  link
                  @click="addStringRow(customUnitAliasRows)"
                >
                  新增
                </el-button>
              </div>
            </template>
            <el-table
              :data="customUnitAliasRows"
              row-key="id"
              empty-text="暂无自定义单位别名"
            >
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
              <el-table-column
                v-if="canUpdate"
                label="操作"
                width="100"
                fixed="right"
              >
                <template #default="{ row }">
                  <el-button
                    type="danger"
                    link
                    @click="removeStringRow(customUnitAliasRows, row.id)"
                  >
                    删除
                  </el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </div>
      </el-tab-pane>

      <el-tab-pane label="字段别名" name="fieldAliases">
        <div class="knowledge-grid">
          <div class="section-label">系统内置（只读）</div>
          <el-card class="knowledge-card">
            <template #header>
              <div class="card-header">
                <div>
                  <div class="card-title">字段别名</div>
                  <div class="card-subtitle">
                    系统维护常见机械、电气、半导体字段归一规则
                  </div>
                </div>
              </div>
            </template>
            <el-table
              :data="builtInFieldAliasRows"
              row-key="id"
              empty-text="暂无系统内置字段别名"
            >
              <el-table-column label="别名" min-width="220">
                <template #default="{ row }">
                  <span class="readonly-cell">{{ row.key }}</span>
                </template>
              </el-table-column>
              <el-table-column label="标准字段" min-width="220">
                <template #default="{ row }">
                  <span class="readonly-cell">{{ row.value }}</span>
                </template>
              </el-table-column>
            </el-table>
          </el-card>

          <div class="section-label">自定义扩展</div>
          <el-card class="knowledge-card">
            <template #header>
              <div class="card-header">
                <div>
                  <div class="card-title">字段别名</div>
                  <div class="card-subtitle">
                    客户内部术语、缩写或特定列名在这里补充
                  </div>
                </div>
                <el-button
                  v-if="canUpdate"
                  type="primary"
                  link
                  @click="addStringRow(customFieldAliasRows)"
                >
                  新增
                </el-button>
              </div>
            </template>
            <el-table
              :data="customFieldAliasRows"
              row-key="id"
              empty-text="暂无自定义字段别名"
            >
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
              <el-table-column
                v-if="canUpdate"
                label="操作"
                width="100"
                fixed="right"
              >
                <template #default="{ row }">
                  <el-button
                    type="danger"
                    link
                    @click="removeStringRow(customFieldAliasRows, row.id)"
                  >
                    删除
                  </el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </div>
      </el-tab-pane>

      <el-tab-pane label="冲突词对" name="conflictPairs">
        <div class="knowledge-grid">
          <div class="section-label">系统内置（只读）</div>
          <el-card class="knowledge-card">
            <template #header>
              <div class="card-header">
                <div>
                  <div class="card-title">冲突词对</div>
                  <div class="card-subtitle">
                    系统内置不可同时成立的对立语义
                  </div>
                </div>
              </div>
            </template>
            <el-table
              :data="builtInConflictPairRows"
              row-key="id"
              empty-text="暂无系统内置冲突词对"
            >
              <el-table-column label="左侧词" min-width="220">
                <template #default="{ row }">
                  <span class="readonly-cell">{{ row.left }}</span>
                </template>
              </el-table-column>
              <el-table-column label="右侧词" min-width="220">
                <template #default="{ row }">
                  <span class="readonly-cell">{{ row.right }}</span>
                </template>
              </el-table-column>
            </el-table>
          </el-card>

          <div class="section-label">自定义扩展</div>
          <el-card class="knowledge-card">
            <template #header>
              <div class="card-header">
                <div>
                  <div class="card-title">冲突词对</div>
                  <div class="card-subtitle">
                    仅补充客户业务里明确互斥的对立语义
                  </div>
                </div>
                <el-button
                  v-if="canUpdate"
                  type="primary"
                  link
                  @click="addConflictRow"
                >
                  新增
                </el-button>
              </div>
            </template>
            <el-table
              :data="customConflictPairRows"
              row-key="id"
              empty-text="暂无自定义冲突词对"
            >
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
              <el-table-column
                v-if="canUpdate"
                label="操作"
                width="100"
                fixed="right"
              >
                <template #default="{ row }">
                  <el-button
                    type="danger"
                    link
                    @click="removeConflictRow(row.id)"
                  >
                    删除
                  </el-button>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </div>
      </el-tab-pane>
    </el-tabs>
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

.section-label {
  font-size: 13px;
  font-weight: 600;
  color: var(--el-color-primary);
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

.readonly-cell {
  color: var(--el-text-color-regular);
  word-break: break-word;
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
}
</style>
