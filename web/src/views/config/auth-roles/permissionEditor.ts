import type { AuthPermission } from "@/api/auth-permission";

export const permissionTypeDefinitions = [
  { value: 3, label: "菜单" },
  { value: 0, label: "页面" },
  { value: 1, label: "按钮" },
  { value: 2, label: "接口" }
] as const;

export type PermissionTypeValue =
  (typeof permissionTypeDefinitions)[number]["value"];

const resourceLabels: Record<string, string> = {
  "ai-service": "AI 服务",
  "audit-log": "审计日志",
  "auth-permission": "权限字典",
  "auth-role": "角色管理",
  "base-data": "基础数据",
  "batch-reply": "批量回复",
  "column-mapping-rule": "列映射规则",
  config: "配置管理",
  customer: "客户管理",
  dashboard: "仪表盘",
  "database-backup": "数据库备份",
  "data-import": "导入数据",
  document: "导入数据",
  "document-template": "结构模板",
  "embedding-cache-warmup": "Embedding 缓存预热",
  "excel-document": "Excel 导入",
  "execution-history": "执行历史",
  "file-compare": "文件对比",
  home: "首页",
  "machine-model": "机型管理",
  matching: "智能匹配",
  "matching-fill": "智能填充",
  "org-unit": "组织管理",
  other: "其他",
  process: "制程管理",
  "prompt-template": "Prompt 模板",
  rbac: "权限中心",
  "smart-config": "智能结构识别",
  "smart-fill": "智能填充",
  "smart-structure-routing-rule": "表格路由规则",
  spec: "验收规格",
  "system-user": "系统用户"
};

const actionLabels: Record<string, string> = {
  "batch-delete": "批量删除",
  create: "新增",
  delete: "删除",
  "delete-batch": "批量删除",
  "delete-range": "按时间范围删除",
  download: "下载",
  execute: "执行",
  "execute-batch": "批量执行",
  effective: "设为生效",
  import: "导入",
  "llm-stream": "AI 流式处理",
  models: "获取模型",
  move: "移动",
  preview: "预览",
  "preview-batch": "批量预览",
  read: "查看",
  "remark-replace": "批量替换备注",
  "reset-password": "重置密码",
  "reset-system": "恢复系统模板",
  "restore-defaults": "恢复默认配置",
  "semantic-search": "AI 搜索",
  "spec-backfill": "规格回填",
  test: "测试连接",
  update: "编辑",
  "update-status": "启用或停用",
  upload: "上传",
  "upload-source": "上传源文件"
};

export interface PermissionEditorItem extends AuthPermission {
  primaryLabel: string;
  secondaryLabel: string;
  selected: boolean;
}

export interface PermissionResourceGroup {
  resource: string;
  label: string;
  selectedCount: number;
  totalCount: number;
  codes: string[];
  items: PermissionEditorItem[];
}

export interface PermissionTypeSummary {
  value: PermissionTypeValue;
  label: string;
  selectedCount: number;
  totalCount: number;
}

interface BuildPermissionEditorViewOptions {
  permissions: readonly AuthPermission[];
  selectedCodes: readonly string[];
  activeType: PermissionTypeValue;
  keyword: string;
  selectedOnly: boolean;
}

const normalizeTokenLabel = (value: string) =>
  value
    .split(/[-_]/)
    .filter(Boolean)
    .map(part => part.charAt(0).toUpperCase() + part.slice(1))
    .join(" ");

export const getPermissionResourceLabel = (resource: string) =>
  resourceLabels[resource] ?? normalizeTokenLabel(resource) ?? "其他";

export const getPermissionActionLabel = (action: string) =>
  actionLabels[action] ?? normalizeTokenLabel(action) ?? "操作";

const stripPermissionTypePrefix = (name: string) =>
  name.replace(/^(菜单|页面|按钮|接口)-/, "").trim();

const buildDisplayItem = (
  permission: AuthPermission,
  selected: boolean
): PermissionEditorItem => ({
  ...permission,
  primaryLabel:
    permission.permissionType === 1 || permission.permissionType === 2
      ? getPermissionActionLabel(permission.action)
      : stripPermissionTypePrefix(permission.name) ||
        getPermissionActionLabel(permission.action),
  secondaryLabel: permission.code,
  selected
});

export const normalizePermissionCodes = (codes: readonly string[]) => {
  const seen = new Set<string>();
  const normalized: string[] = [];
  codes.forEach(value => {
    const code = value.trim();
    if (!code || seen.has(code)) return;
    seen.add(code);
    normalized.push(code);
  });
  return normalized;
};

export const replacePermissionGroupSelection = (
  currentCodes: readonly string[],
  groupCodes: readonly string[],
  selected: boolean
) => {
  const normalizedCurrent = normalizePermissionCodes(currentCodes);
  const normalizedGroup = normalizePermissionCodes(groupCodes);
  const groupSet = new Set(normalizedGroup);

  if (!selected) {
    return normalizedCurrent.filter(code => !groupSet.has(code));
  }

  return normalizePermissionCodes([...normalizedCurrent, ...normalizedGroup]);
};

export const buildPermissionEditorView = ({
  permissions,
  selectedCodes,
  activeType,
  keyword,
  selectedOnly
}: BuildPermissionEditorViewOptions) => {
  const selectedSet = new Set(normalizePermissionCodes(selectedCodes));
  const types: PermissionTypeSummary[] = permissionTypeDefinitions.map(type => {
    const items = permissions.filter(
      permission => permission.permissionType === type.value
    );
    return {
      ...type,
      selectedCount: items.filter(item => selectedSet.has(item.code)).length,
      totalCount: items.length
    };
  });

  const normalizedKeyword = keyword.trim().toLocaleLowerCase("zh-CN");
  const activePermissions = permissions.filter(
    permission => permission.permissionType === activeType
  );
  const grouped = new Map<string, AuthPermission[]>();
  activePermissions.forEach(permission => {
    const resource = permission.resource.trim() || "other";
    const items = grouped.get(resource) ?? [];
    items.push(permission);
    grouped.set(resource, items);
  });

  const groups: PermissionResourceGroup[] = [...grouped.entries()]
    .map(([resource, allItems]) => {
      const label = getPermissionResourceLabel(resource);
      const items = allItems
        .map(item => buildDisplayItem(item, selectedSet.has(item.code)))
        .filter(item => {
          if (selectedOnly && !item.selected) return false;
          if (!normalizedKeyword) return true;
          return [
            item.name,
            item.code,
            item.resource,
            item.action,
            item.primaryLabel,
            label
          ].some(value =>
            value.toLocaleLowerCase("zh-CN").includes(normalizedKeyword)
          );
        })
        .sort((left, right) =>
          left.primaryLabel.localeCompare(right.primaryLabel, "zh-CN")
        );

      return {
        resource,
        label,
        selectedCount: allItems.filter(item => selectedSet.has(item.code))
          .length,
        totalCount: allItems.length,
        codes: allItems.map(item => item.code),
        items
      };
    })
    .filter(group => group.items.length > 0)
    .sort((left, right) => left.label.localeCompare(right.label, "zh-CN"));

  return { types, groups };
};
