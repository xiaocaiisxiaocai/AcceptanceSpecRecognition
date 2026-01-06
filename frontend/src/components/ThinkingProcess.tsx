import React from 'react';
import { Card, Timeline, Typography, Tag, Spin, Collapse, Space, Empty } from 'antd';
import {
  BulbOutlined,
  SwapOutlined,
  WarningOutlined,
  CalculatorOutlined,
  CheckCircleOutlined,
  LoadingOutlined,
  RobotOutlined,
  LinkOutlined,
} from '@ant-design/icons';
import type { ThinkingStep, ThinkingStepType } from '../types';

const { Text } = Typography;

interface ThinkingProcessProps {
  /** 已完成的思考步骤列表 */
  steps: ThinkingStep[];
  /** 当前正在进行的步骤 */
  currentStep?: ThinkingStep;
  /** 是否正在加载 */
  isLoading: boolean;
  /** 状态消息 */
  statusMessage?: string;
}

// 步骤配置：图标和颜色
const stepConfig: Record<ThinkingStepType, { icon: React.ReactNode; color: string; label: string }> = {
  extract: { icon: <BulbOutlined />, color: 'blue', label: '属性提取' },
  equivalence: { icon: <LinkOutlined />, color: 'geekblue', label: '语义等价分析' },
  compare: { icon: <SwapOutlined />, color: 'cyan', label: '语义对比' },
  conflict: { icon: <WarningOutlined />, color: 'orange', label: '冲突检测' },
  confidence: { icon: <CalculatorOutlined />, color: 'purple', label: '置信度推理' },
  conclusion: { icon: <CheckCircleOutlined />, color: 'green', label: '最终结论' },
};

/**
 * 思考过程展示组件
 * 使用 Timeline 展示 LLM 的分析步骤
 */
export const ThinkingProcess: React.FC<ThinkingProcessProps> = ({
  steps,
  currentStep,
  isLoading,
  statusMessage,
}) => {
  // 格式化时间戳
  const formatTime = (timestamp: string) => {
    try {
      return new Date(timestamp).toLocaleTimeString('zh-CN', {
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit',
      });
    } catch {
      return '';
    }
  };

  // 渲染步骤内容
  const renderStepContent = (content: Record<string, unknown>) => {
    return (
      <pre
        style={{
          fontSize: 12,
          background: '#f5f5f5',
          padding: 12,
          borderRadius: 6,
          maxHeight: 200,
          overflow: 'auto',
          margin: 0,
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-all',
        }}
      >
        {JSON.stringify(content, null, 2)}
      </pre>
    );
  };

  // 构建 Timeline items
  const timelineItems = steps.map((step) => {
    const config = stepConfig[step.step] || {
      icon: <BulbOutlined />,
      color: 'default',
      label: step.title,
    };
    const isCurrent = currentStep?.step === step.step;

    return {
      key: `${step.step}-${step.timestamp}`,
      dot: isCurrent ? (
        <LoadingOutlined style={{ fontSize: 16 }} />
      ) : (
        <span style={{ fontSize: 16 }}>{config.icon}</span>
      ),
      color: config.color,
      children: (
        <div>
          <div style={{ marginBottom: 8 }}>
            <Tag color={config.color}>{step.title || config.label}</Tag>
            <Text type="secondary" style={{ fontSize: 12, marginLeft: 8 }}>
              {formatTime(step.timestamp)}
            </Text>
          </div>
          <Collapse
            ghost
            size="small"
            items={[
              {
                key: '1',
                label: <Text type="secondary">查看详情</Text>,
                children: renderStepContent(step.content),
              },
            ]}
          />
        </div>
      ),
    };
  });

  // 如果正在加载且有当前步骤但不在列表中，添加加载中的步骤
  if (isLoading && currentStep && !steps.find((s) => s.step === currentStep.step)) {
    const config = stepConfig[currentStep.step] || {
      icon: <BulbOutlined />,
      color: 'processing',
      label: currentStep.title,
    };
    timelineItems.push({
      key: 'loading',
      dot: <LoadingOutlined style={{ fontSize: 16 }} />,
      color: 'processing' as unknown as string,
      children: (
        <div>
          <Tag color="processing">{currentStep.title || config.label}</Tag>
          <Text type="secondary" style={{ marginLeft: 8 }}>
            分析中...
          </Text>
        </div>
      ),
    });
  }

  return (
    <Card
      title={
        <Space>
          <RobotOutlined />
          <span>AI 思考过程</span>
          {isLoading && <Spin indicator={<LoadingOutlined spin />} size="small" />}
        </Space>
      }
      extra={
        statusMessage && (
          <Text type="secondary" style={{ fontSize: 12 }}>
            {statusMessage}
          </Text>
        )
      }
      style={{ marginBottom: 16 }}
      styles={{
        body: {
          maxHeight: 400,
          overflow: 'auto',
        },
      }}
    >
      {steps.length === 0 && !isLoading ? (
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description="暂无思考过程"
          style={{ margin: '20px 0' }}
        />
      ) : (
        <Timeline items={timelineItems} />
      )}
    </Card>
  );
};

export default ThinkingProcess;
