import { useState } from 'react';
import { Card, Table, DatePicker, Select, Space, Button, Modal, message, Pagination, Tag, Typography, Tooltip } from 'antd';
import { DeleteOutlined, ExclamationCircleOutlined, DownloadOutlined, ReloadOutlined, EyeOutlined } from '@ant-design/icons';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { auditApi } from '../services/api';
import type { AuditLogEntry } from '../types';
import dayjs from 'dayjs';

const { RangePicker } = DatePicker;
const { Text, Paragraph } = Typography;

const actionTypeOptions = [
  { value: '', label: '全部' },
  { value: 'query', label: '查询' },
  { value: 'confirm_match', label: '确认匹配' },
  { value: 'reject_match', label: '拒绝匹配' },
  { value: 'config_change', label: '配置修改' },
  { value: 'create_history', label: '创建记录' },
  { value: 'update_history', label: '更新记录' },
];

// 操作类型对应的颜色
const actionTypeColors: Record<string, string> = {
  query: 'blue',
  confirm_match: 'green',
  reject_match: 'red',
  config_change: 'orange',
  create_history: 'cyan',
  update_history: 'purple',
};

export const AuditPage: React.FC = () => {
  const [dateRange, setDateRange] = useState<[dayjs.Dayjs | null, dayjs.Dayjs | null]>([null, null]);
  const [actionType, setActionType] = useState('');
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(20);
  const [detailModalOpen, setDetailModalOpen] = useState(false);
  const [selectedLog, setSelectedLog] = useState<AuditLogEntry | null>(null);
  const queryClient = useQueryClient();

  const { data: logs, isLoading, refetch } = useQuery({
    queryKey: ['audit', dateRange, actionType, page, pageSize],
    queryFn: () =>
      auditApi.query({
        startTime: dateRange[0]?.toISOString(),
        endTime: dateRange[1]?.toISOString(),
        actionType: actionType || undefined,
        page,
        pageSize,
      }),
    refetchOnMount: 'always',
    staleTime: 0,
  });

  // 清除审计日志
  const clearMutation = useMutation({
    mutationFn: () => auditApi.clear(),
    onSuccess: () => {
      message.success('审计日志已清除');
      queryClient.invalidateQueries({ queryKey: ['audit'] });
      setPage(1);
    },
    onError: () => {
      message.error('清除失败');
    },
  });

  // 确认清除对话框
  const handleClear = () => {
    Modal.confirm({
      title: '确认清除',
      icon: <ExclamationCircleOutlined />,
      content: '确定要清除所有审计日志吗？此操作不可恢复。',
      okText: '确认',
      cancelText: '取消',
      okButtonProps: { danger: true },
      onOk: () => clearMutation.mutate(),
    });
  };

  // 查看详情
  const handleViewDetail = (record: AuditLogEntry) => {
    setSelectedLog(record);
    setDetailModalOpen(true);
  };

  // 导出为 CSV
  const handleExport = () => {
    if (!logs?.entries || logs.entries.length === 0) {
      message.warning('没有可导出的数据');
      return;
    }

    const headers = ['时间', '操作类型', '详情', '记录ID'];
    const csvContent = [
      headers.join(','),
      ...logs.entries.map(entry => [
        `"${new Date(entry.timestamp).toLocaleString('zh-CN')}"`,
        `"${actionTypeOptions.find(o => o.value === entry.actionType)?.label || entry.actionType}"`,
        `"${(entry.details || '').replace(/"/g, '""')}"`,
        `"${entry.recordId || ''}"`,
      ].join(','))
    ].join('\n');

    const blob = new Blob(['\ufeff' + csvContent], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `审计日志_${dayjs().format('YYYY-MM-DD_HHmmss')}.csv`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
    message.success('导出成功');
  };

  // 解析详情中的置信度信息
  const parseConfidence = (details: string | undefined): 'Success' | 'Low' | null => {
    if (!details) return null;
    if (details.includes('置信度: Success')) return 'Success';
    if (details.includes('置信度: Low')) return 'Low';
    return null;
  };

  const columns = [
    {
      title: '时间',
      dataIndex: 'timestamp',
      key: 'timestamp',
      width: 150,
      render: (date: string) => new Date(date).toLocaleString('zh-CN'),
    },
    {
      title: '操作类型',
      dataIndex: 'actionType',
      key: 'actionType',
      width: 95,
      render: (type: string) => {
        const option = actionTypeOptions.find(o => o.value === type);
        const color = actionTypeColors[type] || 'default';
        return <Tag color={color}>{option?.label || type}</Tag>;
      },
    },
    {
      title: '详情',
      dataIndex: 'details',
      key: 'details',
      ellipsis: true,
      render: (details: string, record: AuditLogEntry) => (
        <Tooltip title={details} placement="topLeft">
          <span
            style={{ cursor: 'pointer' }}
            onClick={() => handleViewDetail(record)}
          >
            {details}
          </span>
        </Tooltip>
      ),
    },
    {
      title: '置信度',
      key: 'confidence',
      width: 90,
      render: (_: unknown, record: AuditLogEntry) => {
        const confidence = parseConfidence(record.details);
        if (!confidence) return null;
        return (
          <Tag color={confidence === 'Success' ? 'green' : 'orange'}>
            {confidence === 'Success' ? '高置信度' : '低置信度'}
          </Tag>
        );
      },
    },
    {
      title: '操作',
      key: 'action',
      width: 100,
      render: (_: unknown, record: AuditLogEntry) => (
        <Button
          type="link"
          size="small"
          icon={<EyeOutlined />}
          onClick={() => handleViewDetail(record)}
        >
          详情
        </Button>
      ),
    },
  ];

  return (
    <div style={{ padding: 24, height: '100%', display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
      <Card
        title="审计日志"
        extra={
          <Space>
            <RangePicker
              value={dateRange}
              onChange={(dates) => setDateRange(dates as [dayjs.Dayjs | null, dayjs.Dayjs | null])}
            />
            <Select
              value={actionType}
              onChange={setActionType}
              options={actionTypeOptions}
              style={{ width: 120 }}
              placeholder="操作类型"
            />
            <Button
              icon={<ReloadOutlined />}
              onClick={() => refetch()}
            >
              刷新
            </Button>
            <Button
              icon={<DownloadOutlined />}
              onClick={handleExport}
            >
              导出
            </Button>
            <Button
              danger
              icon={<DeleteOutlined />}
              onClick={handleClear}
              loading={clearMutation.isPending}
            >
              清除
            </Button>
          </Space>
        }
        style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0, overflow: 'hidden' }}
        styles={{ body: { flex: 1, display: 'flex', flexDirection: 'column', padding: '16px 24px', minHeight: 0, overflow: 'hidden' } }}
      >
        <div style={{ flex: 1, minHeight: 0 }}>
          <style>{`
            .audit-log-table .ant-table-container {
              overflow-x: hidden !important;
              overflow-y: hidden !important;
            }
            .audit-log-table .ant-table-body {
              overflow-x: hidden !important;
            }
          `}</style>
          <Table
            dataSource={logs?.entries}
            columns={columns}
            rowKey="id"
            loading={isLoading}
            pagination={false}
            scroll={{ y: 'calc(100vh - 350px)' }}
            tableLayout="fixed"
            className="audit-log-table"
          />
        </div>
        <div style={{
          borderTop: '1px solid #f0f0f0',
          paddingTop: 16,
          marginTop: 16,
          display: 'flex',
          justifyContent: 'flex-end',
          flexShrink: 0
        }}>
          <Pagination
            current={page}
            pageSize={pageSize}
            total={logs?.totalCount || 0}
            onChange={(p, ps) => {
              setPage(p);
              if (ps !== pageSize) {
                setPageSize(ps);
                setPage(1);
              }
            }}
            showSizeChanger
            showQuickJumper
            showTotal={(total) => `共 ${total} 条`}
            pageSizeOptions={['10', '20', '50', '100']}
          />
        </div>
      </Card>

      {/* 详情弹窗 */}
      <Modal
        title="日志详情"
        open={detailModalOpen}
        onCancel={() => {
          setDetailModalOpen(false);
          setSelectedLog(null);
        }}
        footer={[
          <Button key="close" onClick={() => setDetailModalOpen(false)}>
            关闭
          </Button>
        ]}
        width={700}
      >
        {selectedLog && (
          <div>
            <div style={{ marginBottom: 16 }}>
              <Text strong>时间：</Text>
              <Text>{new Date(selectedLog.timestamp).toLocaleString('zh-CN')}</Text>
            </div>
            <div style={{ marginBottom: 16 }}>
              <Text strong>操作类型：</Text>
              <Tag color={actionTypeColors[selectedLog.actionType] || 'default'}>
                {actionTypeOptions.find(o => o.value === selectedLog.actionType)?.label || selectedLog.actionType}
              </Tag>
              {parseConfidence(selectedLog.details) && (
                <Tag color={parseConfidence(selectedLog.details) === 'Success' ? 'green' : 'orange'}>
                  {parseConfidence(selectedLog.details) === 'Success' ? '高置信度' : '低置信度'}
                </Tag>
              )}
            </div>
            {selectedLog.recordId && (
              <div style={{ marginBottom: 16 }}>
                <Text strong>关联记录ID：</Text>
                <Text code>{selectedLog.recordId}</Text>
              </div>
            )}
            <div>
              <Text strong>详细信息：</Text>
              <Paragraph
                style={{
                  marginTop: 8,
                  padding: 12,
                  background: '#f5f5f5',
                  borderRadius: 4,
                  whiteSpace: 'pre-wrap',
                  wordBreak: 'break-all',
                  maxHeight: 300,
                  overflow: 'auto',
                }}
              >
                {selectedLog.details || '无'}
              </Paragraph>
            </div>
          </div>
        )}
      </Modal>
    </div>
  );
};

export default AuditPage;
