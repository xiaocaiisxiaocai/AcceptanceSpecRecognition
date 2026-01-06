import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { HighlightedText } from '../HighlightedText';

describe('HighlightedText', () => {
  it('renders plain text correctly', () => {
    render(<HighlightedText html="Hello World" />);
    expect(screen.getByText('Hello World')).toBeInTheDocument();
  });

  it('renders HTML with highlights', () => {
    const html = 'This is <span style="color: red;">highlighted</span> text';
    const { container } = render(<HighlightedText html={html} />);
    
    expect(container.querySelector('span span')).toBeInTheDocument();
    expect(container.textContent).toContain('highlighted');
  });

  it('applies custom className', () => {
    const { container } = render(
      <HighlightedText html="Test" className="custom-class" />
    );
    
    expect(container.querySelector('.custom-class')).toBeInTheDocument();
  });

  it('handles empty string', () => {
    const { container } = render(<HighlightedText html="" />);
    expect(container.querySelector('span')).toBeInTheDocument();
    expect(container.textContent).toBe('');
  });

  it('renders multiple highlighted keywords', () => {
    const html = '<span class="keyword">DC</span>24V <span class="keyword">输入</span>模块';
    const { container } = render(<HighlightedText html={html} />);
    
    const keywords = container.querySelectorAll('.keyword');
    expect(keywords.length).toBe(2);
  });
});
