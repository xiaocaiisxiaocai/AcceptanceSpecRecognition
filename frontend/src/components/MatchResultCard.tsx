import { Card, Button, Space, Typography, Collapse, Descriptions } from 'antd';
import { CheckOutlined, CloseOutlined, InfoCircleOutlined } from '@ant-design/icons';
import type { MatchCandidate, ConfidenceLevel } from '../types';
import { ConfidenceBadge } from './ConfidenceBadge';
import { HighlightedText } from './HighlightedText';

const { Text } = Typography;

interface MatchResultCardProps {
  candidate: MatchCandidate;
  confidence: ConfidenceLevel;
  onConfirm?: (recordId: string) => void;
  onReject?: (recordId: string) => void;
}

export const MatchResultCard: React.FC<MatchResultCardProps> = ({
  candidate,
  confidence,
  onConfirm,
  onReject,
}) => {
  const { record, similarityScore, highlightedActualSpec, highlightedRemark, explanation } = candidate;

  return (
    <Card
      title={
        <Space>
          <Text>{record.project}</Text>
          <ConfidenceBadge
            confidence={confidence}
            score={similarityScore}
          />
        </Space>
      }
      extra={
        <Space>
          {onConfirm && (
            <Button
              type="primary"
              icon={<CheckOutlined />}
              onClick={() => onConfirm(record.id)}
            >
              确认
            </Button>
          )}
          {onReject && (
            <Button
              danger
              icon={<CloseOutlined />}
              onClick={() => onReject(record.id)}
            >
              拒绝
            </Button>
          )}
        </Space>
      }
      style={{ marginBottom: 16 }}
    >
      <Descriptions column={1} size="small">
        <Descriptions.Item label="技术指标">
          {record.technicalSpec}
        </Descriptions.Item>
        <Descriptions.Item label="实际规格">
          <HighlightedText html={highlightedActualSpec} />
        </Descriptions.Item>
        <Descriptions.Item label="备注">
          <HighlightedText html={highlightedRemark} />
        </Descriptions.Item>
      </Descriptions>

      {explanation && (
        <Collapse
          ghost
          items={[
            {
              key: 'explanation',
              label: (
                <Space>
                  <InfoCircleOutlined />
                  <Text type="secondary">匹配解释</Text>
                </Space>
              ),
              children: (
                <Descriptions column={1} size="small">
                  <Descriptions.Item label="Embedding相似度">
                    {(explanation.embeddingSimilarity * 100).toFixed(2)}%
                  </Descriptions.Item>
                  {explanation.preprocessingSteps.length > 0 && (
                    <Descriptions.Item label="预处理步骤">
                      {explanation.preprocessingSteps.map((step, i) => (
                        <div key={i}>
                          {step.type}: {step.before} → {step.after}
                        </div>
                      ))}
                    </Descriptions.Item>
                  )}
                  {explanation.llmReasoning && (
                    <Descriptions.Item label="LLM分析">
                      {explanation.llmReasoning}
                    </Descriptions.Item>
                  )}
                </Descriptions>
              ),
            },
          ]}
        />
      )}
    </Card>
  );
};

export default MatchResultCard;
