import { useState } from 'react';
import { Outlet, useNavigate, useLocation } from 'react-router-dom';
import { Layout as AntLayout, Menu, Typography } from 'antd';
import {
  SearchOutlined,
  HistoryOutlined,
  SettingOutlined,
  AuditOutlined,
  FileTextOutlined,
} from '@ant-design/icons';

const { Sider, Content, Header } = AntLayout;
const { Title } = Typography;

const menuItems = [
  { key: '/match', icon: <SearchOutlined />, label: '匹配查询' },
  { key: '/history', icon: <HistoryOutlined />, label: '历史记录' },
  { key: '/config', icon: <SettingOutlined />, label: '系统配置' },
  { key: '/audit', icon: <AuditOutlined />, label: '审计日志' },
  { key: '/system-prompt', icon: <FileTextOutlined />, label: '系统提示词' },
];

export const Layout: React.FC = () => {
  const [collapsed, setCollapsed] = useState(false);
  const navigate = useNavigate();
  const location = useLocation();

  return (
    <AntLayout style={{ height: '100vh', overflow: 'hidden' }}>
      <Sider
        collapsible
        collapsed={collapsed}
        onCollapse={setCollapsed}
        theme="light"
        style={{ borderRight: '1px solid #f0f0f0', height: '100vh', overflow: 'auto' }}
      >
        <div style={{ padding: '16px', textAlign: 'center' }}>
          <Title level={4} style={{ margin: 0, color: '#1890ff' }}>
            {collapsed ? '验规' : '验收规范识别'}
          </Title>
        </div>
        <Menu
          mode="inline"
          selectedKeys={[location.pathname]}
          items={menuItems}
          onClick={({ key }) => navigate(key)}
        />
      </Sider>
      <AntLayout style={{ height: '100vh', overflow: 'hidden' }}>
        <Header style={{ background: '#fff', padding: '0 24px', borderBottom: '1px solid #f0f0f0', flexShrink: 0 }}>
          <Title level={4} style={{ margin: '16px 0' }}>
            验收规范智能识别系统
          </Title>
        </Header>
        <Content style={{ background: '#f5f5f5', height: 'calc(100vh - 64px)', overflow: 'hidden' }}>
          <Outlet />
        </Content>
      </AntLayout>
    </AntLayout>
  );
};

export default Layout;
