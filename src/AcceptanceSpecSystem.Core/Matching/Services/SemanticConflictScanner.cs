using System.Text.RegularExpressions;
using AcceptanceSpecSystem.Core.Matching.Interfaces;
using AcceptanceSpecSystem.Core.Matching.Models;

namespace AcceptanceSpecSystem.Core.Matching.Services;

/// <summary>
/// 语义冲突扫描器。
/// 用确定性规则检测数值/单位、比较符/边界方向、极性反义三类硬冲突，
/// 产出 severity=hard_conflict 的 MatchIssue，替代 LLM 裁决这类有规律的场景。
/// </summary>
public sealed partial class SemanticConflictScanner
{
    private readonly ISpecCanonicalizer _canonicalizer;
}
