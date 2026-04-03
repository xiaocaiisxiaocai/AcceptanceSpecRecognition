export type TargetFileLike = {
  name: string;
  size: number;
  lastModified: number;
};

export type TargetUploadItem = {
  id: string;
  file: File;
};

type TargetUploadDecisionParams = {
  hasSourceFile: boolean;
  accept: string;
  existingSignatures: string[];
  file: TargetFileLike;
};

type TargetUploadRejected = {
  status: "rejected";
  message: string;
  level: "warning" | "error";
};

type TargetUploadAccepted = {
  status: "accepted";
  item: {
    id: string;
    file: TargetFileLike;
  };
};

export type TargetUploadDecision =
  | TargetUploadRejected
  | TargetUploadAccepted;

export function createTargetFileSignature(file: TargetFileLike) {
  return `${file.name}-${file.size}-${file.lastModified}`;
}

export function decideTargetUpload(
  params: TargetUploadDecisionParams
): TargetUploadDecision {
  const { hasSourceFile, accept, existingSignatures, file } = params;

  if (!hasSourceFile) {
    return {
      status: "rejected",
      message: "请先上传来源文件",
      level: "warning"
    };
  }

  const lowerName = file.name.toLowerCase();
  const normalizedAccept = accept.trim().toLowerCase();
  if (!lowerName.endsWith(normalizedAccept)) {
    return {
      status: "rejected",
      message: `目标文件仅支持 ${accept} 格式`,
      level: "error"
    };
  }

  if (file.size > 50 * 1024 * 1024) {
    return {
      status: "rejected",
      message: "文件大小不能超过50MB",
      level: "error"
    };
  }

  const signature = createTargetFileSignature(file);
  if (existingSignatures.includes(signature)) {
    return {
      status: "rejected",
      message: `${file.name} 已在列表中`,
      level: "warning"
    };
  }

  return {
    status: "accepted",
    item: {
      id: signature,
      file
    }
  };
}
