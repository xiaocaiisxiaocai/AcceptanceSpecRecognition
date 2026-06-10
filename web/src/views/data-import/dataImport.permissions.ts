import { computed, type ComputedRef } from "vue";

export const useDataImportPermissions = ({
  isExcelFile,
  hasPermission
}: {
  isExcelFile: ComputedRef<boolean>;
  hasPermission: (code: string) => boolean;
}) => {
  const canUploadSourceFile = computed(() =>
    hasPermission("btn:document:upload")
  );
  const canImportWord = computed(() => hasPermission("btn:document:import"));
  const canImportExcel = computed(() =>
    hasPermission("btn:excel-document:import")
  );
  const canImportAny = computed(
    () => canImportWord.value || canImportExcel.value
  );
  const canImportCurrentFile = computed(() =>
    isExcelFile.value ? canImportExcel.value : canImportWord.value
  );
  const currentImportPermissionCode = computed(() =>
    isExcelFile.value ? "btn:excel-document:import" : "btn:document:import"
  );
  const currentImportPermissionMessage = computed(() =>
    isExcelFile.value
      ? "权限不足，无法导入 Excel 数据"
      : "权限不足，无法导入 Word 数据"
  );
  const uploadAccept = computed(() => {
    if (canImportWord.value && canImportExcel.value) return ".docx,.xlsx";
    if (canImportWord.value) return ".docx";
    if (canImportExcel.value) return ".xlsx";
    return "";
  });
  const uploadBlockedMessage = computed(() =>
    canUploadSourceFile.value
      ? "当前账号没有数据导入权限"
      : "当前账号没有文档上传权限"
  );

  return {
    canUploadSourceFile,
    canImportWord,
    canImportExcel,
    canImportAny,
    canImportCurrentFile,
    currentImportPermissionCode,
    currentImportPermissionMessage,
    uploadAccept,
    uploadBlockedMessage
  };
};
