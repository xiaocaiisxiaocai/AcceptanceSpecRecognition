using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AcceptanceSpecSystem.Core.AI.Models;
using AcceptanceSpecSystem.Core.AI.SemanticKernel;
using AcceptanceSpecSystem.Core.Diagnostics;
using AcceptanceSpecSystem.Core.Documents.Intelligence.Structure;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;


namespace AcceptanceSpecSystem.Core.Matching.Services;

public partial class LlmMatchingAssistService
{
    private sealed class ThinkContentFilter
    {
        private const string ThinkOpen = "<think>";
        private const string ThinkClose = "</think>";
        private readonly StringBuilder _buffer = new();
        private bool _insideThinkBlock;

        public string Push(string? chunk)
        {
            if (string.IsNullOrEmpty(chunk))
            {
                return string.Empty;
            }

            _buffer.Append(chunk);
            return DrainBuffer(finalize: false);
        }

        public string Flush()
        {
            return DrainBuffer(finalize: true);
        }

        private string DrainBuffer(bool finalize)
        {
            if (_buffer.Length == 0)
            {
                return string.Empty;
            }

            var output = new StringBuilder();
            var text = _buffer.ToString();
            var index = 0;

            while (index < text.Length)
            {
                if (_insideThinkBlock)
                {
                    var closeIndex = text.IndexOf(ThinkClose, index, StringComparison.OrdinalIgnoreCase);
                    if (closeIndex < 0)
                    {
                        if (finalize)
                        {
                            index = text.Length;
                        }
                        else
                        {
                            KeepTail(text, index);
                            return output.ToString();
                        }
                    }
                    else
                    {
                        index = closeIndex + ThinkClose.Length;
                        _insideThinkBlock = false;
                    }

                    continue;
                }

                var openIndex = text.IndexOf(ThinkOpen, index, StringComparison.OrdinalIgnoreCase);
                if (openIndex < 0)
                {
                    if (finalize)
                    {
                        output.Append(text.AsSpan(index));
                        index = text.Length;
                    }
                    else
                    {
                        var safeLength = GetSafeOutputLength(text, index, ThinkOpen.Length);
                        if (safeLength > 0)
                        {
                            output.Append(text.AsSpan(index, safeLength));
                            index += safeLength;
                        }

                        KeepTail(text, index);
                        return output.ToString();
                    }
                }
                else
                {
                    output.Append(text.AsSpan(index, openIndex - index));
                    index = openIndex + ThinkOpen.Length;
                    _insideThinkBlock = true;
                }
            }

            _buffer.Clear();
            return output.ToString();
        }

        private void KeepTail(string text, int index)
        {
            _buffer.Clear();
            if (index < text.Length)
            {
                _buffer.Append(text.AsSpan(index));
            }
        }

        private static int GetSafeOutputLength(string text, int startIndex, int markerLength)
        {
            var remaining = text.Length - startIndex;
            if (remaining <= markerLength - 1)
            {
                return 0;
            }

            return remaining - (markerLength - 1);
        }
    }

}
