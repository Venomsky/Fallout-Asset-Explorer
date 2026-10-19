using System.Globalization;
using FalloutAssetExplorer.App.ViewModels;
using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FormatCoverageDashboardViewModelTests
{
    [Fact]
    public void Dashboard_UsesCatalogAndCalculatorForAllFormatRows()
    {
        var viewModel = new FormatCoverageDashboardViewModel();

        Assert.Equal(
            FormatCoverageCatalog.All.Select(format => format.Id),
            viewModel.Formats.Select(format => format.FormatId));
        Assert.Equal(FormatCoverageCatalog.All.Count, viewModel.FormatCount);
        Assert.Equal(FormatCoverageCatalog.AuditBaselineCommit, viewModel.AuditBaselineCommit);
        Assert.Contains(FormatCoverageCatalog.AuditBaselineCommit, viewModel.AuditNotice);

        foreach (var definition in FormatCoverageCatalog.All)
        {
            var row = Assert.Single(viewModel.Formats, candidate => candidate.FormatId == definition.Id);
            var calculated = FormatCoverageCalculator.Calculate(definition);

            Assert.Equal(calculated.Percentage, row.Result.Percentage);
            Assert.Equal(calculated.Percentage?.ToString("0.0") + "%", row.PercentageDisplay);
        }
    }

    [Fact]
    public void Dashboard_SelectsFirstCalculableFormatByDefault()
    {
        var viewModel = new FormatCoverageDashboardViewModel();

        Assert.Equal(
            FormatCoverageCatalog.All.First(definition =>
                FormatCoverageCalculator.Calculate(definition).IsCalculable).Id,
            viewModel.SelectedFormat?.FormatId);
    }

    [Fact]
    public void SelectedFormatDetails_ExposeAllCategoriesStatusesWeightsAndEvidence()
    {
        var viewModel = new FormatCoverageDashboardViewModel();
        var definition = FormatCoverageCatalog.All.Single(format => format.Id == "DAT1");
        var row = Assert.Single(viewModel.Formats, candidate => candidate.FormatId == definition.Id);

        Assert.Equal(Enum.GetValues<FormatCoverageCategory>(), row.Categories.Select(category => category.Category));
        foreach (var capability in definition.Capabilities!)
        {
            var detail = Assert.Single(row.Categories, category => category.Category == capability.Category);
            Assert.Equal(capability.Status, detail.Status);
            Assert.Equal(capability.Weight, decimal.Parse(
                detail.WeightDisplay.Replace(" pts", string.Empty, StringComparison.Ordinal)));
            Assert.Equal(capability.Rationale, detail.Rationale);
            Assert.Equal(capability.Confidence.ToString(), detail.ConfidenceDisplay);
            Assert.Equal(
                capability.Evidence!.Select(evidence => evidence.RepositoryPath),
                detail.Evidence.Select(evidence => evidence.RepositoryPath));
        }
    }

    [Fact]
    public void NotApplicable_IsDisplayedAsNotApplicableRatherThanMissing()
    {
        var viewModel = new FormatCoverageDashboardViewModel();
        var row = Assert.Single(viewModel.Formats, format => format.FormatId == "INT");
        var editing = Assert.Single(
            row.Categories,
            category => category.Category == FormatCoverageCategory.Editing);

        Assert.Equal(FormatCoverageStatus.NotApplicable, editing.Status);
        Assert.Equal("Nie dotyczy", editing.StatusText);
        Assert.NotEqual("Missing", editing.StatusText);
    }

    [Fact]
    public void ConfidenceIsDisplayedSeparatelyAndDoesNotChangePercentage()
    {
        var definition = FormatCoverageCatalog.All.Single(format => format.Id == "MAP");
        var lowConfidenceDefinition = definition with
        {
            Capabilities = definition.Capabilities!
                .Select(capability => capability with { Confidence = FormatCoverageConfidence.Low })
                .ToArray()
        };
        var original = new FormatCoverageDashboardViewModel([definition]);
        var lowConfidence = new FormatCoverageDashboardViewModel([lowConfidenceDefinition]);

        Assert.Equal(original.Formats[0].Result.Percentage, lowConfidence.Formats[0].Result.Percentage);
        Assert.Contains("Low", lowConfidence.Formats[0].ConfidenceSummary);
        Assert.Contains("Evidence:", lowConfidence.Formats[0].EvidenceSummary);
    }

    [Fact]
    public void UncalculableFormat_IsNotDisplayedAsZeroPercent()
    {
        var viewModel = new FormatCoverageDashboardViewModel(
        [
            new FormatCoverageDefinition("NONE", "No applicable categories", Array.Empty<FormatCoverageCapability>())
        ]);

        var row = Assert.Single(viewModel.Formats);

        Assert.False(row.IsCalculable);
        Assert.Null(row.Result.Percentage);
        Assert.Equal("Not calculated", row.PercentageDisplay);
        Assert.NotEqual("0.0%", row.PercentageDisplay);
    }

    [Fact]
    public void AverageCoverage_ExcludesFormatsWithoutCalculableResults()
    {
        var calculable = new FormatCoverageDefinition(
            "FULL",
            "Calculable format",
            [
                CreateCapability(FormatCoverageCategory.Parsing, FormatCoverageStatus.Implemented)
            ]);
        var uncalculable = new FormatCoverageDefinition(
            "NONE",
            "Uncalculable format",
            Array.Empty<FormatCoverageCapability>());
        var viewModel = new FormatCoverageDashboardViewModel([calculable, uncalculable]);

        Assert.Equal(2, viewModel.FormatCount);
        Assert.Equal(1, viewModel.UncalculableFormatCount);
        Assert.Equal(100m, viewModel.AverageCoverage);
        Assert.Equal($"{100m.ToString("0.0", CultureInfo.CurrentCulture)}%", viewModel.AverageCoverageDisplay);
    }

    [Fact]
    public void EmptyCatalog_ShowsEmptyStateWithoutThrowing()
    {
        var viewModel = new FormatCoverageDashboardViewModel(Array.Empty<FormatCoverageDefinition>());

        Assert.Empty(viewModel.Formats);
        Assert.Equal(0, viewModel.FormatCount);
        Assert.Equal(0, viewModel.UncalculableFormatCount);
        Assert.Null(viewModel.AverageCoverage);
        Assert.Equal("Not available", viewModel.AverageCoverageDisplay);
        Assert.True(viewModel.HasNoFormats);
        Assert.Null(viewModel.SelectedFormat);
        Assert.True(viewModel.HasNoSelectedFormat);
    }

    private static FormatCoverageCapability CreateCapability(
        FormatCoverageCategory category,
        FormatCoverageStatus status) =>
        new(
            category,
            FormatCoverageWeights.GetWeight(category),
            status,
            "Dashboard test capability.",
            FormatCoverageConfidence.High,
            [
                new FormatCoverageEvidence(
                    "tests/FalloutAssetExplorer.Core.Tests/FormatCoverageDashboardViewModelTests.cs",
                    CoverageEvidenceSource.Test,
                    CoverageDataVerification.SyntheticData,
                    "Synthetic dashboard test data.")
            ]);
}
