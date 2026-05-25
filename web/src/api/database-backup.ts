import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";

export interface DatabaseBackupOptions {
  enabled: boolean;
  runAtLocalTime?: string | null;
  backupDirectory: string;
  retentionCount: number;
}

export interface DatabaseBackupStatus {
  isRunning: boolean;
  lastStartedAt?: string | null;
  lastFinishedAt?: string | null;
  lastSucceeded?: boolean | null;
  lastError?: string | null;
  lastFileName?: string | null;
  lastFileSizeBytes?: number | null;
}

export interface DatabaseBackupFile {
  fileName: string;
  sizeBytes: number;
  createdAt: string;
}

export interface DatabaseBackupInfo {
  options: DatabaseBackupOptions;
  status: DatabaseBackupStatus;
  files: DatabaseBackupFile[];
}

const baseUrl = "/api/database-backup";

export const getDatabaseBackupInfo = () => {
  return http.request<ApiResponse<DatabaseBackupInfo>>("get", baseUrl);
};

export const updateDatabaseBackupOptions = (data: DatabaseBackupOptions) => {
  return http.request<ApiResponse<DatabaseBackupInfo>>(
    "put",
    `${baseUrl}/options`,
    { data }
  );
};

export const runDatabaseBackup = () => {
  return http.request<ApiResponse<DatabaseBackupInfo>>(
    "post",
    `${baseUrl}/run`,
    undefined,
    { timeout: 600000 }
  );
};
