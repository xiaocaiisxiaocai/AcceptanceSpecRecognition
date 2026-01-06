import { useState } from 'react';
import { Card, Table, Input, Button, Space, Modal, Form, message, Popconfirm, DatePicker, Select } from 'antd';
import { PlusOutlined, EditOutlined, SearchOutlined, DeleteOutlined, DownloadOutlined, ExclamationCircleOutlined } from '@ant-design/icons';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { historyApi } from '../services/api';
import type { HistoryRecord } from '../types';
import type { TableProps } from 'antd';
import type { SorterResult } from 'antd/es/table/interface';
import dayjs from 'dayjs';

const { TextArea } = Input;
const { RangePicker } = DatePicker;

export const HistoryPage: React.FC = () => {
  const [search, setSearch] = useState('');
  const [editingRecord, setEditingRecord] = useState<HistoryRecord | null>(null);
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [form] = Form.useForm();
  const [selectedRowKeys, setSelectedRowKeys] = useState<React.Key[]>([]);
  const [sortField, setSortField] = useState<string>('updatedAt');
  const [sortOrder, setSortOrder] = useState<'ascend' | 'descend'>('descend');
  const [projectFilter, setProjectFilter] = useState<string>('');
  const [dateRange, setDateRange] = useState<[dayjs.Dayjs | null, dayjs.Dayjs | null]>([null, null]);
  const queryClient = useQueryClient();

  const { data: records, isLoading } = useQuery({
    queryKey: ['history', search],
    queryFn: () => historyApi.getAll(search || undefined),
  });

  // 获取所有唯一的项目名称用于筛选
  const projectOptions = records
    ? [...new Set(records.map(r => r.project))].map(p => ({ label: p, value: p }))
    : [];

  // 前端过滤和排序
  const filteredRecords = records
    ?.filter(record => {
      // 项目筛选
      if (projectFilter && record.project !== projectFilter) return false;
      // 日期范围筛选
      if (dateRange[0] && dateRange[1]) {
        const recordDate = dayjs(record.updatedAt);
        if (!recordDate.isAfter(dateRange[0].startOf('day')) || !recordDate.isBefore(dateRange[1].endOf('day'))) {
          return false;
        }
      }
      return true;
    })
    ?.sort((a, b) => {
      const aValue = a[sortField as keyof HistoryRecord];
      const bValue = b[sortField as keyof HistoryRecord];
      if (typeof aValue === 'string' && typeof bValue === 'string') {
        return sortOrder === 'ascend' ? aValue.localeCompare(bValue) : bValue.localeCompare(aValue);
      }
      return 0;
    });

  const createMutation = useMutation({
    mutationFn: (record: Partial<HistoryRecord>) => historyApi.create(record),
    onSuccess: () => {
      message.success('创建成功');
      queryClient.invalidateQueries({ queryKey: ['history'] });
      setIsModalOpen(false);
      form.resetFields();
    },
  });

  const updateMutation = useMutation({
    mutationFn: ({ id, record }: { id: string; record: Partial<HistoryRecord> }) =>
      historyApi.update(id, record),
    onSuccess: () => {
      message.success('更新成功');
      queryClient.invalidateQueries({ queryKey: ['history'] });
      setIsModalOpen(false);
      setEditingRecord(null);
      form.resetFields();
    },
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => historyApi.delete(id),
    onSuccess: () => {
      message.success('删除成功');
      queryClient.invalidateQueries({ queryKey: ['history'] });
    },
    onError: () => {
      message.error('删除失败');
    },
  });

  const batchDeleteMutation = useMutation({
    mutationFn: (ids: string[]) => historyApi.deleteBatch(ids),
    onSuccess: () => {
      message.success('批量删除成功');
      setSelectedRowKeys([]);
      queryClient.invalidateQueries({ queryKey: ['history'] });
    },
    onError: () => {
      message.error('批量删除失败');
    },
  });

  const handleEdit = (record: HistoryRecord) => {
    setEditingRecord(record);
    form.setFieldsValue(record);
    setIsModalOpen(true);
  };

  const handleAdd = () => {
    setEditingRecord(null);
    form.resetFields();
    setIsModalOpen(true);
  };

  const handleSubmit = (values: Partial<HistoryRecord>) => {
    if (editingRecord) {
      updateMutation.mutate({ id: editingRecord.id, record: values });
    } else {
      createMutation.mutate(values);
    }
  };

  const handleDelete = (id: string) => {
    deleteMutation.mutate(id);
  };

  const handleBatchDelete = () => {
    Modal.confirm({
      title: '确认批量删除',
      icon: <ExclamationCircleOutlined />,
      content: `确定要删除选中的 ${selectedRowKeys.length} 条记录吗？此操作不可恢复。`,
      okText: '确认删除',
      cancelText: '取消',
      okButtonProps: { danger: true },
      onOk: () => {
        batchDeleteMutation.mutate(selectedRowKeys as string[]);
      },
    });
  };

  // 导出为 CSV
  const handleExport = () => {
    if (!filteredRecords || filteredRecords.length === 0) {
      message.warning('没有可导出的数据');
      return;
    }

    const headers = ['项目', '技术指标', '实际规格', '备注', '更新时间'];
    const csvContent = [
      headers.join(','),
      ...filteredRecords.map(record => [
        `"${record.project || ''}"`,
        `"${record.technicalSpec || ''}"`,
        `"${record.actualSpec || ''}"`,
        `"${record.remark || ''}"`,
        `"${new Date(record.updatedAt).toLocaleString('zh-CN')}"`,
      ].join(','))
    ].join('\n');

    const blob = new Blob(['\ufeff' + csvContent], { type: 'text/csv;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `历史记录_${dayjs().format('YYYY-MM-DD_HHmmss')}.csv`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
    message.success('导出成功');
  };

  // 表格排序变化
  const handleTableChange: TableProps<HistoryRecord>['onChange'] = (
    _pagination,
    _filters,
    sorter
  ) => {
    const s = sorter as SorterResult<HistoryRecord>;
    if (s.field && s.order) {
      setSortField(s.field as string);
      setSortOrder(s.order);
    }
  };

  // 清除筛选
  const handleClearFilters = () => {
    setProjectFilter('');
    setDateRange([null, null]);
    setSearch('');
  };

  const columns = [
    {
      title: '项目',
      dataIndex: 'project',
      key: 'project',
      width: 150,
      sorter: true,
      sortOrder: sortField === 'project' ? sortOrder : undefined,
    },
    {
      title: '技术指标',
      dataIndex: 'technicalSpec',
      key: 'technicalSpec',
      width: 200,
      sorter: true,
      sortOrder: sortField === 'technicalSpec' ? sortOrder : undefined,
    },
    {
      title: '实际规格',
      dataIndex: 'actualSpec',
      key: 'actualSpec',
      width: 200,
      sorter: true,
      sortOrder: sortField === 'actualSpec' ? sortOrder : undefined,
    },
    { title: '备注', dataIndex: 'remark', key: 'remark', width: 150 },
    {
      title: '更新时间',
      dataIndex: 'updatedAt',
      key: 'updatedAt',
      width: 160,
      sorter: true,
      sortOrder: sortField === 'updatedAt' ? sortOrder : undefined,
      render: (date: string) => new Date(date).toLocaleString('zh-CN'),
    },
    {
      title: '操作',
      key: 'action',
      width: 120,
      render: (_: unknown, record: HistoryRecord) => (
        <Space size="small">
          <Button
            type="link"
            size="small"
            icon={<EditOutlined />}
            onClick={() => handleEdit(record)}
          >
            编辑
          </Button>
          <Popconfirm
            title="确认删除"
            description="确定要删除这条记录吗？"
            onConfirm={() => handleDelete(record.id)}
            okText="确认"
            cancelText="取消"
          >
            <Button
              type="link"
              size="small"
              danger
              icon={<DeleteOutlined />}
            >
              删除
            </Button>
          </Popconfirm>
        </Space>
      ),
    },
  ];

  const rowSelection = {
    selectedRowKeys,
    onChange: (keys: React.Key[]) => setSelectedRowKeys(keys),
  };

  return (
    <div style={{ padding: '16px 24px', height: '100%', display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
      <Card
        title="历史记录管理"
        extra={
          <Space wrap>
            <Input
              placeholder="搜索..."
              prefix={<SearchOutlined />}
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              style={{ width: 150 }}
              allowClear
            />
            <Select
              placeholder="按项目筛选"
              value={projectFilter || undefined}
              onChange={setProjectFilter}
              options={projectOptions}
              style={{ width: 140 }}
              allowClear
            />
            <RangePicker
              value={dateRange}
              onChange={(dates) => setDateRange(dates as [dayjs.Dayjs | null, dayjs.Dayjs | null])}
              placeholder={['开始日期', '结束日期']}
              style={{ width: 220 }}
            />
            {(projectFilter || dateRange[0] || search) && (
              <Button onClick={handleClearFilters}>清除筛选</Button>
            )}
            <Button icon={<DownloadOutlined />} onClick={handleExport}>
              导出
            </Button>
            <Button type="primary" icon={<PlusOutlined />} onClick={handleAdd}>
              新增记录
            </Button>
          </Space>
        }
        style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0, overflow: 'hidden' }}
        styles={{ body: { flex: 1, display: 'flex', flexDirection: 'column', padding: '8px 16px', minHeight: 0, overflow: 'hidden' } }}
      >
        {selectedRowKeys.length > 0 && (
          <div style={{ marginBottom: 16, padding: '8px 16px', background: '#e6f7ff', borderRadius: 4 }}>
            <Space>
              <span>已选择 {selectedRowKeys.length} 项</span>
              <Button
                type="primary"
                danger
                size="small"
                icon={<DeleteOutlined />}
                onClick={handleBatchDelete}
                loading={batchDeleteMutation.isPending}
              >
                批量删除
              </Button>
              <Button size="small" onClick={() => setSelectedRowKeys([])}>
                取消选择
              </Button>
            </Space>
          </div>
        )}
        <Table
          dataSource={filteredRecords}
          columns={columns}
          rowKey="id"
          loading={isLoading}
          rowSelection={rowSelection}
          onChange={handleTableChange}
          pagination={{
            pageSize: 20,
            showSizeChanger: true,
            showTotal: (total) => `共 ${total} 条`,
            pageSizeOptions: ['10', '20', '50', '100'],
          }}
          scroll={{ x: 1100, y: 'calc(100vh - 320px)' }}
        />
      </Card>

      <Modal
        title={editingRecord ? '编辑记录' : '新增记录'}
        open={isModalOpen}
        onCancel={() => {
          setIsModalOpen(false);
          setEditingRecord(null);
          form.resetFields();
        }}
        footer={null}
        width={600}
      >
        <Form form={form} layout="vertical" onFinish={handleSubmit}>
          <Form.Item
            name="project"
            label="项目名称"
            rules={[{ required: true, message: '请输入项目名称' }]}
          >
            <Input />
          </Form.Item>
          <Form.Item
            name="technicalSpec"
            label="技术指标"
            rules={[{ required: true, message: '请输入技术指标' }]}
          >
            <TextArea rows={2} />
          </Form.Item>
          <Form.Item name="actualSpec" label="实际规格">
            <TextArea rows={2} />
          </Form.Item>
          <Form.Item name="remark" label="备注">
            <TextArea rows={2} />
          </Form.Item>
          <Form.Item>
            <Space>
              <Button type="primary" htmlType="submit" loading={createMutation.isPending || updateMutation.isPending}>
                保存
              </Button>
              <Button onClick={() => setIsModalOpen(false)}>取消</Button>
            </Space>
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default HistoryPage;
