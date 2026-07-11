<script setup lang="ts">
import type {
  BatchReplyDuplicateGroup,
  BatchReplyDuplicateStrategy
} from "@/api/matching";
import type { BatchReplyDuplicateDialogState } from "../batch-reply-duplicates";

defineProps<{
  dialog: BatchReplyDuplicateDialogState | null;
  modelValue: boolean;
  getSourceLabel: (
    duplicateSource: BatchReplyDuplicateGroup["duplicateSource"]
  ) => string;
}>();

defineEmits<{
  "update:strategy": [groupId: string, strategy: BatchReplyDuplicateStrategy];
  close: [];
  confirm: [];
}>();
</script>

<template>
  <el-dialog
    :model-value="modelValue"
    title="重复项处理"
    width="min(960px, calc(100vw - 32px))"
    destroy-on-close
    @close="$emit('close')"
  >
    <template v-if="dialog">
      <div class="duplicate-dialog__intro">
        当前 Sheet/表格存在重复的“项目 +
        规格”组合。请为每个冲突组选择处理策略，确认后系统会自动重新预览这张表。
      </div>

      <div class="duplicate-group-list">
        <div
          v-for="group in dialog.groups"
          :key="group.groupId"
          class="duplicate-group-card"
        >
          <div class="duplicate-group-card__header">
            <div class="duplicate-group-card__meta">
              <el-tag
                size="small"
                :type="
                  group.duplicateSource === 'source' ? 'warning' : 'danger'
                "
                effect="plain"
              >
                {{ getSourceLabel(group.duplicateSource) }}
              </el-tag>
              <strong>{{ group.project || "空项目" }}</strong>
              <span>/</span>
              <span>{{ group.specification || "空规格" }}</span>
            </div>
            <el-select
              :model-value="dialog.strategies[group.groupId]"
              class="duplicate-group-card__select"
              @update:model-value="
                $emit('update:strategy', group.groupId, $event)
              "
            >
              <el-option label="保留首条" value="keepFirst" />
              <el-option label="保留末条" value="keepLast" />
              <el-option label="跳过该组" value="skip" />
            </el-select>
          </div>

          <el-table
            :data="group.rows"
            border
            size="small"
            class="duplicate-group-card__table"
          >
            <el-table-column prop="rowIndex" label="行号" width="90" />
            <el-table-column
              prop="project"
              label="项目"
              min-width="min(150px, calc(100vw - 32px))"
            />
            <el-table-column
              prop="specification"
              label="规格"
              min-width="min(180px, calc(100vw - 32px))"
            />
            <el-table-column
              prop="acceptance"
              label="验收"
              min-width="min(180px, calc(100vw - 32px))"
            />
            <el-table-column
              prop="remark"
              label="备注"
              min-width="min(180px, calc(100vw - 32px))"
            />
          </el-table>
        </div>
      </div>
    </template>

    <template #footer>
      <div class="duplicate-dialog__footer">
        <el-button @click="$emit('close')">取消</el-button>
        <el-button type="primary" @click="$emit('confirm')">
          确认处理
        </el-button>
      </div>
    </template>
  </el-dialog>
</template>
