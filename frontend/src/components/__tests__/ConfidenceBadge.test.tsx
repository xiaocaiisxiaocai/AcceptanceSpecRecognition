import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ConfidenceBadge } from '../ConfidenceBadge';

describe('ConfidenceBadge', () => {
  it('renders success confidence correctly', () => {
    render(<ConfidenceBadge confidence="Success" />);
    expect(screen.getByText('匹配成功')).toBeInTheDocument();
  });

  it('renders low confidence correctly', () => {
    render(<ConfidenceBadge confidence="Low" />);
    expect(screen.getByText('置信度低')).toBeInTheDocument();
  });

  it('displays score when provided', () => {
    render(<ConfidenceBadge confidence="Success" score={0.95} />);
    expect(screen.getByText(/95\.0%/)).toBeInTheDocument();
  });

  it('does not display score when not provided', () => {
    render(<ConfidenceBadge confidence="Success" />);
    expect(screen.queryByText(/%/)).not.toBeInTheDocument();
  });

  it('renders unknown when confidence is not provided', () => {
    render(<ConfidenceBadge />);
    expect(screen.getByText('未知')).toBeInTheDocument();
  });
});
