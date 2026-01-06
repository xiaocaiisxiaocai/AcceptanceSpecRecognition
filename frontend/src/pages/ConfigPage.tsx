import { Card, Tabs, Form, InputNumber, Switch, Button, Table, Space, message, Input, Modal, Divider, Spin, Tooltip } from 'antd';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { configApi } from '../services/api';
import type { KeywordEntry, TestConnectionResult } from '../types';
import React, { useState } from 'react';
import { PlusOutlined, EditOutlined, DeleteOutlined, EyeInvisibleOutlined, EyeOutlined, ApiOutlined, QuestionCircleOutlined, ClearOutlined } from '@ant-design/icons';

export const ConfigPage: React.FC = () => {
  const queryClient = useQueryClient();
  const [matchingForm] = Form.useForm();
  const [embeddingForm] = Form.useForm();
  const [llmForm] = Form.useForm();
  const [cacheForm] = Form.useForm();
  const [preprocessingForm] = Form.useForm();
  const [keywordForm] = Form.useForm();
  const [editingKeyword, setEditingKeyword] = useState<KeywordEntry | null>(null);
  const [keywordModalOpen, setKeywordModalOpen] = useState(false);
  const [showEmbeddingKey, setShowEmbeddingKey] = useState(false);
  const [showLLMKey, setShowLLMKey] = useState(false);
  const [testingEmbedding, setTestingEmbedding] = useState(false);
  const [testingLLM, setTestingLLM] = useState(false);
  const [clearingCache, setClearingCache] = useState(false);

  // 保存用户输入的 API Key（不被后端返回的 *** 覆盖）
  const embeddingApiKeyRef = React.useRef<string>('');
  const llmApiKeyRef = React.useRef<string>('');

  const { data: config, isLoading: configLoading } = useQuery({
    queryKey: ['config'],
    queryFn: configApi.getConfig,
  });

  const { data: keywords } = useQuery({
    queryKey: ['keywords'],
    queryFn: configApi.getKeywords,
  });

  // 当配置加载完成后更新表单
  React.useEffect(() => {
    if (config?.embedding) {
      embeddingForm.setFieldsValue({
        ...config.embedding,
        // 如果用户已输入过 API Key，保持用户输入的值
        apiKey: embeddingApiKeyRef.current || config.embedding.apiKey
      });
    }
    if (config?.llm) {
      llmForm.setFieldsValue({
        ...config.llm,
        // 如果用户已输入过 API Key，保持用户输入的值
        apiKey: llmApiKeyRef.current || config.llm.apiKey
      });
    }
    if (config?.matching) {
      matchingForm.setFieldsValue(config.matching);
    }
    if (config?.cache) {
      cacheForm.setFieldsValue(config.cache);
    }
    if (config?.preprocessing) {
      preprocessingForm.setFieldsValue(config.preprocessing);
    }
  }, [config, embeddingForm, llmForm, matchingForm, cacheForm, preprocessingForm]);

  const updateConfigMutation = useMutation({
    mutationFn: configApi.updateConfig,
    onSuccess: () => {
      message.success('配置已更新');
      queryClient.invalidateQueries({ queryKey: ['config'] });
    },
  });

  const updateKeywordsMutation = useMutation({
    mutationFn: configApi.updateKeywords,
    onSuccess: () => {
      message.success('关键字库已更新');
      queryClient.invalidateQueries({ queryKey: ['keywords'] });
      setKeywordModalOpen(false);
    },
  });

  const handleSaveMatching = (values: Record<string, unknown>) => {
    updateConfigMutation.mutate({ matching: values as never });
  };

  const handleSaveCache = (values: Record<string, unknown>) => {
    updateConfigMutation.mutate({ cache: values as never });
  };

  const handleSavePreprocessing = (values: Record<string, unknown>) => {
    updateConfigMutation.mutate({ preprocessing: values as never });
  };

  const handleClearCache = () => {
    Modal.confirm({
      title: '清除缓存',
      content: '确定要清除所有缓存吗？这将清除向量缓存、LLM缓存和结果缓存。',
      okText: '确定',
      cancelText: '取消',
      onOk: async () => {
        setClearingCache(true);
        try {
          await configApi.clearCache();
          message.success('缓存已清除');
        } catch {
          message.error('清除缓存失败');
        } finally {
          setClearingCache(false);
        }
      },
    });
  };

  const handleSaveEmbedding = (values: Record<string, unknown>) => {
    // 保存用户输入的 API Key
    if (values.apiKey && values.apiKey !== '***') {
      embeddingApiKeyRef.current = values.apiKey as string;
    }
    updateConfigMutation.mutate({ embedding: values as never });
  };

  const handleSaveLLM = (values: Record<string, unknown>) => {
    // 保存用户输入的 API Key
    if (values.apiKey && values.apiKey !== '***') {
      llmApiKeyRef.current = values.apiKey as string;
    }
    updateConfigMutation.mutate({ llm: values as never });
  };

  const handleTestEmbedding = async () => {
    try {
      const values = await embeddingForm.validateFields();
      setTestingEmbedding(true);
      
      // 获取当前配置中的 apiKey（如果表单中是 *** 则使用原有的）
      let apiKey = values.apiKey || '';
      if (apiKey === '***' && config?.embedding?.apiKey) {
        apiKey = ''; // 后端会使用已保存的 key
      }
      
      const result: TestConnectionResult = await configApi.testEmbedding({
        baseUrl: values.baseUrl,
        apiKey: apiKey,
        model: values.model,
      });
      
      if (result.success) {
        message.success(result.message);
      } else {
        message.error(result.message);
      }
    } catch (error) {
      message.error('请先填写必填字段');
    } finally {
      setTestingEmbedding(false);
    }
  };

  const handleTestLLM = async () => {
    try {
      const values = await llmForm.validateFields();
      setTestingLLM(true);
      
      // 获取当前配置中的 apiKey（如果表单中是 *** 则使用原有的）
      let apiKey = values.apiKey || '';
      if (apiKey === '***' && config?.llm?.apiKey) {
        apiKey = ''; // 后端会使用已保存的 key
      }
      
      const result: TestConnectionResult = await configApi.testLLM({
        baseUrl: values.baseUrl,
        apiKey: apiKey,
        model: values.model,
        temperature: values.temperature,
      });
      
      if (result.success) {
        message.success(result.message);
      } else {
        message.error(result.message);
      }
    } catch (error) {
      message.error('请先填写必填字段');
    } finally {
      setTestingLLM(false);
    }
  };

  const handleAddKeyword = () => {
    setEditingKeyword(null);
    keywordForm.resetFields();
    setKeywordModalOpen(true);
  };

  const handleEditKeyword = (entry: KeywordEntry) => {
    setEditingKeyword(entry);
    keywordForm.setFieldsValue({
      keyword: entry.keyword,
    });
    setKeywordModalOpen(true);
  };

  const handleSaveKeyword = (values: { keyword: string }) => {
    if (!keywords) return;
    const newEntry: KeywordEntry = {
      id: editingKeyword?.id || `kw_${Date.now()}`,
      keyword: values.keyword,
      category: '',
      synonyms: [],
      style: editingKeyword?.style || { color: '#000', backgroundColor: '#ffff00' },
    };
    const updatedKeywords = editingKeyword
      ? keywords.keywords.map(k => k.id === editingKeyword.id ? newEntry : k)
      : [...keywords.keywords, newEntry];
    updateKeywordsMutation.mutate({ ...keywords, keywords: updatedKeywords });
  };

  const handleDeleteKeyword = (id: string) => {
    if (!keywords) return;
    updateKeywordsMutation.mutate({
      ...keywords,
      keywords: keywords.keywords.filter(k => k.id !== id),
    });
  };

  const keywordColumns = [
    { title: '关键字', dataIndex: 'keyword', key: 'keyword' },
    {
      title: '操作',
      key: 'action',
      render: (_: unknown, record: KeywordEntry) => (
        <Space>
          <Button type="link" icon={<EditOutlined />} onClick={() => handleEditKeyword(record)} />
          <Button type="link" danger icon={<DeleteOutlined />} onClick={() => handleDeleteKeyword(record.id)} />
        </Space>
      ),
    },
  ];

  const items = [
    {
      key: 'embedding',
      label: 'Embedding配置',
      children: (
        <Form
          form={embeddingForm}
          layout="vertical"
          onFinish={handleSaveEmbedding}
        >
          <Form.Item name="baseUrl" label="API地址 (BaseUrl)" rules={[{ required: true }]}>
            <Input placeholder="https://api.openai.com/v1" />
          </Form.Item>
          <Form.Item name="apiKey" label="API Key">
            <Input.Password 
              placeholder="sk-xxx 或留空使用环境变量"
              visibilityToggle={{ 
                visible: showEmbeddingKey, 
                onVisibleChange: setShowEmbeddingKey 
              }}
              iconRender={(visible) => (visible ? <EyeOutlined /> : <EyeInvisibleOutlined />)}
              onChange={(e) => {
                const val = e.target.value;
                if (val && val !== '***') {
                  embeddingApiKeyRef.current = val;
                }
              }}
            />
          </Form.Item>
          <Form.Item name="model" label="模型名称" rules={[{ required: true }]}>
            <Input placeholder="text-embedding-3-small" />
          </Form.Item>
          <Form.Item name="dimension" label="向量维度">
            <InputNumber min={128} max={4096} placeholder="1536" style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item>
            <Space>
              <Button type="primary" htmlType="submit" loading={updateConfigMutation.isPending}>
                保存配置
              </Button>
              <Button
                icon={<ApiOutlined />}
                onClick={handleTestEmbedding}
                loading={testingEmbedding}
              >
                测试连接
              </Button>
            </Space>
          </Form.Item>
        </Form>
      ),
    },
    {
      key: 'llm',
      label: 'LLM配置',
      children: (
        <Form
          form={llmForm}
          layout="vertical"
          onFinish={handleSaveLLM}
        >
          <Form.Item name="baseUrl" label="API地址 (BaseUrl)" rules={[{ required: true }]}>
            <Input placeholder="https://api.openai.com/v1" />
          </Form.Item>
          <Form.Item name="apiKey" label="API Key">
            <Input.Password 
              placeholder="sk-xxx 或留空使用环境变量"
              visibilityToggle={{ 
                visible: showLLMKey, 
                onVisibleChange: setShowLLMKey 
              }}
              iconRender={(visible) => (visible ? <EyeOutlined /> : <EyeInvisibleOutlined />)}
              onChange={(e) => {
                const val = e.target.value;
                if (val && val !== '***') {
                  llmApiKeyRef.current = val;
                }
              }}
            />
          </Form.Item>
          <Form.Item name="model" label="模型名称" rules={[{ required: true }]}>
            <Input placeholder="gpt-4o-mini" />
          </Form.Item>
          <Form.Item name="temperature" label="Temperature">
            <InputNumber min={0} max={2} step={0.1} placeholder="0.1" style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item name="maxTokens" label="最大Token数">
            <InputNumber min={100} max={200000} placeholder="2000" style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item>
            <Space>
              <Button type="primary" htmlType="submit" loading={updateConfigMutation.isPending}>
                保存配置
              </Button>
              <Button
                icon={<ApiOutlined />}
                onClick={handleTestLLM}
                loading={testingLLM}
              >
                测试连接
              </Button>
            </Space>
          </Form.Item>
        </Form>
      ),
    },
    {
      key: 'matching',
      label: '匹配配置',
      children: (
        <div style={{ overflow: 'auto', height: '100%', paddingRight: 8 }}>
          <Form
            form={matchingForm}
            layout="vertical"
            onFinish={handleSaveMatching}
          >
          <Form.Item
            name="enableLLM"
            label={
              <span>
                启用 LLM 智能分析{' '}
                <Tooltip title="开启后使用大模型进行冲突检测和智能分析，准确度更高但有API成本；关闭则仅使用向量相似度匹配">
                  <QuestionCircleOutlined style={{ color: '#999' }} />
                </Tooltip>
              </span>
            }
            valuePropName="checked"
          >
            <Switch checkedChildren="LLM+Embedding" unCheckedChildren="纯Embedding" />
          </Form.Item>
          <Divider />
          <Form.Item
            name="matchSuccessThreshold"
            label={
              <span>
                匹配成功阈值{' '}
                <Tooltip title="相似度达到此值时认为匹配成功（绿色），低于此值时认为置信度较低需人工确认（红色）">
                  <QuestionCircleOutlined style={{ color: '#999' }} />
                </Tooltip>
              </span>
            }
          >
            <InputNumber min={0} max={1} step={0.01} style={{ width: '100%' }} />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit" loading={updateConfigMutation.isPending}>
              保存配置
            </Button>
          </Form.Item>
        </Form>
        </div>
      ),
    },
    {
      key: 'cache',
      label: '缓存配置',
      children: (
        <div style={{ overflow: 'auto', height: '100%', paddingRight: 8 }}>
          <Form
            form={cacheForm}
            layout="vertical"
            onFinish={handleSaveCache}
          >
            <Form.Item
              name="enableVectorCache"
              label={
                <span>
                  启用向量缓存{' '}
                  <Tooltip title="缓存Embedding向量结果，相同文本不重复调用API。调试时建议关闭。">
                    <QuestionCircleOutlined style={{ color: '#999' }} />
                  </Tooltip>
                </span>
              }
              valuePropName="checked"
            >
              <Switch checkedChildren="开启" unCheckedChildren="关闭" />
            </Form.Item>
            <Form.Item
              name="enableLLMCache"
              label={
                <span>
                  启用LLM缓存{' '}
                  <Tooltip title="缓存LLM冲突检测结果，相同查询不重复调用API。调试时建议关闭。">
                    <QuestionCircleOutlined style={{ color: '#999' }} />
                  </Tooltip>
                </span>
              }
              valuePropName="checked"
            >
              <Switch checkedChildren="开启" unCheckedChildren="关闭" />
            </Form.Item>
            <Form.Item
              name="enableResultCache"
              label={
                <span>
                  启用结果缓存{' '}
                  <Tooltip title="缓存完整匹配结果。调试时建议关闭。">
                    <QuestionCircleOutlined style={{ color: '#999' }} />
                  </Tooltip>
                </span>
              }
              valuePropName="checked"
            >
              <Switch checkedChildren="开启" unCheckedChildren="关闭" />
            </Form.Item>
            <Form.Item>
              <Space>
                <Button type="primary" htmlType="submit" loading={updateConfigMutation.isPending}>
                  保存配置
                </Button>
                <Button
                  danger
                  icon={<ClearOutlined />}
                  onClick={handleClearCache}
                  loading={clearingCache}
                >
                  清除缓存
                </Button>
              </Space>
            </Form.Item>
          </Form>
        </div>
      ),
    },
    {
      key: 'preprocessing',
      label: '预处理配置',
      children: (
        <div style={{ overflow: 'auto', height: '100%', paddingRight: 8 }}>
          <Form
            form={preprocessingForm}
            layout="vertical"
            onFinish={handleSavePreprocessing}
          >
            <Form.Item
              name="enableChineseSimplification"
              label={
                <span>
                  启用繁简体转换{' '}
                  <Tooltip title="将繁体中文转换为简体中文，提升匹配准确度。例如：通訊→通讯，網路→网络，軟體→软件">
                    <QuestionCircleOutlined style={{ color: '#999' }} />
                  </Tooltip>
                </span>
              }
              valuePropName="checked"
            >
              <Switch checkedChildren="开启" unCheckedChildren="关闭" />
            </Form.Item>
            <Form.Item>
              <Button type="primary" htmlType="submit" loading={updateConfigMutation.isPending}>
                保存配置
              </Button>
            </Form.Item>
          </Form>
        </div>
      ),
    },
    {
      key: 'keywords',
      label: '关键字库',
      children: (
        <div style={{
          display: 'flex',
          flexDirection: 'column',
          height: '100%',
          minHeight: 0,
          overflow: 'hidden'
        }}>
          <div style={{ flexShrink: 0, marginBottom: 16 }}>
            <Button type="primary" icon={<PlusOutlined />} onClick={handleAddKeyword}>
              添加关键字
            </Button>
          </div>
          <div style={{ flex: 1, minHeight: 0, overflow: 'hidden' }}>
            <Table
              dataSource={keywords?.keywords}
              columns={keywordColumns}
              rowKey="id"
              pagination={false}
              scroll={{ y: 'calc(100vh - 380px)' }}
            />
          </div>
          <div style={{
            flexShrink: 0,
            padding: '12px 0',
            borderTop: '1px solid #f0f0f0',
            textAlign: 'right',
            color: '#666'
          }}>
            共 {keywords?.keywords?.length || 0} 条
          </div>
        </div>
      ),
    },
  ];

  return (
    <div style={{ padding: 24, height: '100%', display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
      <Card
        title="系统配置"
        style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0, overflow: 'hidden' }}
        styles={{ body: { flex: 1, overflow: 'hidden', display: 'flex', flexDirection: 'column', padding: '0 24px 24px' } }}
      >
        {configLoading ? (
          <div style={{ textAlign: 'center', padding: 50 }}>
            <Spin size="large" />
          </div>
        ) : (
          <Tabs
            items={items}
            style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}
            tabBarStyle={{ marginBottom: 16, flexShrink: 0 }}
          />
        )}
      </Card>

      <Modal title={editingKeyword ? '编辑关键字' : '添加关键字'} open={keywordModalOpen} onCancel={() => setKeywordModalOpen(false)} footer={null}>
        <Form form={keywordForm} layout="vertical" onFinish={handleSaveKeyword}>
          <Form.Item name="keyword" label="关键字" rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item>
            <Button type="primary" htmlType="submit">保存</Button>
          </Form.Item>
        </Form>
      </Modal>
    </div>
  );
};

export default ConfigPage;
