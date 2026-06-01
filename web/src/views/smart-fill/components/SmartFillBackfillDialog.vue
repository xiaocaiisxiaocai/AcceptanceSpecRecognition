<script setup lang="ts">
import type { SmartFillBackfillCandidate } from "../composables/useSmartFillBackfillState";

defineProps<{
  visible: boolean;
  candidates: SmartFillBackfillCandidate[];
  selectedCount: number;
  backfillingSpecs: boolean;
  executing: boolean;
}>();

const emit = defineEmits<{
  (e: "update:visible", value: boolean): void;
  (e: "toggleAll", checked: boolean): void;
  (e: "executeWithoutBackfill"): void;
  (e: "confirmBackfill"): void;
}>();

const handleToggleAll = (checked: string | number | boolean) => {
  emit("toggleAll", Boolean(checked));
};
</script>

<template>
  <el-dialog
    :model-value="visible"
    title="回填验收规格"
    width="1080px"
    :close-on-click-modal="false"
    @update:model-value="emit('update:visible', $event)"
  >
    <div class="backfill-dialog">
      <div class="backfill-summary">
        <span>共 {{ candidates.length }} 条手动修改内容</span>
        <span>已选择 {{ selectedCount }} 条回填</span>
      </div>
      <el-table
        :data="candidates"
        border
        max-height="460"
        row-key="rowIndex"
      >
        <el-table-column width="56" align="center">
          <template #header>
            <el-checkbox
              :model-value="
                candidates.length > 0 &&
                selectedCount === candidates.length
              "
              :indeterminate="
                selectedCount > 0 &&
                selectedCount < candidates.length
              "
              @change="handleToggleAll"
            />
          </template>
          <template #default="{ row }">
            <el-checkbox v-model="row.selected" />
          </template>
        </el-table-column>
        <el-table-column label="行" width="80" align="center">
          <template #default="{ row }">
            {{ row.rowIndex + 1 }}
          </template>
        </el-table-column>
        <el-table-column label="动作" width="120" align="center">
          <template #default="{ row }">
            <el-tag
              size="small"
              :type="row.actionType === 'update' ? 'warning' : 'success'"
            >
              {{ row.actionType === "update" ? "更新现有规格" : "新增规格" }}
            </el-tag>
          </template>
        </el-table-column>
        <el-table-column label="项目/规格" min-width="220">
          <template #default="{ row }">
            <div class="backfill-source">
              <div>{{ row.sourceProject }}</div>
              <div>{{ row.sourceSpecification }}</div>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="验收标准" min-width="260">
          <template #default="{ row }">
            <div class="backfill-change">
              <div class="backfill-change__old">
                {{ row.originalAcceptance || "-" }}
              </div>
              <div class="backfill-change__new">
                {{ row.overrideAcceptance || "-" }}
              </div>
            </div>
          </template>
        </el-table-column>
        <el-table-column label="备注" min-width="220">
          <template #default="{ row }">
            <div class="backfill-change">
              <div class="backfill-change__old">
                {{ row.originalRemark || "-" }}
              </div>
              <div class="backfill-change__new">
                {{ row.overrideRemark || "-" }}
              </div>
            </div>
          </template>
        </el-table-column>
      </el-table>
    </div>
    <template #footer>
      <div class="backfill-footer">
        <el-button @click="emit('executeWithoutBackfill')">
          不回填，仅执行填充
        </el-button>
        <el-button
          type="primary"
          :loading="backfillingSpecs || executing"
          @click="emit('confirmBackfill')"
        >
          确认回填并执行填充
        </el-button>
      </div>
    </template>
  </el-dialog>
</template>
