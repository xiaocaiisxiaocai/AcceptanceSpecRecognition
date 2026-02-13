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

defineOptions({
  name: "ColumnMappingRules"
});

const loading = ref(false);
const rules = ref<ColumnMappingRule[]>([]);
const tableSelectWidth = 320;
const tableSelectClass = `table-select table-select--${tableSelectWidth}`;

const activeTarget = ref<ColumnMappingTargetField>(ColumnMappingTargetField.Project);

const targetOptions = [
  { label: "项目", value: ColumnMappingTargetField.Project },
  { label: "规格内容", value: ColumnMappingTargetField.Specification },
  { label: "验收标准", value: ColumnMappingTargetField.Acceptance },
  { label: "备注", value: ColumnMappingTargetField.Remark }
];

const matchModeOptions = [
  { label: "包含", value: ColumnMappingMatchMode.Contains },
  { label: "相等", value: ColumnMappingMatchMode.Equals },
  { label: "正则", value: ColumnMappingMatchMode.Regex }
];

const filteredRules = computed(() =>
  rules.value
    .filter(r => r.targetField === activeTarget.value)
    .sort((a, b) => (b.priority ?? 0) - (a.priority ?? 0) || a.id - b.id)
);

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

// 新增 / 编辑
const dialogVisible = ref(false);
const dialogTitle = ref("新增规则");
const isEdit = ref(false);

const form = reactive({
  id: 0,
  targetField: ColumnMappingTargetField.Project,
  matchMode: ColumnMappingMatchMode.Contains,
  pattern: "",
  priority: 0,
  enabled: true
});

const openAdd = () => {
  isEdit.value = false;
  dialogTitle.value = "新增规则";
  form.id = 0;
  form.targetField = activeTarget.value;
  form.matchMode = ColumnMappingMatchMode.Contains;
  form.pattern = "";
  form.priority = 0;
  form.enabled = true;
  dialogVisible.value = true;
};

const openEdit = (row: ColumnMappingRule) => {
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
  } catch (e: any) {
    ElMessage.error(e?.message || "保存失败");
  }
};

const toggleEnabled = async (row: ColumnMappingRule) => {
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

const remove = async (row: ColumnMappingRule) => {
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
    // cancel
  }
};

onMounted(load);
</script>

<template>
  <div class="page column-rules config-page">
    <div class="page-header">
      <div>
        <div class="page-title">列映射规则</div>
        <div class="page-subtitle">表头关键字映射与优先级配置</div>
      </div>
    </div>
    <el-card>
      <template #header>
        <div class="flex justify-between items-center">
          <span>列映射规则（全局）</span>
          <div>
            <el-button type="primary" @click="openAdd">新增规则</el-button>
          </div>
        </div>
      </template>

      <div class="mb-3 text-sm text-gray-500">
        用于导入数据时自动识别表头列。比如将“工艺流程 / 项目 / 项目管理”都配置为“项目”列。
      </div>

      <el-tabs v-model="activeTarget" type="card">
        <el-tab-pane
          v-for="t in targetOptions"
          :key="t.value"
          :name="t.value"
          :label="t.label"
        />
      </el-tabs>

      <el-table v-loading="loading" :data="filteredRules" stripe border>
        <el-table-column prop="pattern" label="匹配词" min-width="200" />
        <el-table-column label="匹配模式" :width="tableSelectWidth">
          <template #default="{ row }">
            <el-select
              v-model="row.matchMode"
              size="small"
              :class="tableSelectClass"
              popper-class="config-select-popper"
              @change="
                () =>
                  updateColumnMappingRule(row.id, {
                    targetField: row.targetField,
                    matchMode: row.matchMode,
                    pattern: row.pattern,
                    priority: row.priority ?? 0,
                    enabled: row.enabled
                  }).then(() => load())
              "
            >
              <el-option
                v-for="m in matchModeOptions"
                :key="m.value"
                :label="m.label"
                :value="m.value"
              />
            </el-select>
          </template>
        </el-table-column>
        <el-table-column label="优先级" width="110">
          <template #default="{ row }">
            <el-input-number
              v-model="row.priority"
              size="small"
              :min="-999"
              :max="999"
              controls-position="right"
              @change="
                () =>
                  updateColumnMappingRule(row.id, {
                    targetField: row.targetField,
                    matchMode: row.matchMode,
                    pattern: row.pattern,
                    priority: row.priority ?? 0,
                    enabled: row.enabled
                  }).then(() => load())
              "
            />
          </template>
        </el-table-column>
        <el-table-column label="启用" width="90">
          <template #default="{ row }">
            <el-switch v-model="row.enabled" @change="() => toggleEnabled(row)" />
          </template>
        </el-table-column>
        <el-table-column label="操作" width="150" fixed="right">
          <template #default="{ row }">
            <el-button type="primary" link @click="openEdit(row)">编辑</el-button>
            <el-button type="danger" link @click="remove(row)">删除</el-button>
          </template>
        </el-table-column>
      </el-table>
    </el-card>

    <el-dialog v-model="dialogVisible" :title="dialogTitle" width="520">
      <el-form label-width="90px">
        <el-form-item label="目标字段" required>
          <el-select v-model="form.targetField" popper-class="config-select-popper">
            <el-option
              v-for="t in targetOptions"
              :key="t.value"
              :label="t.label"
              :value="t.value"
            />
          </el-select>
        </el-form-item>
        <el-form-item label="匹配模式">
          <el-select v-model="form.matchMode" popper-class="config-select-popper">
            <el-option
              v-for="m in matchModeOptions"
              :key="m.value"
              :label="m.label"
              :value="m.value"
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
        <el-button type="primary" @click="submit">保存</el-button>
      </template>
    </el-dialog>
  </div>
</template>

