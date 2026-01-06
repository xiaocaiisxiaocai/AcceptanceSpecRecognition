import { Tag } from 'antd';
import type { ConfidenceLevel } from '../types';

interface ConfidenceBadgeProps {
  confidence?: ConfidenceLevel;
  score?: number;
}

const confidenceConfig: Record<ConfidenceLevel, { color: string; text: string }> = {
  Success: { color: 'green', text: '匹配成功' },
  Low: { color: 'red', text: '置信度低' },
};

export const ConfidenceBadge: React.FC<ConfidenceBadgeProps> = ({ confidence, score }) => {
  const config = confidence ? confidenceConfig[confidence] : null;

  if (!config) {
    return <Tag color="default">未知</Tag>;
  }

  return (
    <Tag color={config.color}>
      {config.text}
      {score !== undefined && ` (${(score * 100).toFixed(1)}%)`}
    </Tag>
  );
};

export default ConfidenceBadge;
