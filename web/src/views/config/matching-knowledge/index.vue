<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from "vue";
import { ElMessage, ElMessageBox } from "element-plus";
import MatchingKnowledgeDraftDialog from "./components/MatchingKnowledgeDraftDialog.vue";
import {
  clearMatchingKnowledge,
  getMatchingKnowledge,
  restoreDefaultMatchingKnowledge,
  type MatchingKnowledgeConflictGroup,
  type MatchingKnowledgeDraftCategory,
  type MatchingKnowledgeDraftItem,
  type MatchingKnowledgeGroup,
  type MatchingKnowledgeLayer,
  updateMatchingKnowledge
} from "@/api/matching-knowledge";
import { hasPerms } from "@/utils/auth";
import { ensurePermission } from "@/utils/permission-guard";

defineOptions({
  name: "MatchingKnowledgeConfig"
});

interface EditableGroupRow {
  id: number;
  text: string;
  editing: boolean;
  originalText: string;
  isNew: boolean;
}

interface EditableNumberRow {
  id: number;
  key: string;
  value: number | null;
  editing: boolean;
  originalKey: string;
  originalValue: number | null;
  isNew: boolean;
}

interface EditableConflictGroupRow {
  id: number;
  leftText: string;
  rightText: string;
  editing: boolean;
  originalLeftText: string;
  originalRightText: string;
  isNew: boolean;
}

const GROUP_SEPARATOR_PATTERN = /[、，,]/;

const loading = ref(false);
const activeTab = ref("entityAliases");
const entityGroupRows = ref<EditableGroupRow[]>([]);
const unitGroupRows = ref<EditableGroupRow[]>([]);
const unitFactorRows = ref<EditableNumberRow[]>([]);
const fieldGroupRows = ref<EditableGroupRow[]>([]);
const conflictGroupRows = ref<EditableConflictGroupRow[]>([]);
const entitySearchQuery = ref("");
const unitSearchQuery = ref("");
const fieldSearchQuery = ref("");
const conflictSearchQuery = ref("");

const canUpdate = computed(() => hasPerms("btn:matching-knowledge:update"));
const canReset = computed(() => hasPerms("btn:matching-knowledge:reset"));
const canGenerateDraft = computed(() =>
  hasPerms("btn:matching-knowledge:generate-draft")
);
const draftDialogVisible = ref(false);
const draftDialogCategory = ref<MatchingKnowledgeDraftCategory>("entityAliases");
const pageRef = ref<HTMLElement | null>(null);
const pageViewportHeight = ref(0);
let appMainWrapEl: HTMLElement | null = null;
let previousAppMainOverflowY = "";

let nextRowId = 1;

const allocateRowId = () => {
  const id = nextRowId;
  nextRowId += 1;
  return id;
};

const createGroupRow = (
  text = "",
  editing = false,
  isNew = false
): EditableGroupRow => ({
  id: allocateRowId(),
  text,
  editing,
  originalText: text,
  isNew
});

const createNumberRow = (
  key = "",
  value: number | null = null,
  editing = false,
  isNew = false
): EditableNumberRow => ({
  id: allocateRowId(),
  key,
  value,
  editing,
  originalKey: key,
  originalValue: value,
  isNew
});

const createConflictGroupRow = (
  leftText = "",
  rightText = "",
  editing = false,
  isNew = false
): EditableConflictGroupRow => ({
  id: allocateRowId(),
  leftText,
  rightText,
  editing,
  originalLeftText: leftText,
  originalRightText: rightText,
  isNew
});

const normalizeValue = (value: string) => value.trim();
const normalizeKey = (value: string) => normalizeValue(value).toLowerCase();
const normalizeSearchQuery = (value: string) => normalizeKey(value);

const matchesSearchToken = (candidate: string, searchQuery: string) => {
  const normalizedCandidate = normalizeKey(candidate);
  return (
    normalizedCandidate === searchQuery ||
    normalizedCandidate.startsWith(searchQuery)
  );
};

const parseGroupItems = (value: string) => {
  const result: string[] = [];
  const seen = new Set<string>();

  value
    .split(GROUP_SEPARATOR_PATTERN)
    .map(item => item.trim())
    .forEach(item => {
      const normalized = item.toLowerCase();
      if (!item || seen.has(normalized)) {
        return;
      }

      seen.add(normalized);
      result.push(item);
    });

  return result;
};

const joinGroupItems = (items: string[]) => items.join("、");

