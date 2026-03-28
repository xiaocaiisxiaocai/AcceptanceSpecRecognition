<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  getMatchingKnowledge,
  resetMatchingKnowledge,
  updateMatchingKnowledge,
  type ConflictPair,
  type MatchingKnowledgeConfig
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

interface EditableFactorRow {
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
const entityAliasRows = ref<EditableStringRow[]>([]);
const unitAliasRows = ref<EditableStringRow[]>([]);
const unitFactorRows = ref<EditableFactorRow[]>([]);
const fieldAliasRows = ref<EditableStringRow[]>([]);
const conflictPairRows = ref<EditableConflictRow[]>([]);

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

const createFactorRow = (
  key = "",
  value: number | null = null
): EditableFactorRow => ({
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

const buildFactorRows = (source?: Record<string, number>) =>
  Object.entries(source ?? {}).map(([key, value]) =>
    createFactorRow(key, value)
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

const toFactorDictionary = (rows: EditableFactorRow[]) => {
  const result: Record<string, number> = {};
  rows.forEach(row => {
    const key = row.key.trim();
    if (!key || row.value === null || Number.isNaN(row.value)) {
      return;
    }
    result[key] = row.value;
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

const applyConfig = (config: MatchingKnowledgeConfig) => {
  entityAliasRows.value = buildStringRows(config.entityAliases);
  unitAliasRows.value = buildStringRows(config.unitAliases);
  unitFactorRows.value = buildFactorRows(config.unitFactors);
  fieldAliasRows.value = buildStringRows(config.fieldAliases);
  conflictPairRows.value = buildConflictRows(config.conflictPairs);
};

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

const addStringRow = (target: typeof entityAliasRows.value) => {
  target.push(createStringRow());
};

const addFactorRow = () => {
  unitFactorRows.value.push(createFactorRow());
};

const addConflictRow = () => {
  conflictPairRows.value.push(createConflictRow());
};

const removeStringRow = (target: typeof entityAliasRows.value, id: number) => {
  const index = target.findIndex(row => row.id === id);
  if (index >= 0) {
    target.splice(index, 1);
  }
};

const removeFactorRow = (id: number) => {
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
    const res = await updateMatchingKnowledge({
      entityAliases: toStringDictionary(entityAliasRows.value),
      unitAliases: toStringDictionary(unitAliasRows.value),
      unitFactors: toFactorDictionary(unitFactorRows.value),
      fieldAliases: toStringDictionary(fieldAliasRows.value),
      conflictPairs: toConflictPairs(conflictPairRows.value)
    });

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
      "权限不足，无法重置匹配知识配置"
    )
  ) {
    return;
  }

  try {
    await ElMessageBox.confirm("确定重置为系统默认匹配知识吗？", "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
  } catch {
    return;
  }

  loading.value = true;
  try {
    const res = await resetMatchingKnowledge();
    if (res.code === 0) {
      applyConfig(res.data);
      ElMessage.success("已恢复系统默认配置");
    } else {
      ElMessage.error(res.message || "重置匹配知识失败");
    }
  } catch {
    ElMessage.error("重置匹配知识失败");
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
          集中维护匹配主链路使用的别名、换算与冲突词规则
        </div>
      </div>
      <div class="page-actions">
        <el-button v-if="canReset" :loading="loading" @click="reset"
          >重置默认</el-button
        >
        <el-button
          v-if="canUpdate"
          type="primary"
          :loading="loading"
          @click="save"
        >
          保存
        </el-button>
      </div>
    </div>

    <el-alert
      type="info"
      show-icon
      :closable="false"
      title="页面只维护结构化匹配知识；空行会在保存时自动忽略，重复键以后输入值为准。"
    />

    <div v-loading="loading" class="knowledge-grid">
      <el-card class="knowledge-card">
        <template #header>
          <div class="card-header">
            <div>
              <div class="card-title">实体别名</div>
              <div class="card-subtitle">例如 Panasonic 品牌 -> 松下</div>
            </div>
            <el-button
              v-if="canUpdate"
              type="primary"
              link
              @click="addStringRow(entityAliasRows)"
            >
              新增
            </el-button>
          </div>
        </template>
        <el-table
          :data="entityAliasRows"
          row-key="id"
          empty-text="暂无实体别名"
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
                @click="removeStringRow(entityAliasRows, row.id)"
              >
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
              <div class="card-title">单位别名</div>
              <div class="card-subtitle">例如 公分 -> cm</div>
            </div>
            <el-button
              v-if="canUpdate"
              type="primary"
              link
              @click="addStringRow(unitAliasRows)"
            >
              新增
            </el-button>
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
                @click="removeStringRow(unitAliasRows, row.id)"
              >
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
              <div class="card-subtitle">
                保存相对标准单位的倍率，例如 cm -> 10
              </div>
            </div>
            <el-button
              v-if="canUpdate"
              type="primary"
              link
              @click="addFactorRow"
              >新增</el-button
            >
          </div>
        </template>
        <el-table :data="unitFactorRows" row-key="id" empty-text="暂无单位换算">
          <el-table-column label="单位" min-width="220">
            <template #default="{ row }">
              <el-input v-model="row.key" placeholder="输入单位" />
            </template>
          </el-table-column>
          <el-table-column label="换算系数" min-width="220">
            <template #default="{ row }">
              <el-input-number
                v-model="row.value"
                class="factor-input"
                :controls="false"
                :precision="6"
                placeholder="输入换算系数"
              />
            </template>
          </el-table-column>
          <el-table-column
            v-if="canUpdate"
            label="操作"
            width="100"
            fixed="right"
          >
            <template #default="{ row }">
              <el-button type="danger" link @click="removeFactorRow(row.id)"
                >删除</el-button
              >
            </template>
          </el-table-column>
        </el-table>
      </el-card>

      <el-card class="knowledge-card">
        <template #header>
          <div class="card-header">
            <div>
              <div class="card-title">字段别名</div>
              <div class="card-subtitle">例如 宽尺寸 -> 宽度</div>
            </div>
            <el-button
              v-if="canUpdate"
              type="primary"
              link
              @click="addStringRow(fieldAliasRows)"
            >
              新增
            </el-button>
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
                @click="removeStringRow(fieldAliasRows, row.id)"
              >
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
              <div class="card-title">冲突词对</div>
              <div class="card-subtitle">用于标记不可同时成立的对立语义</div>
            </div>
            <el-button
              v-if="canUpdate"
              type="primary"
              link
              @click="addConflictRow"
              >新增</el-button
            >
          </div>
        </template>
        <el-table
          :data="conflictPairRows"
          row-key="id"
          empty-text="暂无冲突词对"
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
              <el-button type="danger" link @click="removeConflictRow(row.id)"
                >删除</el-button
              >
            </template>
          </el-table-column>
        </el-table>
      </el-card>
    </div>
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

.factor-input {
  width: 100%;
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
