import { useState, useCallback, useRef } from 'react';
import { Card, Form, Input, Button, Space, Alert, Spin, Empty, message, Table, Tag, Typography, Tooltip, Switch } from 'antd';
import { SearchOutlined, ClockCircleOutlined, ThunderboltOutlined, RobotOutlined, ClearOutlined, BulbOutlined, StopOutlined } from '@ant-design/icons';
import { useMutation } from '@tanstack/react-query';
import { matchApi } from '../services/api';
import { MatchResultCard, ConfidenceBadge, ThinkingProcess } from '../components';
import type { MatchQuery, MatchResult, BatchResult, StreamingMatchState, ThinkingStep } from '../types';

const { TextArea } = Input;
const { Text } = Typography;

// 初始流式状态
const initialStreamingState: StreamingMatchState = {
  status: 'idle',
  thinkingSteps: [],
};

export const MatchPage: React.FC = () => {
  const [form] = Form.useForm();
  const [results, setResults] = useState<BatchResult | null>(null);
  const [confirmedIds, setConfirmedIds] = useState<Set<string>>(new Set());
  const [rejectedIds, setRejectedIds] = useState<Set<string>>(new Set());

  // 流式匹配相关状态
  const [enableStreaming, setEnableStreaming] = useState(true);
  const [streamingState, setStreamingState] = useState<StreamingMatchState>(initialStreamingState);
  const abortControllerRef = useRef<{ abort: () => void } | null>(null);

  // 批量匹配（支持单条和多条）- 同步模式
  const matchMutation = useMutation({
    mutationFn: (queries: MatchQuery[]) => matchApi.matchBatch({ queries }),
    onSuccess: (data) => {
      setResults(data);
      setConfirmedIds(new Set());
      setRejectedIds(new Set());
    },
    onError: (error: Error) => {
      message.error(`匹配失败: ${error.message}`);
    },
  });

  // 流式匹配处理
  const handleStreamingMatch = useCallback((query: MatchQuery) => {
    // 重置状态
    setStreamingState({
      status: 'preprocessing',
      statusMessage: '正在预处理文本...',
      thinkingSteps: [],
    });
    setResults(null);

    const controller = matchApi.matchStream(query, {
      onStatus: (data) => {
        setStreamingState((prev) => ({
          ...prev,
          status: data.stage === 'thinking' ? 'thinking' : prev.status,
          statusMessage: data.message,
        }));
      },
      onPreprocess: (data) => {
        setStreamingState((prev) => ({
          ...prev,
          status: 'thinking',
          statusMessage: 'AI 正在分析...',
          preprocessResult: data,
        }));
      },
      onThinking: (event) => {
        if (event.thinkingStep) {
          setStreamingState((prev) => ({
            ...prev,
            thinkingSteps: [...prev.thinkingSteps, event.thinkingStep as ThinkingStep],
            currentStep: event.thinkingStep,
          }));
        }
      },
      onResult: (result) => {
        setStreamingState((prev) => ({
          ...prev,
          result,
          currentStep: undefined,
        }));
        // 同时设置到 results 中以复用现有的结果展示逻辑
        setResults({
          taskId: 'streaming',
          results: [result],
          summary: {
            totalCount: 1,
            successCount: result.confidence === 'Success' ? 1 : 0,
            lowConfidenceCount: result.confidence === 'Low' ? 1 : 0,
          },
        });
      },
      onError: (error) => {
        setStreamingState((prev) => ({
          ...prev,
          status: 'error',
          error,
          currentStep: undefined,
        }));
        message.error(`匹配失败: ${error}`);
      },
      onDone: (data) => {
        setStreamingState((prev) => ({
          ...prev,
          status: 'done',
          durationMs: data.durationMs,
          currentStep: undefined,
        }));
        abortControllerRef.current = null;
      },
    });

    abortControllerRef.current = controller;
  }, []);

  // 取消流式请求
  const handleCancelStream = useCallback(() => {
    abortControllerRef.current?.abort();
    abortControllerRef.current = null;
    setStreamingState((prev) => ({
      ...prev,
      status: 'idle',
      statusMessage: '已取消',
      currentStep: undefined,
    }));
    message.info('已取消匹配请求');
  }, []);

  const handleSubmit = (values: { batchInput: string }) => {
    const lines = values.batchInput.trim().split('\n').filter(line => line.trim());
    if (lines.length === 0) {
      message.warning('请输入至少一条数据');
      return;
    }

    const queries: MatchQuery[] = lines.map(line => {
      const parts = line.split('|').map(p => p.trim());
      if (parts.length >= 2) {
        return { project: parts[0], technicalSpec: parts[1] };
      }
      // 如果没有分隔符，使用默认项目名
      return { project: '默认项目', technicalSpec: line.trim() };
    });

    // 流式模式只支持单条
    if (enableStreaming && queries.length === 1) {
      handleStreamingMatch(queries[0]);
    } else {
      // 多条使用同步批量匹配
      if (enableStreaming && queries.length > 1) {
        message.info('批量匹配暂不支持流式模式，已切换为同步模式');
      }
      setStreamingState(initialStreamingState); // 重置流式状态
      matchMutation.mutate(queries);
    }
  };

  const handleConfirm = async (recordId: string) => {
    try {
      await matchApi.confirmMatch(recordId, true);
      setConfirmedIds(prev => new Set(prev).add(recordId));
      message.success('已确认匹配结果');
    } catch (err) {
      console.error('确认失败:', err);
      message.error('确认失败');
    }
  };

  const handleReject = async (recordId: string) => {
    try {
      await matchApi.confirmMatch(recordId, false);
      setRejectedIds(prev => new Set(prev).add(recordId));
      message.info('已拒绝匹配结果');
    } catch (err) {
      console.error('拒绝失败:', err);
      message.error('操作失败');
    }
  };

  // 判断是否为单条结果
  const isSingleResult = results && results.results.length === 1;
  const singleResult = isSingleResult ? results.results[0] : null;

  // 判断是否正在加载
  const isLoading = matchMutation.isPending ||
    streamingState.status === 'preprocessing' ||
    streamingState.status === 'thinking';

  // 清除输入
  const handleClear = () => {
    form.resetFields();
    setResults(null);
    setConfirmedIds(new Set());
    setRejectedIds(new Set());
    setStreamingState(initialStreamingState);
    abortControllerRef.current?.abort();
    abortControllerRef.current = null;
  };

  // 填充示例数据
  const handleFillExample = () => {
    form.setFieldsValue({
      batchInput: `电气控制系统|DC24V 输入模块 16点
电气控制系统|AC220V 输出模块 8点
PLC控制系统|三相异步电机 5.5KW`
    });
  };

  // 批量结果表格列
  const batchColumns = [
    {
      title: '项目',
      dataIndex: ['query', 'project'],
      key: 'project',
      width: 120,
    },
    {
      title: '技术指标',
      dataIndex: ['query', 'technicalSpec'],
      key: 'technicalSpec',
      width: 200,
    },
    {
      title: '匹配结果',
      key: 'match',
      width: 200,
      render: (_: unknown, record: MatchResult) => {
        if (!record.bestMatch) {
          return <Text type="secondary">无匹配</Text>;
        }
        return (
          <Space direction="vertical" size={0}>
            <Text ellipsis style={{ maxWidth: 180 }}>{record.bestMatch.record.actualSpec}</Text>
            <Text type="secondary" style={{ fontSize: 12 }}>
              {record.bestMatch.record.technicalSpec}
            </Text>
          </Space>
        );
      },
    },
    {
      title: '相似度',
      key: 'score',
      width: 100,
      render: (_: unknown, record: MatchResult) => (
        <span>{(record.similarityScore * 100).toFixed(1)}%</span>
      ),
    },
    {
      title: '状态',
      key: 'confidence',
      width: 100,
      render: (_: unknown, record: MatchResult) => (
        <ConfidenceBadge confidence={record.confidence} />
      ),
    },
    {
      title: '模式',
      key: 'matchMode',
      width: 120,
      render: (_: unknown, record: MatchResult) => (
        <Tag
          icon={record.matchMode === 'LLM+Embedding' ? <RobotOutlined /> : <ThunderboltOutlined />}
          color={record.matchMode === 'LLM+Embedding' ? 'purple' : 'blue'}
        >
          {record.matchMode}
        </Tag>
      ),
    },
    {
      title: '耗时',
      key: 'duration',
      width: 80,
      render: (_: unknown, record: MatchResult) => (
        <Text type="secondary">{record.durationMs}ms</Text>
      ),
    },
    {
      title: '操作',
      key: 'action',
      width: 150,
      render: (_: unknown, record: MatchResult) => {
        if (!record.bestMatch) return null;
        const recordId = record.bestMatch.record.id;
        const isConfirmed = confirmedIds.has(recordId);
        const isRejected = rejectedIds.has(recordId);

        if (isConfirmed) {
          return <Tag color="green">已确认</Tag>;
        }
        if (isRejected) {
          return <Tag color="red">已拒绝</Tag>;
        }

        return (
          <Space size="small">
            <Button size="small" type="primary" onClick={() => handleConfirm(recordId)}>
              确认
            </Button>
            <Button size="small" danger onClick={() => handleReject(recordId)}>
              拒绝
            </Button>
          </Space>
        );
      },
    },
  ];

  return (
    <div style={{ padding: 24, height: '100%', display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
      <Card
        title="验收规范匹配"
        style={{ marginBottom: 16, flexShrink: 0 }}
      >
        <Form
          form={form}
          layout="vertical"
          onFinish={handleSubmit}
        >
          <Form.Item
            name="batchInput"
            label={
              <Space>
                <span>输入数据</span>
                <Text type="secondary" style={{ fontSize: 12, fontWeight: 'normal' }}>
                  格式：项目名称|技术指标（每行一条，支持单条或批量输入）
                </Text>
              </Space>
            }
            rules={[{ required: true, message: '请输入匹配数据' }]}
          >
            <TextArea
              rows={4}
              placeholder={`电气控制系统|DC24V 输入模块 16点\n电气控制系统|AC220V 输出模块 8点\nPLC控制系统|三相异步电机 5.5KW`}
            />
          </Form.Item>

          <Form.Item style={{ marginBottom: 0 }}>
            <Space wrap>
              <Tooltip title="启用后单条查询将显示 AI 实时思考过程">
                <Space>
                  <Switch
                    checked={enableStreaming}
                    onChange={setEnableStreaming}
                    checkedChildren="流式"
                    unCheckedChildren="同步"
                  />
                  <Text type="secondary" style={{ fontSize: 12 }}>
                    {enableStreaming ? '显示 AI 思考过程' : '快速返回结果'}
                  </Text>
                </Space>
              </Tooltip>
              <Button
                type="primary"
                htmlType="submit"
                icon={<SearchOutlined />}
                loading={isLoading}
              >
                开始匹配
              </Button>
              {isLoading && enableStreaming && (
                <Button
                  icon={<StopOutlined />}
                  danger
                  onClick={handleCancelStream}
                >
                  取消
                </Button>
              )}
              <Button
                icon={<ClearOutlined />}
                onClick={handleClear}
              >
                清空
              </Button>
              <Button
                icon={<BulbOutlined />}
                onClick={handleFillExample}
              >
                示例数据
              </Button>
            </Space>
          </Form.Item>
        </Form>
      </Card>

      <div style={{ flex: 1, overflow: 'auto' }}>
        {/* 流式思考过程展示 */}
        {enableStreaming && (streamingState.thinkingSteps.length > 0 || streamingState.status === 'thinking' || streamingState.status === 'preprocessing') && (
          <ThinkingProcess
            steps={streamingState.thinkingSteps}
            currentStep={streamingState.currentStep}
            isLoading={streamingState.status === 'thinking' || streamingState.status === 'preprocessing'}
            statusMessage={streamingState.statusMessage}
          />
        )}

        {/* 同步模式加载状态 */}
        {matchMutation.isPending && !enableStreaming && (
          <div style={{ textAlign: 'center', padding: 48 }}>
            <Spin size="large" tip="正在匹配中..." />
          </div>
        )}

        {/* 错误状态 */}
        {streamingState.status === 'error' && (
          <Alert
            type="error"
            message="匹配失败"
            description={streamingState.error}
            style={{ marginBottom: 16 }}
          />
        )}

        {results && !matchMutation.isPending && streamingState.status !== 'preprocessing' && streamingState.status !== 'thinking' && (
          <>
            {/* 单条结果展示 */}
            {isSingleResult && singleResult && (
              <Card
                title={
                  <Space>
                    <span>匹配结果</span>
                    <ConfidenceBadge confidence={singleResult.confidence} score={singleResult.similarityScore} />
                    {singleResult.isLowConfidence && (
                      <Alert
                        type="warning"
                        message="置信度较低，请人工复核"
                        style={{ display: 'inline-block', padding: '2px 8px' }}
                      />
                    )}
                  </Space>
                }
                extra={
                  <Space>
                    <Tooltip title={`匹配模式: ${singleResult.matchMode}`}>
                      <Tag
                        icon={singleResult.matchMode === 'LLM+Embedding' ? <RobotOutlined /> : <ThunderboltOutlined />}
                        color={singleResult.matchMode === 'LLM+Embedding' ? 'purple' : 'blue'}
                      >
                        {singleResult.matchMode}
                      </Tag>
                    </Tooltip>
                    <Tooltip title="匹配耗时">
                      <Tag icon={<ClockCircleOutlined />}>
                        {streamingState.durationMs || singleResult.durationMs}ms
                      </Tag>
                    </Tooltip>
                  </Space>
                }
              >
                {!singleResult.bestMatch ? (
                  <Empty description="未找到匹配结果" />
                ) : rejectedIds.has(singleResult.bestMatch.record.id) ? (
                  <Empty description="匹配结果已被拒绝" />
                ) : (
                  <MatchResultCard
                    candidate={singleResult.bestMatch}
                    confidence={singleResult.confidence}
                    onConfirm={confirmedIds.has(singleResult.bestMatch.record.id) ? undefined : handleConfirm}
                    onReject={confirmedIds.has(singleResult.bestMatch.record.id) ? undefined : handleReject}
                  />
                )}

                {singleResult.llmAnalysis && (
                  <Alert
                    type="info"
                    message="AI分析建议"
                    description={singleResult.llmAnalysis.reasoning}
                    style={{ marginTop: 16 }}
                  />
                )}
              </Card>
            )}

            {/* 批量结果展示 */}
            {!isSingleResult && (
              <Card
                title={
                  <Space>
                    <span>匹配结果</span>
                    <Tag color="blue">共 {results.summary.totalCount} 条</Tag>
                    <Tag color="green">成功 {results.summary.successCount} 条</Tag>
                    <Tag color="red">低置信度 {results.summary.lowConfidenceCount} 条</Tag>
                  </Space>
                }
              >
                <Table
                  dataSource={results.results}
                  columns={batchColumns}
                  rowKey={(record) => `${record.query.project}-${record.query.technicalSpec}`}
                  pagination={{ pageSize: 10 }}
                  size="small"
                  scroll={{ x: 1100 }}
                  rowClassName={(record) => record.isLowConfidence ? 'low-confidence-row' : ''}
                />
              </Card>
            )}
          </>
        )}
      </div>

      <style>{`
        .low-confidence-row {
          background-color: #fff2f0;
        }
        .low-confidence-row:hover > td {
          background-color: #ffebe8 !important;
        }
      `}</style>
    </div>
  );
};

export default MatchPage;
