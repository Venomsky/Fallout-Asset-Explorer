namespace FalloutAssetExplorer.Core;

public enum FormatCoverageCategory
{
    Parsing,
    Structure,
    Preview,
    Editing,
    SaveExport,
    ValidationIntegrity,
    Tests
}

public enum FormatCoverageStatus
{
    Implemented,
    Partial,
    Missing,
    NotApplicable
}

public enum FormatCoverageConfidence
{
    Low,
    Medium,
    High
}

public enum CoverageEvidenceSource
{
    ProductionCode,
    Test,
    Documentation
}

public enum CoverageDataVerification
{
    RealGameData,
    SyntheticData,
    Unverified
}

public sealed record FormatCoverageEvidence(
    string RepositoryPath,
    CoverageEvidenceSource Source,
    CoverageDataVerification DataVerification,
    string Note);

public sealed record FormatCoverageCapability(
    FormatCoverageCategory Category,
    decimal? Weight,
    FormatCoverageStatus Status,
    string Rationale,
    FormatCoverageConfidence Confidence,
    IReadOnlyList<FormatCoverageEvidence>? Evidence);

public sealed record FormatCoverageDefinition(
    string Id,
    string Name,
    IReadOnlyList<FormatCoverageCapability>? Capabilities);

public sealed record FormatCoverageResult(
    string FormatId,
    string FormatName,
    decimal EarnedPoints,
    decimal ApplicablePoints,
    decimal? Percentage)
{
    public bool IsCalculable => Percentage.HasValue;
}

public static class FormatCoverageWeights
{
    public static decimal GetWeight(FormatCoverageCategory category) =>
        category switch
        {
            FormatCoverageCategory.Parsing => 20m,
            FormatCoverageCategory.Structure => 20m,
            FormatCoverageCategory.Preview => 15m,
            FormatCoverageCategory.Editing => 15m,
            FormatCoverageCategory.SaveExport => 15m,
            FormatCoverageCategory.ValidationIntegrity => 10m,
            FormatCoverageCategory.Tests => 5m,
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown coverage category.")
        };
}

public static class FormatCoverageCalculator
{
    public static FormatCoverageResult Calculate(FormatCoverageDefinition format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (string.IsNullOrWhiteSpace(format.Id))
        {
            throw new ArgumentException("A format identifier is required.", nameof(format));
        }

        if (string.IsNullOrWhiteSpace(format.Name))
        {
            throw new ArgumentException("A format name is required.", nameof(format));
        }

        if (format.Capabilities is null)
        {
            throw new ArgumentException("A capability list is required.", nameof(format));
        }

        var categories = new HashSet<FormatCoverageCategory>();
        var earnedPoints = 0m;
        var applicablePoints = 0m;

        foreach (var capability in format.Capabilities)
        {
            if (capability is null)
            {
                throw new ArgumentException("A capability cannot be null.", nameof(format));
            }

            if (!Enum.IsDefined(capability.Category))
            {
                throw new ArgumentException("A capability has an unknown category.", nameof(format));
            }

            if (!categories.Add(capability.Category))
            {
                throw new ArgumentException(
                    $"Format '{format.Id}' contains more than one '{capability.Category}' capability.",
                    nameof(format));
            }

            var expectedWeight = FormatCoverageWeights.GetWeight(capability.Category);
            if (capability.Weight is not decimal weight
                || weight <= 0
                || weight != expectedWeight)
            {
                throw new ArgumentException(
                    $"Category '{capability.Category}' must use its configured weight of {expectedWeight}.",
                    nameof(format));
            }

            if (!Enum.IsDefined(capability.Status))
            {
                throw new ArgumentException("A capability has an unknown implementation status.", nameof(format));
            }

            if (!Enum.IsDefined(capability.Confidence))
            {
                throw new ArgumentException("A capability has an unknown confidence level.", nameof(format));
            }

            if (string.IsNullOrWhiteSpace(capability.Rationale))
            {
                throw new ArgumentException("A capability rationale is required.", nameof(format));
            }

            ValidateEvidence(capability.Evidence, format);

            if (capability.Status == FormatCoverageStatus.NotApplicable)
            {
                continue;
            }

            applicablePoints += weight;
            earnedPoints += weight * GetCredit(capability.Status);
        }

        decimal? percentage = applicablePoints == 0
            ? null
            : decimal.Round(
                earnedPoints / applicablePoints * 100m,
                1,
                MidpointRounding.AwayFromZero);
        return new FormatCoverageResult(
            format.Id,
            format.Name,
            earnedPoints,
            applicablePoints,
            percentage);
    }

    private static void ValidateEvidence(
        IReadOnlyList<FormatCoverageEvidence>? evidence,
        FormatCoverageDefinition format)
    {
        if (evidence is null || evidence.Count == 0)
        {
            throw new ArgumentException(
                $"Every capability for format '{format.Id}' requires at least one evidence reference.",
                nameof(format));
        }

        foreach (var item in evidence)
        {
            if (item is null
                || string.IsNullOrWhiteSpace(item.RepositoryPath)
                || string.IsNullOrWhiteSpace(item.Note)
                || !Enum.IsDefined(item.Source)
                || !Enum.IsDefined(item.DataVerification))
            {
                throw new ArgumentException(
                    $"Format '{format.Id}' contains an incomplete or invalid evidence reference.",
                    nameof(format));
            }
        }
    }

    private static decimal GetCredit(FormatCoverageStatus status) =>
        status switch
        {
            FormatCoverageStatus.Implemented => 1m,
            FormatCoverageStatus.Partial => 0.5m,
            FormatCoverageStatus.Missing => 0m,
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Status has no numeric credit.")
        };
}
