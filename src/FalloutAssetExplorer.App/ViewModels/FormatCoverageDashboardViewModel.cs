using System.ComponentModel;
using System.Globalization;
using FalloutAssetExplorer.Core;

namespace FalloutAssetExplorer.App.ViewModels;

public sealed class FormatCoverageDashboardViewModel : INotifyPropertyChanged
{
    private FormatCoverageDashboardFormatViewModel? _selectedFormat;

    public FormatCoverageDashboardViewModel(
        IReadOnlyList<FormatCoverageDefinition>? definitions = null)
    {
        var rows = (definitions ?? FormatCoverageCatalog.All)
            .Select(definition => new FormatCoverageDashboardFormatViewModel(
                definition,
                FormatCoverageCalculator.Calculate(definition)))
            .ToArray();
        Formats = Array.AsReadOnly(rows);

        var percentages = rows
            .Where(row => row.IsCalculable)
            .Select(row => row.Result.Percentage!.Value)
            .ToArray();
        AverageCoverage = percentages.Length == 0
            ? null
            : decimal.Round(percentages.Average(), 1, MidpointRounding.AwayFromZero);
        AverageCoverageDisplay = AverageCoverage is decimal average
            ? $"{average.ToString("0.0", CultureInfo.CurrentCulture)}%"
            : "Not available";

        FormatCount = rows.Length;
        UncalculableFormatCount = rows.Count(row => !row.IsCalculable);
        AuditBaselineCommit = FormatCoverageCatalog.AuditBaselineCommit;
        AuditNotice =
            $"These curated results describe the audit for commit {AuditBaselineCommit}; "
            + "they are not automatically updated for later changes.";

        SelectedFormat = rows.FirstOrDefault(row => row.IsCalculable)
            ?? rows.FirstOrDefault();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<FormatCoverageDashboardFormatViewModel> Formats { get; }

    public int FormatCount { get; }

    public decimal? AverageCoverage { get; }

    public string AverageCoverageDisplay { get; }

    public int UncalculableFormatCount { get; }

    public string AuditBaselineCommit { get; }

    public string AuditNotice { get; }

    public bool HasFormats => Formats.Count > 0;

    public bool HasNoFormats => !HasFormats;

    public bool HasSelectedFormat => SelectedFormat is not null;

    public bool HasNoSelectedFormat => SelectedFormat is null;

    public FormatCoverageDashboardFormatViewModel? SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (ReferenceEquals(_selectedFormat, value))
            {
                return;
            }

            _selectedFormat = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedFormat));
            OnPropertyChanged(nameof(HasNoSelectedFormat));
        }
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class FormatCoverageDashboardFormatViewModel
{
    public FormatCoverageDashboardFormatViewModel(
        FormatCoverageDefinition definition,
        FormatCoverageResult result)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(result);

        FormatId = definition.Id;
        Name = definition.Name;
        Result = result;
        Categories = Array.AsReadOnly(
            (definition.Capabilities ?? [])
            .Select(capability => new FormatCoverageDashboardCategoryViewModel(capability))
            .ToArray());

        ConfidenceSummary = BuildConfidenceSummary(Categories);
        EvidenceSummary = BuildEvidenceSummary(Categories);
    }

    public string FormatId { get; }

    public string Name { get; }

    public FormatCoverageResult Result { get; }

    public IReadOnlyList<FormatCoverageDashboardCategoryViewModel> Categories { get; }

    public bool IsCalculable => Result.IsCalculable;

    public string PercentageDisplay => Result.Percentage is decimal percentage
        ? $"{percentage.ToString("0.0", CultureInfo.CurrentCulture)}%"
        : "Not calculated";

    public double ProgressValue => Result.Percentage is decimal percentage
        ? decimal.ToDouble(percentage)
        : 0d;

    public string ConfidenceSummary { get; }

    public string EvidenceSummary { get; }

    private static string BuildConfidenceSummary(
        IReadOnlyList<FormatCoverageDashboardCategoryViewModel> categories)
    {
        var confidenceLevels = categories
            .Select(category => category.Capability.Confidence)
            .Distinct()
            .OrderBy(confidence => confidence)
            .Select(confidence => confidence.ToString())
            .ToArray();
        return confidenceLevels.Length == 0
            ? "Confidence: Not assessed"
            : $"Confidence: {string.Join(", ", confidenceLevels)}";
    }

    private static string BuildEvidenceSummary(
        IReadOnlyList<FormatCoverageDashboardCategoryViewModel> categories)
    {
        var verificationKinds = categories
            .SelectMany(category => category.Evidence)
            .Select(evidence => evidence.Evidence.DataVerification)
            .Distinct()
            .OrderBy(verification => verification)
            .Select(FormatCoverageDashboardEvidenceViewModel.FormatVerification)
            .ToArray();
        return verificationKinds.Length == 0
            ? "Evidence: None recorded"
            : $"Evidence: {string.Join(", ", verificationKinds)}";
    }
}