const matchesGroupSearch = (text: string, searchQuery: string) => {
  const normalizedQuery = normalizeSearchQuery(searchQuery);
  if (!normalizedQuery) {
    return true;
  }

  const items = parseGroupItems(text);
  if (items.some(item => matchesSearchToken(item, normalizedQuery))) {
    return true;
  }

  return normalizeKey(text).includes(normalizedQuery);
};

const matchesConflictGroupSearch = (
  row: EditableConflictGroupRow,
  searchQuery: string
) => {
  const normalizedQuery = normalizeSearchQuery(searchQuery);
  if (!normalizedQuery) {
    return true;
  }

  const leftItems = parseGroupItems(row.leftText);
  const rightItems = parseGroupItems(row.rightText);
  if (
    [...leftItems, ...rightItems].some(item =>
      matchesSearchToken(item, normalizedQuery)
    )
  ) {
    return true;
  }

  return normalizeKey(`${row.leftText} ${row.rightText}`).includes(normalizedQuery);
};

const matchesNumberRowSearch = (row: EditableNumberRow, searchQuery: string) => {
  const normalizedQuery = normalizeSearchQuery(searchQuery);
  if (!normalizedQuery) {
    return true;
  }

  const normalizedKey = normalizeKey(row.key);
  if (
    normalizedKey === normalizedQuery ||
    normalizedKey.startsWith(normalizedQuery) ||
    normalizedKey.includes(normalizedQuery)
  ) {
    return true;
  }

  const valueText =
    row.value === null || Number.isNaN(row.value) ? "" : `${row.value}`.toLowerCase();
  return valueText.includes(normalizedQuery);
};

const buildGroupRows = (source?: MatchingKnowledgeGroup[]) =>
  (source ?? []).map(group => createGroupRow(joinGroupItems(group.items ?? [])));

const buildNumberRows = (source?: Record<string, number>) =>
  Object.entries(source ?? {}).map(([key, value]) =>
    createNumberRow(key, value)
  );

const buildConflictGroupRows = (source?: MatchingKnowledgeConflictGroup[]) =>
  (source ?? []).map(group =>
    createConflictGroupRow(
      joinGroupItems(group.leftItems ?? []),
      joinGroupItems(group.rightItems ?? [])
    )
  );

const toGroups = (rows: EditableGroupRow[]): MatchingKnowledgeGroup[] =>
  rows
    .map(row => ({
      items: parseGroupItems(row.text)
    }))
    .filter(group => group.items.length > 0);

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

const toConflictGroups = (
  rows: EditableConflictGroupRow[]
): MatchingKnowledgeConflictGroup[] =>
  rows
    .map(row => ({
      leftItems: parseGroupItems(row.leftText),
      rightItems: parseGroupItems(row.rightText)
    }))
    .filter(group => group.leftItems.length > 0 || group.rightItems.length > 0);

const applyConfig = (config: MatchingKnowledgeLayer) => {
  entityGroupRows.value = buildGroupRows(config.entityGroups);
  unitGroupRows.value = buildGroupRows(config.unitGroups);
  unitFactorRows.value = buildNumberRows(config.unitFactors);
  fieldGroupRows.value = buildGroupRows(config.fieldGroups);
  conflictGroupRows.value = buildConflictGroupRows(config.conflictGroups);
};

const buildPayload = (): MatchingKnowledgeLayer => ({
  entityGroups: toGroups(entityGroupRows.value),
  unitGroups: toGroups(unitGroupRows.value),
  unitFactors: toNumberDictionary(unitFactorRows.value),
  fieldGroups: toGroups(fieldGroupRows.value),
  conflictGroups: toConflictGroups(conflictGroupRows.value)
});

const filteredEntityGroupRows = computed(() =>
  entityGroupRows.value.filter(row =>
    matchesGroupSearch(row.text, entitySearchQuery.value)
  )
);

const filteredUnitGroupRows = computed(() =>
  unitGroupRows.value.filter(row =>
    matchesGroupSearch(row.text, unitSearchQuery.value)
  )
);

const filteredUnitFactorRows = computed(() =>
  unitFactorRows.value.filter(row =>
    matchesNumberRowSearch(row, unitSearchQuery.value)
  )
);

const filteredFieldGroupRows = computed(() =>
  fieldGroupRows.value.filter(row =>
    matchesGroupSearch(row.text, fieldSearchQuery.value)
  )
);

