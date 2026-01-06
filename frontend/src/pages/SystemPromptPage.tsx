import React, { useState } from 'react';
import { Card, Input, Button, Form, message, Typography, Space, Spin, Modal } from 'antd';
import { LockOutlined, UnlockOutlined, SaveOutlined, ReloadOutlined, UndoOutlined } from '@ant-design/icons';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { configApi } from '../services/api';

const { Paragraph, Text } = Typography;
const { Password, TextArea } = Input;

// 密码验证（简单客户端验证，生产环境应使用后端验证）
const VALID_PASSWORD = 'admin';

export const SystemPromptPage: React.FC = () => {
  const [isAuthenticated, setIsAuthenticated] = useState(false);
  const [loading, setLoading] = useState(false);
  const [loginForm] = Form.useForm();
  const [prompt, setPrompt] = useState('');
  const queryClient = useQueryClient();

  // 获取提示词配置
  const { data: prompts, isLoading: promptsLoading, refetch } = useQuery({
    queryKey: ['prompts'],
    queryFn: configApi.getPrompts,
    enabled: isAuthenticated,
  });

  // 更新提示词
  const updateMutation = useMutation({
    mutationFn: configApi.updatePrompts,
    onSuccess: () => {
      message.success('提示词保存成功');
      queryClient.invalidateQueries({ queryKey: ['prompts'] });
    },
    onError: () => {
      message.error('保存失败');
    },
  });

  // 重置提示词
  const resetMutation = useMutation({
    mutationFn: configApi.resetPrompts,
    onSuccess: () => {
      message.success('提示词已重置为默认值');
      queryClient.invalidateQueries({ queryKey: ['prompts'] });
    },
    onError: () => {
      message.error('重置失败');
    },
  });

  // 当提示词加载完成后更新状态
  React.useEffect(() => {
    if (prompts) {
      setPrompt(prompts.unifiedAnalysisPrompt || '');
    }
  }, [prompts]);

  const handleLogin = (values: { password: string }) => {
    setLoading(true);

    // 模拟验证延迟
    setTimeout(() => {
      if (values.password === VALID_PASSWORD) {
        setIsAuthenticated(true);
        message.success('验证成功');
      } else {
        message.error('密码错误');
      }
      setLoading(false);
    }, 500);
  };

  const handleLogout = () => {
    setIsAuthenticated(false);
    loginForm.resetFields();
  };

  const handleReset = () => {
    Modal.confirm({
      title: '确认重置',
      content: '确定要将提示词重置为默认值吗？此操作不可撤销。',
      okText: '确定重置',
      cancelText: '取消',
      okButtonProps: { danger: true },
      onOk: () => {
        resetMutation.mutate();
      },
    });
  };

  const handleSave = () => {
    if (!prompt.trim()) {
      message.error('请输入提示词内容');
      return;
    }
    updateMutation.mutate({ unifiedAnalysisPrompt: prompt });
  };

  // 未认证时显示登录界面
  if (!isAuthenticated) {
    return (
      <div style={{
        padding: 24,
        height: '100%',
        display: 'flex',
        justifyContent: 'center',
        alignItems: 'center'
      }}>
        <Card
          title={
            <span>
              <LockOutlined style={{ marginRight: 8 }} />
              系统提示词访问验证
            </span>
          }
          style={{ width: 400 }}
        >
          <Paragraph type="secondary" style={{ marginBottom: 24 }}>
            编辑系统提示词需要输入管理员密码
          </Paragraph>
          <Form form={loginForm} onFinish={handleLogin} layout="vertical">
            <Form.Item
              name="password"
              label="密码"
              rules={[{ required: true, message: '请输入管理员密码' }]}
            >
              <Password
                prefix={<LockOutlined />}
                placeholder="请输入管理员密码"
                size="large"
              />
            </Form.Item>
            <Form.Item style={{ marginBottom: 0 }}>
              <Button
                type="primary"
                htmlType="submit"
                loading={loading}
                block
                size="large"
              >
                验证
              </Button>
            </Form.Item>
          </Form>
        </Card>
      </div>
    );
  }

  // 认证通过后显示编辑界面
  return (
    <div style={{ padding: 24, height: '100%', display: 'flex', flexDirection: 'column', overflow: 'hidden' }}>
      <Card
        title={
          <span>
            <UnlockOutlined style={{ marginRight: 8 }} />
            统一分析提示词配置
          </span>
        }
        extra={
          <Space>
            <Button
              danger
              icon={<UndoOutlined />}
              onClick={handleReset}
              loading={resetMutation.isPending}
            >
              重置为默认
            </Button>
            <Button type="link" danger onClick={handleLogout}>
              退出
            </Button>
          </Space>
        }
        style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0, overflow: 'hidden' }}
        styles={{ body: { flex: 1, overflow: 'hidden', display: 'flex', flexDirection: 'column', padding: '16px 24px 24px' } }}
      >
        {promptsLoading ? (
          <div style={{ textAlign: 'center', padding: 50 }}>
            <Spin size="large" />
          </div>
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column', height: '100%' }}>
            <Paragraph type="secondary" style={{ marginBottom: 12, flexShrink: 0 }}>
              此提示词用于 LLM 一次性完成：冲突检测、语义等价分析、匹配置信度评估。
              <br />
              支持变量：<Text code>{'{query}'}</Text>（查询规格）、<Text code>{'{candidate}'}</Text>（候选规格）
            </Paragraph>
            <div style={{ flex: 1, minHeight: 0, marginBottom: 12 }}>
              <TextArea
                value={prompt}
                onChange={(e) => setPrompt(e.target.value)}
                style={{
                  height: '100%',
                  fontFamily: 'monospace',
                  fontSize: 13,
                  resize: 'none',
                }}
                placeholder="请输入统一分析提示词..."
              />
            </div>
            <div style={{ flexShrink: 0 }}>
              <Space>
                <Button
                  type="primary"
                  icon={<SaveOutlined />}
                  loading={updateMutation.isPending}
                  onClick={handleSave}
                >
                  保存
                </Button>
                <Button
                  icon={<ReloadOutlined />}
                  onClick={() => refetch()}
                >
                  刷新
                </Button>
              </Space>
            </div>
          </div>
        )}
      </Card>
    </div>
  );
};

export default SystemPromptPage;
