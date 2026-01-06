import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { ConfigProvider } from 'antd';
import zhCN from 'antd/locale/zh_CN';
import { Layout } from './Layout';
import { MatchPage, HistoryPage, ConfigPage, AuditPage, SystemPromptPage } from './pages';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 1000 * 60,
      retry: 1,
    },
  },
});

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ConfigProvider locale={zhCN}>
        <BrowserRouter>
          <Routes>
            <Route path="/" element={<Layout />}>
              <Route index element={<Navigate to="/match" replace />} />
              <Route path="match" element={<MatchPage />} />
              <Route path="history" element={<HistoryPage />} />
              <Route path="config" element={<ConfigPage />} />
              <Route path="audit" element={<AuditPage />} />
              <Route path="system-prompt" element={<SystemPromptPage />} />
            </Route>
          </Routes>
        </BrowserRouter>
      </ConfigProvider>
    </QueryClientProvider>
  );
}

export default App;
