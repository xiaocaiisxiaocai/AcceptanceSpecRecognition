import { http } from "@/utils/http";
import type { ApiResponse } from "./customer";

export enum ChineseConversionMode {
  None = 0,
  HansToTW = 1,
  TWToHans = 2
}

export interface TextProcessingConfig {
  id: number;
  enableChineseConversion: boolean;
  conversionMode: ChineseConversionMode;
  enableSynonym: boolean;
  enableOkNgConversion: boolean;
  okStandardFormat: string;
  ngStandardFormat: string;
  enableKeywordHighlight: boolean;
  highlightColorHex: string;
  updatedAt: string;
}

export interface UpdateTextProcessingConfigRequest {
  enableChineseConversion: boolean;
  conversionMode: ChineseConversionMode;
  enableSynonym: boolean;
  enableOkNgConversion: boolean;
  okStandardFormat?: string | null;
  ngStandardFormat?: string | null;
  enableKeywordHighlight: boolean;
  highlightColorHex: string;
}

const baseUrl = "/api/text-processing";

export const getTextProcessingConfig = () => {
  return http.request<ApiResponse<TextProcessingConfig>>(
    "get",
    `${baseUrl}/config`
  );
};

export const saveTextProcessingConfig = (
  data: UpdateTextProcessingConfigRequest
) => {
  return http.request<ApiResponse<TextProcessingConfig>>(
    "put",
    `${baseUrl}/config`,
    { data }
  );
};

export const resetTextProcessingConfig = () => {
  return http.request<ApiResponse<TextProcessingConfig>>(
    "post",
    `${baseUrl}/config/reset`
  );
};

