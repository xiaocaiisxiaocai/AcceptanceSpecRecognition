interface HighlightedTextProps {
  html: string;
  className?: string;
}

export const HighlightedText: React.FC<HighlightedTextProps> = ({ html, className }) => {
  return (
    <span
      className={className}
      dangerouslySetInnerHTML={{ __html: html }}
    />
  );
};

export default HighlightedText;
