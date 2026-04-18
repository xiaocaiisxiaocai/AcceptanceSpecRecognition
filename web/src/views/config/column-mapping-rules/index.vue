<script setup lang="ts">
import { computed, onMounted, reactive, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import {
  ColumnMappingMatchMode,
  ColumnMappingTargetField,
  createColumnMappingRule,
  deleteColumnMappingRule,
  getColumnMappingRules,
  updateColumnMappingRule,
  type ColumnMappingRule
} from "@/api/column-mapping-rules";
import { hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";

defineOptions({
  name: "ColumnMappingRules"
});

const loading = ref(false);
const rules = ref<ColumnMappingRule[]>([]);

const activeTarget = ref<ColumnMappingTargetField>(ColumnMappingTargetField.Project);

const targetOptions = [
  { label: "项目", value: ColumnMappingTargetField.Project },
  { label: "规格内容", value: ColumnMappingTargetField.Specification },
  { label: "验收标准", value: ColumnMappingTargetField.Acceptance },
  { label: "备注", value: ColumnMappingTargetField.Remark }
];

const matchModeOptions = [
  { label: "相等", value: ColumnMappingMatchMode.Equals },
  { label: "包含", value: ColumnMappingMatchMode.Contains },
  { label: "正则", value: ColumnMappingMatchMode.Regex }
];

const tabKeywords = reactive({
  [ColumnMappingTargetField.Project]: "",
  [ColumnMappingTargetField.Specification]: "",
  [ColumnMappingTargetField.Acceptance]: "",
  [ColumnMappingTargetField.Remark]: ""
});

const canCreate = computed(() => hasPerms("btn:column-mapping-rule:create"));
const canUpdate = computed(() => hasPerms("btn:column-mapping-rule:update"));
const canDelete = computed(() => hasPerms("btn:column-mapping-rule:delete"));
const canSubmit = computed(() => (isEdit.value ? canUpdate.value : canCreate.value));
const hasOperationActions = computed(() => canUpdate.value || canDelete.value);

const getMatchModeLabel = (matchMode: ColumnMappingMatchMode) =>
  matchModeOptions.find(option => option.value === matchMode)?.label.toLowerCase() ?? "";

const filteredRulesByTarget = computed(() => {
  const result = {} as Record<ColumnMappingTargetField, ColumnMappingRule[]>;

  targetOptions.forEach(target => {
    const keyword = tabKeywords[target.value].trim().toLowerCase();

    result[target.value] = rules.value
      .filter(rule => rule.targetField === target.value)
      .filter(rule => {
        if (!keyword) {
          return true;
        }

        const matchModeLabel = getMatchModeLabel(rule.matchMode);
        return (
          rule.pattern.toLowerCase().includes(keyword) ||
          matchModeLabel.includes(keyword)
        );
      })
      .sort((a, b) => (b.priority ?? 0) - (a.priority ?? 0) || a.id - b.id);
  });

  return result;
});

const load = async () => {
  loading.value = true;
  try {
    const res = await getColumnMappingRules();
    if (res.code === 0) {
      rules.value = res.data || [];
    } else {
      ElMessage.error(res.message || "加载规则失败");
    }
  } catch {
    ElMessage.error("加载规则失败");
  } finally {
    loading.value = false;
  }
};

const dialogVisible = ref(false);
const dialogTitle = ref("新增规则");
const isEdit = ref(false);

const form = reactive({
  id: 0,
  targetField: ColumnMappingTargetField.Project,
  matchMode: ColumnMappingMatchMode.Equals,
  pattern: "",
  priority: 0,
  enabled: true
});

const openAdd = () => {
  if (!ensurePermission("btn:column-mapping-rule:create", "权限不足，无法新增列映射规则")) {
    return;
  }

  isEdit.value = false;
  dialogTitle.value = "新增规则";
  form.id = 0;
  form.targetField = activeTarget.value;
  form.matchMode = ColumnMappingMatchMode.Equals;
  form.pattern = "";
  form.priority = 0;
  form.enabled = true;
  dialogVisible.value = true;
};

const openEdit = (row: ColumnMappingRule) => {
  if (!ensurePermission("btn:column-mapping-rule:update", "权限不足，无法编辑列映射规则")) {
    return;
  }

  isEdit.value = true;
  dialogTitle.value = "编辑规则";
  form.id = row.id;
  form.targetField = row.targetField;
  form.matchMode = row.matchMode;
  form.pattern = row.pattern;
  form.priority = row.priority ?? 0;
  form.enabled = row.enabled;
  dialogVisible.value = true;
};

const submit = async () => {
  if (
    !ensurePermission(
      isEdit.value ? "btn:column-mapping-rule:update" : "btn:column-mapping-rule:create",
      isEdit.value ? "权限不足，无法保存列映射规则" : "权限不足，无法新增列映射规则"
    )
  ) {
    return;
  }

  const pattern = form.pattern.trim();
  if (!pattern) {
    ElMessage.warning("请输入匹配词");
    return;
  }

  try {
    const payload = {
      targetField: form.targetField,
      matchMode: form.matchMode,
      pattern,
      priority: Number(form.priority) || 0,
      enabled: form.enabled
    };

    const res = isEdit.value
      ? await updateColumnMappingRule(form.id, payload)
      : await createColumnMappingRule(payload);

    if (res.code === 0) {
      ElMessage.success(isEdit.value ? "更新成功" : "创建成功");
      dialogVisible.value = false;
      await load();
    } else {
      ElMessage.error(res.message || "保存失败");
    }
  } catch (error: any) {
    ElMessage.error(error?.message || "保存失败");
  }
};

const persistRow = async (row: ColumnMappingRule) => {
  if (!ensurePermission("btn:column-mapping-rule:update", "权限不足，无法更新列映射规则")) {
    await load();
    return;
  }

  try {
    const res = await updateColumnMappingRule(row.id, {
      targetField: row.targetField,
      matchMode: row.matchMode,
      pattern: row.pattern,
      priority: row.priority ?? 0,
      enabled: row.enabled
    });
    if (res.code !== 0) {
      ElMessage.error(res.message || "更新失败");
      await load();
    }
  } catch {
    ElMessage.error("更新失败");
    await load();
  }
};

const toggleEnabled = async (row: ColumnMappingRule) => {
  await persistRow(row);
};

const remove = async (row: ColumnMappingRule) => {
  if (!ensurePermission("btn:column-mapping-rule:delete", "权限不足，无法删除列映射规则")) {
    return;
  }

  try {
    await ElMessageBox.confirm(`确定删除匹配词 "${row.pattern}" 吗？`, "提示", {
      confirmButtonText: "确定",
      cancelButtonText: "取消",
      type: "warning"
    });
    const res = await deleteColumnMappingRule(row.id);
    if (res.code === 0) {
      ElMessage.success("删除成功");
      await load();
    } else {
      ElMessage.error(res.message || "删除失败");
    }
  } catch {
    // 用户取消
  }
};

onMounted(load);
</script>

<template>
  <div class="page column-rules config-page">
    <div class="page-header">
      <div>
        <div class="page-title">列映射规则</div>
        <div class="page-subtitle">Word 表头关键字映射与优先级配置</div>
      </div>
    </div>
    <el-card>
      <template #header>
        <div class="flex justify-between items-center">
          <span>列映射规则（全局）</span>
          <div>
            <el-button v-if="canCreate" type="primary" @click="openAdd">
              新增规则
            </el-button>
          </div>
        </div>
      </template>

      <div class="mb-3 text-sm text-gray-500">
        用于 Word 导入和智能填充时自动识别表头列，例如将“工艺流程 / 项目 / 项目管理”都映射到“项目”列。
      </div>

      <el-tabs v-model="activeTarget" type="card">
        <el-tab-pane
          v-for="target in targetOptions"
          :key="target.value"
          :name="target.value"
          :label="target.label"
        >
          <div class="rule-toolbar">
            <el-input
              v-model="tabKeywords[target.value]"
              class="rule-search-input"
              placeholder="搜索当前字段的匹配词 / 匹配模式"
              clearable
            />
          </div>

          <el-table
            v-loading="loading"
            :data="filteredRulesByTarget[target.value]"
            stripe
            border
          >
            <el-table-column prop="pattern" label="匹配词" min-width="200" />
            <el-table-column label="匹配模式" min-width="160">
              <template #default="{ row }">
                <el-select
                  v-model="row.matchMode"
                  size="small"
                  :disabled="!canUpdate"
                  class="table-select"
                  popper-class="config-select-popper"
                  @change="() => persistRow(row)"
                >
                  <el-option
                    v-for="mode in matchModeOptions"
                    :key="mode.value"
                    :label="mode.label"
                    :value="mode.value"
                  />
                </el-select>
              </template>
            </el-table-column>
            <el-table-column label="优先级" width="140">
              <template #default="{ row }">
                <el-input-number
                  v-model="row.priority"
                  class="table-number-input"
                  size="small"
                  :disabled="!canUpdate"
                  :min="-999"
                  :max="999"
                  controls-position="right"
                  @change="() => persistRow(row)"
                />
              </template>
            </el-table-column>
            <el-table-column label="启用" width="90">
              <template #default="{ row }">
                <el-switch
                  v-model="row.enabled"
                  :disabled="!canUpdate"
                  @change="() => toggleEnabled(row)"
                />
              </template>
            </el-table-column>
            <el-table-column
              v-if="hasOperationActions"
              label="操作"
              width="150"
              fixed="right"
            >
              <template #default="{ row }">
                <el-button v-if="canUpdate" type="primary" link @click="openEdit(row)">
                  编辑
                </el-button>
                <el-button v-if="canDelete" type="danger" link @click="remove(row)">
                  删除
                </el-button>
              </template>
            </el-table-column>
          </el-table>
        </el-tab-pane>
      </el-tabs>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="dialogTitle" width="520">
      <el-form label-width="90px">
        <el-form-item label="目标字段" required>
          <el-select v-model="form.targetField" popper-class="config-select-popper">
            <el-option
              v-for="target in targetOptions"
              :key="target.value"
              :label="target.label"
              :value="target.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="匹配模式">
          <el-select v-model="form.matchMode" popper-class="config-select-popper">
            <el-option
              v-for="mode in matchModeOptions"
              :key="mode.value"
              :label="mode.label"
              :value="mode.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="匹配词" required>
          <el-input v-model="form.pattern" placeholder="例如：项目管理" />
        </el-form-item>
        <el-form-item label="优先级">
          <el-input-number v-model="form.priority" :min="-999" :max="999" />
        </el-form-item>
        <el-form-item label="启用">
          <el-switch v-model="form.enabled" />
        </el-form-item>
      </el-form>
      <template #footer>
        <el-button @click="dialogVisible = false">取消</el-button>
        <el-button v-if="canSubmit" type="primary" @click="submit">
          保存
        </el-button>
      </template>
    </el-dialog>
  </div>
</template>

<style scoped>
.rule-toolbar {
  margin-bottom: 16px;
}

.rule-search-input {
  width: 100%;
  max-width: 360px;
}

.table-select {
  display: block;
  width: 100%;
}

:deep(.table-select .el-select__wrapper) {
  min-width: 0;
}

.table-number-input {
  width: 100%;
}
</style>