const filteredConflictGroupRows = computed(() =>
  conflictGroupRows.value.filter(row =>
    matchesConflictGroupSearch(row, conflictSearchQuery.value)
  )
);

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

const addGroupRow = (target: EditableGroupRow[]) => {
  target.push(createGroupRow("", true, true));
};

const addNumberRow = () => {
  unitFactorRows.value.push(createNumberRow("", null, true, true));
};

const addConflictGroupRow = () => {
  conflictGroupRows.value.push(createConflictGroupRow("", "", true, true));
};

const removeGroupRow = (target: EditableGroupRow[], id: number) => {
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

const removeConflictGroupRow = (id: number) => {
  const index = conflictGroupRows.value.findIndex(row => row.id === id);
  if (index >= 0) {
    conflictGroupRows.value.splice(index, 1);
  }
};

const finalizeGroupRowText = (value: string) =>
  joinGroupItems(parseGroupItems(value));

const startGroupRowEdit = (row: EditableGroupRow) => {
  row.originalText = row.text;
  row.editing = true;
};

const completeGroupRowEdit = (row: EditableGroupRow) => {
  row.text = finalizeGroupRowText(row.text);
  if (!row.text && row.isNew) {
    row.editing = false;
    return;
  }

  row.originalText = row.text;
  row.editing = false;
  row.isNew = false;
};

const cancelGroupRowEdit = (target: EditableGroupRow[], id: number) => {
  const row = target.find(item => item.id === id);
  if (!row) {
    return;
  }

  if (row.isNew) {
    removeGroupRow(target, id);
    return;
  }

  row.text = row.originalText;
  row.editing = false;
};

const formatGroupRowText = (value: string) => finalizeGroupRowText(value) || "未填写";

const startNumberRowEdit = (row: EditableNumberRow) => {
  row.originalKey = row.key;
  row.originalValue = row.value;
  row.editing = true;
};

const completeNumberRowEdit = (row: EditableNumberRow) => {
  row.key = row.key.trim();
  if (!row.key && row.value === null && row.isNew) {
    row.editing = false;
    return;
  }

  row.originalKey = row.key;
  row.originalValue = row.value;
  row.editing = false;
  row.isNew = false;
};

const cancelNumberRowEdit = (id: number) => {
  const row = unitFactorRows.value.find(item => item.id === id);
  if (!row) {
    return;
  }

  if (row.isNew) {
    removeNumberRow(id);
    return;
  }

  row.key = row.originalKey;
  row.value = row.originalValue;
  row.editing = false;
};

const formatNumberValue = (value: number | null) =>
  value === null || Number.isNaN(value) ? "未填写" : `${value}`;

const startConflictGroupRowEdit = (row: EditableConflictGroupRow) => {
  row.originalLeftText = row.leftText;
  row.originalRightText = row.rightText;
  row.editing = true;
};

const completeConflictGroupRowEdit = (row: EditableConflictGroupRow) => {
  row.leftText = finalizeGroupRowText(row.leftText);
  row.rightText = finalizeGroupRowText(row.rightText);
  if (!row.leftText && !row.rightText && row.isNew) {
    row.editing = false;
    return;
  }

  row.originalLeftText = row.leftText;
  row.originalRightText = row.rightText;
  row.editing = false;
  row.isNew = false;
};

const cancelConflictGroupRowEdit = (id: number) => {
  const row = conflictGroupRows.value.find(item => item.id === id);
  if (!row) {
    return;
  }

  if (row.isNew) {
    removeConflictGroupRow(id);
    return;
  }

  row.leftText = row.originalLeftText;
  row.rightText = row.originalRightText;
  row.editing = false;
};

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

const buildGroupIndex = (rows: EditableGroupRow[]) => {
  const rowItems = new Map<number, string[]>();
  const canonicalByTerm = new Map<string, string>();
  const rowByCanonical = new Map<string, EditableGroupRow>();

  rows.forEach(row => {
    const items = parseGroupItems(row.text);
    if (items.length === 0) {
      return;
    }

    rowItems.set(row.id, items);
    rowByCanonical.set(normalizeKey(items[0]), row);
    items.forEach(item => {
      canonicalByTerm.set(normalizeKey(item), items[0]);
    });
  });

  return {
    rowItems,
    canonicalByTerm,
    rowByCanonical
  };
};

const mergeMappingDraftItems = (
  targetRows: EditableGroupRow[],
  items: MatchingKnowledgeDraftItem[]
) => {
  const index = buildGroupIndex(targetRows);
  let imported = 0;
  let mergedIntoExisting = 0;
  let createdGroups = 0;
  let duplicate = 0;
  let conflict = 0;
  let invalid = 0;

  items.forEach(item => {
    const alias = normalizeValue(item.key);
    const canonical = normalizeValue(item.value);
    if (!alias || !canonical) {
      invalid += 1;
      return;
    }

    const aliasKey = normalizeKey(alias);
    const canonicalKey = normalizeKey(canonical);
    const existingAliasCanonical = index.canonicalByTerm.get(aliasKey);
    const existingCanonical = index.canonicalByTerm.get(canonicalKey);

    if (existingAliasCanonical) {
      if (normalizeKey(existingAliasCanonical) === canonicalKey) {
        duplicate += 1;
      } else {
        conflict += 1;
      }
      return;
    }

    if (existingCanonical && normalizeKey(existingCanonical) !== canonicalKey) {
      conflict += 1;
      return;
    }

    const existingRow = index.rowByCanonical.get(canonicalKey);
    if (existingRow) {
      const existingItems = index.rowItems.get(existingRow.id) ?? [canonical];
      existingItems.push(alias);
      existingRow.text = joinGroupItems(parseGroupItems(joinGroupItems(existingItems)));
      index.rowItems.set(existingRow.id, parseGroupItems(existingRow.text));
      index.canonicalByTerm.set(aliasKey, canonical);
      imported += 1;
      mergedIntoExisting += 1;
      return;
    }

    const newItems =
      aliasKey === canonicalKey ? [canonical] : [canonical, alias];
    const newRow = createGroupRow(joinGroupItems(newItems));
    targetRows.push(newRow);
    index.rowItems.set(newRow.id, newItems);
    index.rowByCanonical.set(canonicalKey, newRow);
    newItems.forEach(groupItem => {
      index.canonicalByTerm.set(normalizeKey(groupItem), canonical);
    });
    imported += 1;
    createdGroups += 1;
  });

  return {
    imported,
    mergedIntoExisting,
    createdGroups,
    duplicate,
    conflict,
    invalid
  };
};

const expandConflictGroupRows = (rows: EditableConflictGroupRow[]) => {
  const pairKeys = new Set<string>();
  rows.forEach(row => {
    const leftItems = parseGroupItems(row.leftText);
    const rightItems = parseGroupItems(row.rightText);
    leftItems.forEach(left => {
      rightItems.forEach(right => {
        const pairKey = buildConflictPairKey(left, right);
        if (pairKey) {
          pairKeys.add(pairKey);
        }
      });
    });
  });
  return pairKeys;
};

const mergeConflictDraftItems = (items: MatchingKnowledgeDraftItem[]) => {
  const existingPairs = expandConflictGroupRows(conflictGroupRows.value);
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

    conflictGroupRows.value.push(createConflictGroupRow(left, right));
    existingPairs.add(pairKey);
    imported += 1;
  });

  return {
    imported,
    mergedIntoExisting: 0,
    createdGroups: imported,
    duplicate,
    conflict: 0,
    invalid
  };
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
      ? mergeMappingDraftItems(entityGroupRows.value, payload.items)
      : payload.category === "unitAliases"
        ? mergeMappingDraftItems(unitGroupRows.value, payload.items)
        : payload.category === "fieldAliases"
          ? mergeMappingDraftItems(fieldGroupRows.value, payload.items)
          : mergeConflictDraftItems(payload.items);

  const messages: string[] = [];
  if (result.imported > 0) {
    messages.push(`已导入 ${result.imported} 条`);
  }
  if (result.mergedIntoExisting > 0) {
    messages.push(`${result.mergedIntoExisting} 条并入已有组`);
  }
  if (result.createdGroups > 0) {
    messages.push(`${result.createdGroups} 条新建组`);
  }
  if (result.duplicate > 0) {
    messages.push(`${result.duplicate} 条重复已忽略`);
  }
  if (result.conflict > 0) {
    messages.push(`${result.conflict} 条候选与现有分组冲突未导入`);
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

const updatePageViewportHeight = () => {
  const host = pageRef.value;
  if (!host) return;
  const rect = host.getBoundingClientRect();
  pageViewportHeight.value = Math.max(
    480,
    Math.floor(window.innerHeight - rect.top - 12)
  );
};

const lockOuterScroll = () => {
  appMainWrapEl = document.querySelector(
    ".app-main .el-scrollbar__wrap, .app-main-nofixed-header .el-scrollbar__wrap"
  ) as HTMLElement | null;
  if (!appMainWrapEl) return;
  previousAppMainOverflowY = appMainWrapEl.style.overflowY;
  appMainWrapEl.style.setProperty("overflow-y", "hidden", "important");
};

const unlockOuterScroll = () => {
  if (!appMainWrapEl) return;
  appMainWrapEl.style.removeProperty("overflow-y");
  if (previousAppMainOverflowY) {
    appMainWrapEl.style.overflowY = previousAppMainOverflowY;
  }
  appMainWrapEl = null;
};

onMounted(() => {
  load();
  nextTick(updatePageViewportHeight);
  nextTick(lockOuterScroll);
  window.addEventListener("resize", updatePageViewportHeight);
});

onBeforeUnmount(() => {
  window.removeEventListener("resize", updatePageViewportHeight);
  unlockOuterScroll();
});
</script>

<template>
  <div
    ref="pageRef"
    class="page config-page"
    :style="pageViewportHeight > 0 ? { height: `${pageViewportHeight}px` } : undefined"
  >
    <div class="page-header">
      <div>
        <div class="page-title">匹配知识配置</div>
        <div class="page-subtitle">
          当前生效配置。组内使用 、 、， 或 , 分隔，首项作为标准值。
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

    <el-tabs v-model="activeTab" v-loading="loading" class="knowledge-tabs">
      <el-tab-pane label="实体组" name="entityAliases" class="single-card-pane">
        <el-card class="knowledge-card">
          <template #header>
            <div class="card-header">
              <div>
                <div class="card-title">实体组</div>
                <div class="card-subtitle">首项作为标准实体，其余词项自动归一到首项。</div>
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
                  @click="addGroupRow(entityGroupRows)"
                >
                  新增
                </el-button>
              </div>
            </div>
          </template>
          <div class="tab-search-row">
            <el-input
              v-model="entitySearchQuery"
              clearable
              placeholder="搜索当前 Tab"
            />
          </div>
          <el-table
            :data="filteredEntityGroupRows"
            row-key="id"
            :empty-text="entitySearchQuery ? '没有匹配的实体组' : '暂无实体组'"
          >
            <el-table-column label="实体组" min-width="420">
              <template #default="{ row }">
                <el-input
                  v-if="row.editing"
                  v-model="row.text"
                  placeholder="输入同一实体的多个叫法，首项作为标准实体"
                />
                <div v-else class="row-display-text">
                  {{ formatGroupRowText(row.text) }}
                </div>
              </template>
            </el-table-column>
            <el-table-column v-if="canUpdate" label="操作" width="180" fixed="right">
              <template #default="{ row }">
                <template v-if="row.editing">
                  <el-button type="primary" link @click="completeGroupRowEdit(row)">
                    完成
                  </el-button>
                  <el-button link @click="cancelGroupRowEdit(entityGroupRows, row.id)">
                    取消
                  </el-button>
                </template>
                <template v-else>
                  <el-button type="primary" link @click="startGroupRowEdit(row)">
                    编辑
                  </el-button>
                  <el-button type="danger" link @click="removeGroupRow(entityGroupRows, row.id)">
                    删除
                  </el-button>
                </template>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-tab-pane>

      <el-tab-pane label="单位规则" name="unitRules" class="multi-card-pane">
        <div class="knowledge-grid">
          <div class="tab-search-row">
            <el-input
              v-model="unitSearchQuery"
              clearable
              placeholder="搜索当前 Tab"
            />
          </div>
          <el-card class="knowledge-card">
            <template #header>
              <div class="card-header">
                <div>
                  <div class="card-title">单位组</div>
                  <div class="card-subtitle">首项作为标准单位，其余词项自动归一到首项。</div>
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
                    @click="addGroupRow(unitGroupRows)"
                  >
                    新增
                  </el-button>
                </div>
              </div>
            </template>
            <el-table
              :data="filteredUnitGroupRows"
              row-key="id"
              :empty-text="unitSearchQuery ? '没有匹配的单位组' : '暂无单位组'"
            >
              <el-table-column label="单位组" min-width="420">
                <template #default="{ row }">
                  <el-input
                    v-if="row.editing"
                    v-model="row.text"
                    placeholder="输入同一单位的多个写法，首项作为标准单位"
                  />
                  <div v-else class="row-display-text">
                    {{ formatGroupRowText(row.text) }}
                  </div>
                </template>
              </el-table-column>
              <el-table-column v-if="canUpdate" label="操作" width="180" fixed="right">
                <template #default="{ row }">
                  <template v-if="row.editing">
                    <el-button type="primary" link @click="completeGroupRowEdit(row)">
                      完成
                    </el-button>
                    <el-button link @click="cancelGroupRowEdit(unitGroupRows, row.id)">
                      取消
                    </el-button>
                  </template>
                  <template v-else>
                    <el-button type="primary" link @click="startGroupRowEdit(row)">
                      编辑
                    </el-button>
                    <el-button type="danger" link @click="removeGroupRow(unitGroupRows, row.id)">
                      删除
                    </el-button>
                  </template>
                </template>
              </el-table-column>
            </el-table>
          </el-card>

          <el-card class="knowledge-card">
            <template #header>
              <div class="card-header">
                <div>
                  <div class="card-title">单位换算</div>
                </div>
                <div class="card-toolbar">
                  <el-button v-if="canUpdate" type="primary" link @click="addNumberRow">
                    新增
                  </el-button>
                </div>
              </div>
            </template>
            <el-table
              :data="filteredUnitFactorRows"
              row-key="id"
              :empty-text="unitSearchQuery ? '没有匹配的单位换算' : '暂无单位换算'"
            >
              <el-table-column label="标准单位" min-width="220">
                <template #default="{ row }">
                  <el-input v-if="row.editing" v-model="row.key" placeholder="输入标准单位" />
                  <div v-else class="row-display-text">
                    {{ row.key || "未填写" }}
                  </div>
                </template>
              </el-table-column>
              <el-table-column label="归一系数" min-width="220">
                <template #default="{ row }">
                  <el-input-number
                    v-if="row.editing"
                    v-model="row.value"
                    :controls="false"
                    style="width: 100%"
                  />
                  <div v-else class="row-display-text">
                    {{ formatNumberValue(row.value) }}
                  </div>
                </template>
              </el-table-column>
              <el-table-column v-if="canUpdate" label="操作" width="180" fixed="right">
                <template #default="{ row }">
                  <template v-if="row.editing">
                    <el-button type="primary" link @click="completeNumberRowEdit(row)">
                      完成
                    </el-button>
                    <el-button link @click="cancelNumberRowEdit(row.id)">
                      取消
                    </el-button>
                  </template>
                  <template v-else>
                    <el-button type="primary" link @click="startNumberRowEdit(row)">
                      编辑
                    </el-button>
                    <el-button type="danger" link @click="removeNumberRow(row.id)">
                      删除
                    </el-button>
                  </template>
                </template>
              </el-table-column>
            </el-table>
          </el-card>
        </div>
      </el-tab-pane>

      <el-tab-pane label="字段组" name="fieldAliases" class="single-card-pane">
        <el-card class="knowledge-card">
          <template #header>
            <div class="card-header">
              <div>
                <div class="card-title">字段组</div>
                <div class="card-subtitle">首项作为标准字段，其余词项自动归一到首项。</div>
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
                  @click="addGroupRow(fieldGroupRows)"
                >
                  新增
                </el-button>
              </div>
            </div>
          </template>
          <div class="tab-search-row">
            <el-input
              v-model="fieldSearchQuery"
              clearable
              placeholder="搜索当前 Tab"
            />
          </div>
          <el-table
            :data="filteredFieldGroupRows"
            row-key="id"
            :empty-text="fieldSearchQuery ? '没有匹配的字段组' : '暂无字段组'"
          >
            <el-table-column label="字段组" min-width="420">
              <template #default="{ row }">
                <el-input
                  v-if="row.editing"
                  v-model="row.text"
                  placeholder="输入同一字段的多个叫法，首项作为标准字段"
                />
                <div v-else class="row-display-text">
                  {{ formatGroupRowText(row.text) }}
                </div>
              </template>
            </el-table-column>
            <el-table-column v-if="canUpdate" label="操作" width="180" fixed="right">
              <template #default="{ row }">
                <template v-if="row.editing">
                  <el-button type="primary" link @click="completeGroupRowEdit(row)">
                    完成
                  </el-button>
                  <el-button link @click="cancelGroupRowEdit(fieldGroupRows, row.id)">
                    取消
                  </el-button>
                </template>
                <template v-else>
                  <el-button type="primary" link @click="startGroupRowEdit(row)">
                    编辑
                  </el-button>
                  <el-button type="danger" link @click="removeGroupRow(fieldGroupRows, row.id)">
                    删除
                  </el-button>
                </template>
              </template>
            </el-table-column>
          </el-table>
        </el-card>
      </el-tab-pane>

      <el-tab-pane label="冲突组" name="conflictPairs" class="single-card-pane">
        <el-card class="knowledge-card">
          <template #header>
            <div class="card-header">
              <div>
                <div class="card-title">冲突组</div>
                <div class="card-subtitle">
                  左右两组内部表示同义词集合，左右两组之间表示冲突关系。
                </div>
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
                  @click="addConflictGroupRow"
                >
                  新增
                </el-button>
              </div>
            </div>
          </template>
          <div class="tab-search-row">
            <el-input
              v-model="conflictSearchQuery"
              clearable
              placeholder="搜索当前 Tab"
            />
          </div>
          <el-table
            :data="filteredConflictGroupRows"
            row-key="id"
            :empty-text="conflictSearchQuery ? '没有匹配的冲突组' : '暂无冲突组'"
          >
            <el-table-column label="左冲突组" min-width="260">
              <template #default="{ row }">
                <el-input
                  v-if="row.editing"
                  v-model="row.leftText"
                  placeholder="输入左冲突组，组内使用分隔符"
                />
                <div v-else class="row-display-text">
                  {{ formatGroupRowText(row.leftText) }}
                </div>
              </template>
            </el-table-column>
            <el-table-column label="右冲突组" min-width="260">
              <template #default="{ row }">
                <el-input
                  v-if="row.editing"
                  v-model="row.rightText"
                  placeholder="输入右冲突组，组内使用分隔符"
                />
                <div v-else class="row-display-text">
                  {{ formatGroupRowText(row.rightText) }}
                </div>
              </template>
            </el-table-column>
            <el-table-column v-if="canUpdate" label="操作" width="180" fixed="right">
              <template #default="{ row }">
                <template v-if="row.editing">
                  <el-button type="primary" link @click="completeConflictGroupRowEdit(row)">
                    完成
                  </el-button>
                  <el-button link @click="cancelConflictGroupRowEdit(row.id)">
                    取消
                  </el-button>
                </template>
                <template v-else>
                  <el-button type="primary" link @click="startConflictGroupRowEdit(row)">
                    编辑
                  </el-button>
                  <el-button type="danger" link @click="removeConflictGroupRow(row.id)">
                    删除
                  </el-button>
                </template>
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
  height: 100%;
  min-height: 0;
  overflow: hidden;
  box-sizing: border-box;
}

