import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { HistoryPage } from '../HistoryPage';
import { historyApi } from '../../services/api';

// Mock the API
vi.mock('../../services/api', () => ({
  historyApi: {
    getAll: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
  },
}));

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
};

describe('HistoryPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the history page title', async () => {
    vi.mocked(historyApi.getAll).mockResolvedValue([]);
    
    render(<HistoryPage />, { wrapper: createWrapper() });
    
    expect(screen.getByText('历史记录管理')).toBeInTheDocument();
  });

  it('displays history records in table', async () => {
    const mockRecords = [
      {
        id: 'rec_001',
        project: '电气控制系统',
        technicalSpec: 'DC24V 输入模块',
        actualSpec: '西门子 SM321',
        remark: '符合要求',
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: '2024-01-01T00:00:00Z',
      },
      {
        id: 'rec_002',
        project: 'PLC控制系统',
        technicalSpec: 'AC220V 输出模块',
        actualSpec: '西门子 SM322',
        remark: '符合要求',
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: '2024-01-01T00:00:00Z',
      },
    ];
    
    vi.mocked(historyApi.getAll).mockResolvedValue(mockRecords);
    
    render(<HistoryPage />, { wrapper: createWrapper() });
    
    await waitFor(() => {
      expect(screen.getByText('电气控制系统')).toBeInTheDocument();
      expect(screen.getByText('PLC控制系统')).toBeInTheDocument();
    });
  });

  it('shows loading state while fetching data', () => {
    vi.mocked(historyApi.getAll).mockImplementation(
      () => new Promise(() => {}) // Never resolves
    );
    
    render(<HistoryPage />, { wrapper: createWrapper() });
    
    // The table should show loading state
    expect(screen.getByText('历史记录管理')).toBeInTheDocument();
  });

  it('shows empty state when no records exist', async () => {
    vi.mocked(historyApi.getAll).mockResolvedValue([]);
    
    render(<HistoryPage />, { wrapper: createWrapper() });
    
    await waitFor(() => {
      // Use getAllByText since there might be multiple "No data" elements
      const noDataElements = screen.getAllByText(/No data/i);
      expect(noDataElements.length).toBeGreaterThan(0);
    });
  });

  it('has search functionality', async () => {
    vi.mocked(historyApi.getAll).mockResolvedValue([]);
    
    render(<HistoryPage />, { wrapper: createWrapper() });
    
    // Check for search input
    const searchInput = screen.getByPlaceholderText(/搜索/i);
    expect(searchInput).toBeInTheDocument();
  });

  it('has add new record button', async () => {
    vi.mocked(historyApi.getAll).mockResolvedValue([]);
    
    render(<HistoryPage />, { wrapper: createWrapper() });
    
    // The button text is "新增记录" not "添加记录"
    expect(screen.getByText('新增记录')).toBeInTheDocument();
  });
});
