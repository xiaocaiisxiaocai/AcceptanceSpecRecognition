import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { MatchPage } from '../MatchPage';
import { matchApi } from '../../services/api';

// Mock the API
vi.mock('../../services/api', () => ({
  matchApi: {
    matchBatch: vi.fn(),
    confirmMatch: vi.fn(),
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

describe('MatchPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the match form', () => {
    render(<MatchPage />, { wrapper: createWrapper() });

    expect(screen.getByText('验收规范匹配')).toBeInTheDocument();
    expect(screen.getByText('开始匹配')).toBeInTheDocument();
  });

  it('shows validation errors when submitting empty form', async () => {
    render(<MatchPage />, { wrapper: createWrapper() });

    fireEvent.click(screen.getByText('开始匹配'));

    await waitFor(() => {
      expect(screen.getByText('请输入匹配数据')).toBeInTheDocument();
    });
  });

  it('calls matchBatch API when form is submitted', async () => {
    const mockResult = {
      taskId: 'task_001',
      results: [{
        query: { project: '测试项目', technicalSpec: 'DC24V' },
        bestMatch: null,
        similarityScore: 0,
        confidence: 'Low' as const,
        isLowConfidence: true,
        matchMode: 'Embedding',
        durationMs: 100,
      }],
      summary: { totalCount: 1, successCount: 0, lowConfidenceCount: 1 },
    };

    vi.mocked(matchApi.matchBatch).mockResolvedValue(mockResult);

    render(<MatchPage />, { wrapper: createWrapper() });

    fireEvent.change(screen.getByRole('textbox'), {
      target: { value: '测试项目|DC24V' },
    });
    fireEvent.click(screen.getByText('开始匹配'));

    await waitFor(() => {
      expect(matchApi.matchBatch).toHaveBeenCalledWith({
        queries: [{ project: '测试项目', technicalSpec: 'DC24V' }],
      });
    });
  });

  it('displays single match result with card view', async () => {
    const mockResult = {
      taskId: 'task_001',
      results: [{
        query: { project: '测试项目', technicalSpec: 'DC24V' },
        bestMatch: {
          record: {
            id: 'rec_001',
            project: '电气控制系统',
            technicalSpec: 'DC24V 输入模块',
            actualSpec: '西门子 SM321',
            remark: '符合要求',
            createdAt: '2024-01-01T00:00:00Z',
            updatedAt: '2024-01-01T00:00:00Z',
          },
          similarityScore: 0.95,
          highlightedActualSpec: '西门子 SM321',
          highlightedRemark: '符合要求',
        },
        similarityScore: 0.95,
        confidence: 'Success' as const,
        isLowConfidence: false,
        matchMode: 'LLM+Embedding',
        durationMs: 1500,
      }],
      summary: { totalCount: 1, successCount: 1, lowConfidenceCount: 0 },
    };

    vi.mocked(matchApi.matchBatch).mockResolvedValue(mockResult);

    render(<MatchPage />, { wrapper: createWrapper() });

    fireEvent.change(screen.getByRole('textbox'), {
      target: { value: '测试项目|DC24V' },
    });
    fireEvent.click(screen.getByText('开始匹配'));

    await waitFor(() => {
      expect(screen.getByText('匹配结果')).toBeInTheDocument();
      expect(screen.getByText('电气控制系统')).toBeInTheDocument();
    });
  });

  it('displays batch results with table view', async () => {
    const mockResult = {
      taskId: 'task_001',
      results: [
        {
          query: { project: '项目1', technicalSpec: 'DC24V' },
          bestMatch: null,
          similarityScore: 0,
          confidence: 'Low' as const,
          isLowConfidence: true,
          matchMode: 'Embedding',
          durationMs: 50,
        },
        {
          query: { project: '项目2', technicalSpec: 'AC220V' },
          bestMatch: null,
          similarityScore: 0,
          confidence: 'Low' as const,
          isLowConfidence: true,
          matchMode: 'Embedding',
          durationMs: 45,
        },
      ],
      summary: { totalCount: 2, successCount: 0, lowConfidenceCount: 2 },
    };

    vi.mocked(matchApi.matchBatch).mockResolvedValue(mockResult);

    render(<MatchPage />, { wrapper: createWrapper() });

    fireEvent.change(screen.getByRole('textbox'), {
      target: { value: '项目1|DC24V\n项目2|AC220V' },
    });
    fireEvent.click(screen.getByText('开始匹配'));

    await waitFor(() => {
      expect(screen.getByText('共 2 条')).toBeInTheDocument();
    });
  });

  it('shows empty state when no matches found', async () => {
    const mockResult = {
      taskId: 'task_001',
      results: [{
        query: { project: '测试项目', technicalSpec: '不存在的规格' },
        bestMatch: null,
        similarityScore: 0,
        confidence: 'Low' as const,
        isLowConfidence: true,
        matchMode: 'Embedding',
        durationMs: 80,
      }],
      summary: { totalCount: 1, successCount: 0, lowConfidenceCount: 1 },
    };

    vi.mocked(matchApi.matchBatch).mockResolvedValue(mockResult);

    render(<MatchPage />, { wrapper: createWrapper() });

    fireEvent.change(screen.getByRole('textbox'), {
      target: { value: '测试项目|不存在的规格' },
    });
    fireEvent.click(screen.getByText('开始匹配'));

    await waitFor(() => {
      expect(screen.getByText('未找到匹配结果')).toBeInTheDocument();
    });
  });

  it('shows low confidence warning when isLowConfidence is true', async () => {
    const mockResult = {
      taskId: 'task_001',
      results: [{
        query: { project: '测试项目', technicalSpec: 'DC24V' },
        bestMatch: {
          record: {
            id: 'rec_001',
            project: '电气控制系统',
            technicalSpec: 'DC24V 输入模块',
            actualSpec: '西门子 SM321',
            remark: '符合要求',
            createdAt: '2024-01-01T00:00:00Z',
            updatedAt: '2024-01-01T00:00:00Z',
          },
          similarityScore: 0.75,
          highlightedActualSpec: '西门子 SM321',
          highlightedRemark: '符合要求',
        },
        similarityScore: 0.75,
        confidence: 'Low' as const,
        isLowConfidence: true,
        matchMode: 'LLM+Embedding',
        durationMs: 2000,
      }],
      summary: { totalCount: 1, successCount: 0, lowConfidenceCount: 1 },
    };

    vi.mocked(matchApi.matchBatch).mockResolvedValue(mockResult);

    render(<MatchPage />, { wrapper: createWrapper() });

    fireEvent.change(screen.getByRole('textbox'), {
      target: { value: '测试项目|DC24V' },
    });
    fireEvent.click(screen.getByText('开始匹配'));

    await waitFor(() => {
      expect(screen.getByText('置信度较低，请人工复核')).toBeInTheDocument();
    });
  });
});
