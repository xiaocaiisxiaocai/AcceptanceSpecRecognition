import { http } from "@/utils/http";

/** 统一API响应类型 */
export interface ApiResponse<T = any> {
  code: number;
  message: string;
  data: T;
}

/** 分页数据类型 */
export interface PagedData<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNext: boolean;
  hasPrevious: boolean;
}

/** 分页请求参数 */
export interface PagedRequest {
  page?: number;
  pageSize?: number;
  keyword?: string;
}

/** 客户类型 */
export interface Customer {
  id: number;
  name: string;
  createdAt: string;
  processCount: number;
}

/** 创建客户请求 */
export interface CreateCustomerRequest {
  name: string;
}

/** 更新客户请求 */
export interface UpdateCustomerRequest {
  name: string;
}

/** 制程类型 */
const baseUrl = "/api/customers";

/** 获取客户列表 */
export const getCustomerList = (params?: PagedRequest) => {
  return http.request<ApiResponse<PagedData<Customer>>>("get", baseUrl, {
    params
  });
};

/** 获取客户详情 */
export const getCustomer = (id: number) => {
  return http.request<ApiResponse<Customer>>("get", `${baseUrl}/${id}`);
};

/** 创建客户 */
export const createCustomer = (data: CreateCustomerRequest) => {
  return http.request<ApiResponse<Customer>>("post", baseUrl, { data });
};

/** 更新客户 */
export const updateCustomer = (id: number, data: UpdateCustomerRequest) => {
  return http.request<ApiResponse<Customer>>("put", `${baseUrl}/${id}`, {
    data
  });
};

/** 删除客户 */
export const deleteCustomer = (id: number) => {
  return http.request<ApiResponse<void>>("delete", `${baseUrl}/${id}`);
};

/** 获取客户的制程列表 */
export const getCustomerProcesses = (customerId: number) => {
  // 注意：该接口语义为“该客户的验规中使用过的制程列表”，并非 Customer→Process 从属关系
  return http.request<ApiResponse<{ id: number; name: string; createdAt: string; specCount: number }[]>>(
    "get",
    `${baseUrl}/${customerId}/processes`
  );
};
