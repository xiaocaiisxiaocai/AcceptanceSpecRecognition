import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MatchResultCard } from '../MatchResultCard';
import type { MatchCandidate } from '../../types';

const mockCandidate: MatchCandidate = {
  record: {
    id: 'rec_001',
    project: '电气控制系统',
    technicalSpec: 'DC24V 输入模块 16点',
    actualSpec: '西门子 SM321 DI16',
    remark: '符合要求',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
  similarityScore: 0.95,
  highlightedActualSpec: '西门子 <span class="keyword">SM321</span> DI16',
  highlightedRemark: '符合要求',
  explanation: {
    embeddingSimilarity: 0.95,
    matchedSynonyms: [],
    preprocessingSteps: [
      { type: 'normalize', before: 'ＤＣ', after: 'DC' }
    ],
  },
};

describe('MatchResultCard', () => {
  it('renders candidate information correctly', () => {
    render(<MatchResultCard candidate={mockCandidate} confidence="Success" />);

    expect(screen.getByText('电气控制系统')).toBeInTheDocument();
    expect(screen.getByText('DC24V 输入模块 16点')).toBeInTheDocument();
  });

  it('displays confidence badge with score', () => {
    render(<MatchResultCard candidate={mockCandidate} confidence="Success" />);

    expect(screen.getByText(/匹配成功/)).toBeInTheDocument();
    expect(screen.getByText(/95\.0%/)).toBeInTheDocument();
  });

  it('calls onConfirm when confirm button is clicked', () => {
    const onConfirm = vi.fn();
    render(
      <MatchResultCard
        candidate={mockCandidate}
        confidence="Success"
        onConfirm={onConfirm}
      />
    );

    fireEvent.click(screen.getByText('确认'));
    expect(onConfirm).toHaveBeenCalledWith('rec_001');
  });

  it('calls onReject when reject button is clicked', () => {
    const onReject = vi.fn();
    render(
      <MatchResultCard
        candidate={mockCandidate}
        confidence="Success"
        onReject={onReject}
      />
    );

    fireEvent.click(screen.getByText('拒绝'));
    expect(onReject).toHaveBeenCalledWith('rec_001');
  });

  it('does not show confirm button when onConfirm is not provided', () => {
    render(<MatchResultCard candidate={mockCandidate} confidence="Success" />);

    expect(screen.queryByText('确认')).not.toBeInTheDocument();
  });

  it('does not show reject button when onReject is not provided', () => {
    render(<MatchResultCard candidate={mockCandidate} confidence="Success" />);

    expect(screen.queryByText('拒绝')).not.toBeInTheDocument();
  });

  it('shows explanation section when explanation is provided', () => {
    render(<MatchResultCard candidate={mockCandidate} confidence="Success" />);

    expect(screen.getByText('匹配解释')).toBeInTheDocument();
  });

  it('renders low confidence correctly', () => {
    render(<MatchResultCard candidate={mockCandidate} confidence="Low" />);
    expect(screen.getByText(/置信度低/)).toBeInTheDocument();
  });
});
