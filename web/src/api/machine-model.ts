import { http } from "@/utils/http";
import type { ApiResponse, PagedData, PagedRequest } from "./customer";

/** 机型类型 */
export interface MachineModel {
  id: number;
  name: string;
  createdAt: string;
  specCount: number;
}

/** 创建机型请求 */
export interface CreateMachineModelRequest {
  name: string;
}

/** 更新机型请求 */
export interface UpdateMachineModelRequest {
  name: string;
}

/** 机型列表请求参数 */
export interface MachineModelListRequest extends PagedRequest {
}

const baseUrl = "/api/machine-models";

/** 获取机型列表 */
export const getMachineModelList = (params?: MachineModelListRequest) => {
  return http.request<ApiResponse<PagedData<MachineModel>>>("get", baseUrl, {
    params
  });
};

/** 获取机型详情 */
export const getMachineModel = (id: number) => {
  return http.request<ApiResponse<MachineModel>>("get", `${baseUrl}/${id}`);
};

/** 创建机型 */
export const createMachineModel = (data: CreateMachineModelRequest) => {
  return http.request<ApiResponse<MachineModel>>("post", baseUrl, { data });
};

/** 更新机型 */
export const updateMachineModel = (id: number, data: UpdateMachineModelRequest) => {
  return http.request<ApiResponse<MachineModel>>("put", `${baseUrl}/${id}`, {
    data
  });
};

/** 删除机型 */
export const deleteMachineModel = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};