public sealed class FormatCoverageDashboardCategoryViewModel
{
    public FormatCoverageDashboardCategoryViewModel(FormatCoverageCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);

        Capability = capability;
        CategoryName = capability.Category switch
        {
            FormatCoverageCategory.Parsing => "Parse",
            FormatCoverageCategory.Structure => "Structure",
            FormatCoverageCategory.Preview => "Preview",
            FormatCoverageCategory.Editing => "Edit",
            FormatCoverageCategory.SaveExport => "Save/export",
            FormatCoverageCategory.ValidationIntegrity => "Validation/integrity",
            FormatCoverageCategory.Tests => "Tests",
            _ => capability.Category.ToString()
        };
        StatusText = capability.Status switch
        {
            FormatCoverageStatus.Implemented => "Implemented",
            FormatCoverageStatus.Partial => "Partial",
            FormatCoverageStatus.Missing => "Missing",
            FormatCoverageStatus.NotApplicable => "Nie dotyczy",
            _ => capability.Status.ToString()
        };
        WeightDisplay = capability.Weight is decimal weight
            ? $"{weight.ToString("0.#", CultureInfo.CurrentCulture)} pts"
            : "Weight unavailable";
        ConfidenceDisplay = capability.Confidence.ToString();
        Evidence = Array.AsReadOnly(
            (capability.Evidence ?? [])
            .Select(evidence => new FormatCoverageDashboardEvidenceViewModel(evidence))
            .ToArray());
    }

    public FormatCoverageCapability Capability { get; }

    public FormatCoverageCategory Category => Capability.Category;

    public FormatCoverageStatus Status => Capability.Status;

    public string CategoryName { get; }

    public string StatusText { get; }

    public string WeightDisplay { get; }

    public string Rationale => Capability.Rationale;

    public string ConfidenceDisplay { get; }

    public IReadOnlyList<FormatCoverageDashboardEvidenceViewModel> Evidence { get; }
}

public sealed class FormatCoverageDashboardEvidenceViewModel
{
    public FormatCoverageDashboardEvidenceViewModel(FormatCoverageEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        Evidence = evidence;
        SourceDisplay = evidence.Source switch
        {
            CoverageEvidenceSource.ProductionCode => "Production code",
            CoverageEvidenceSource.Test => "Test",
            CoverageEvidenceSource.Documentation => "Documentation",
            _ => evidence.Source.ToString()
        };
        DataVerificationDisplay = FormatVerification(evidence.DataVerification);
    }

    public FormatCoverageEvidence Evidence { get; }

    public string RepositoryPath => Evidence.RepositoryPath;

    public string SourceDisplay { get; }

    public string DataVerificationDisplay { get; }

    public string SourceAndVerificationDisplay => $"{SourceDisplay} | {DataVerificationDisplay}";

    public string Note => Evidence.Note;

    public static string FormatVerification(CoverageDataVerification verification) =>
        verification switch
        {
            CoverageDataVerification.RealGameData => "Real game data",
            CoverageDataVerification.SyntheticData => "Synthetic data",
            CoverageDataVerification.Unverified => "Unverified",
            _ => verification.ToString()
        };
}