.page-header {
  flex-shrink: 0;
}

.page-subtitle,
.card-subtitle {
  margin-top: 4px;
  font-size: 12px;
  line-height: 1.5;
  color: var(--el-text-color-secondary);
}

.page-actions {
  display: flex;
  gap: 12px;
  align-items: center;
  flex-wrap: wrap;
}

.knowledge-tabs {
  margin-top: 4px;
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

:deep(.knowledge-tabs > .el-tabs__content) {
  flex: 1;
  min-height: 0;
  overflow: hidden;
}

:deep(.knowledge-tabs > .el-tabs__content > .el-tab-pane) {
  height: 100%;
  box-sizing: border-box;
  padding-right: 4px;
}

.knowledge-grid {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.knowledge-card {
  border-radius: 16px;
}

.tab-search-row {
  margin-bottom: 16px;
}

.row-display-text {
  min-height: 32px;
  display: flex;
  align-items: center;
  line-height: 1.6;
  color: var(--el-text-color-primary);
  word-break: break-all;
}

:deep(.knowledge-tabs > .el-tabs__content > .el-tab-pane.multi-card-pane) {
  overflow: auto;
}

:deep(.knowledge-tabs > .el-tabs__content > .el-tab-pane.single-card-pane) {
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

:deep(.knowledge-tabs > .el-tabs__content > .el-tab-pane.single-card-pane > .knowledge-card) {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
}

:deep(
    .knowledge-tabs
      > .el-tabs__content
      > .el-tab-pane.single-card-pane
      > .knowledge-card
      > .el-card__body
  ) {
  flex: 1;
  min-height: 0;
  overflow: auto;
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
