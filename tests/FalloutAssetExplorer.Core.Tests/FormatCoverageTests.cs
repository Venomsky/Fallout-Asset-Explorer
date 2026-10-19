using FalloutAssetExplorer.Core;
using Xunit;

namespace FalloutAssetExplorer.Core.Tests;

public sealed class FormatCoverageTests
{
    private static readonly FormatCoverageEvidence TestEvidence = new(
        "tests/FalloutAssetExplorer.Core.Tests/FormatCoverageTests.cs",
        CoverageEvidenceSource.Test,
        CoverageDataVerification.SyntheticData,
        "Synthetic scorer test evidence.");

    [Fact]
    public void CatalogScoresMatchTheBaselineForAllNineFormats()
    {
        var expected = new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["DAT1"] = 85.3m,
            ["DAT2"] = 97.1m,
            ["FRM"] = 88.2m,
            ["MAP"] = 42.5m,
            ["MSG"] = 87.5m,
            ["INT"] = 57.1m,
            ["PRO"] = 58.8m,
            ["LST"] = 57.1m,
            ["PAL"] = 57.1m
        };

        Assert.Equal(expected.Keys.Order(), FormatCoverageCatalog.All.Select(format => format.Id).Order());
        foreach (var format in FormatCoverageCatalog.All)
        {
            Assert.Equal(expected[format.Id], FormatCoverageCalculator.Calculate(format).Percentage);
        }
    }

    [Fact]
    public void Calculate_MapsImplementationStatusesToCreditsAndExcludesNotApplicable()
    {
        var format = CreateFormat(
            Capability(FormatCoverageCategory.Parsing, FormatCoverageStatus.Implemented),
            Capability(FormatCoverageCategory.Structure, FormatCoverageStatus.Partial),
            Capability(FormatCoverageCategory.Preview, FormatCoverageStatus.Missing),
            Capability(FormatCoverageCategory.Editing, FormatCoverageStatus.NotApplicable));

        var result = FormatCoverageCalculator.Calculate(format);

        Assert.Equal(30m, result.EarnedPoints);
        Assert.Equal(55m, result.ApplicablePoints);
        Assert.Equal(54.5m, result.Percentage);
    }

    [Fact]
    public void Calculate_ExcludesNotApplicableCategoryWeightFromTheDenominator()
    {
        var format = CreateFormat(
            Capability(FormatCoverageCategory.Parsing, FormatCoverageStatus.Implemented),
            Capability(FormatCoverageCategory.Editing, FormatCoverageStatus.NotApplicable));

        var result = FormatCoverageCalculator.Calculate(format);

        Assert.Equal(20m, result.EarnedPoints);
        Assert.Equal(20m, result.ApplicablePoints);
        Assert.Equal(100m, result.Percentage);
    }

    [Fact]
    public void Calculate_RoundsOnlyTheFinalPercentageToOneDecimalAwayFromZero()
    {
        var format = CreateFormat(
            Capability(FormatCoverageCategory.Parsing, FormatCoverageStatus.Missing),
            Capability(FormatCoverageCategory.SaveExport, FormatCoverageStatus.Missing),
            Capability(FormatCoverageCategory.Tests, FormatCoverageStatus.Partial));

        var result = FormatCoverageCalculator.Calculate(format);

        Assert.Equal(2.5m, result.EarnedPoints);
        Assert.Equal(40m, result.ApplicablePoints);
        Assert.Equal(6.3m, result.Percentage);
    }

    [Fact]
    public void Calculate_ReturnsNoPercentageWhenNoCategoryApplies()
    {
        var empty = CreateFormat();
        var allNotApplicable = CreateFormat(
            Capability(FormatCoverageCategory.Parsing, FormatCoverageStatus.NotApplicable),
            Capability(FormatCoverageCategory.Tests, FormatCoverageStatus.NotApplicable));

        var emptyResult = FormatCoverageCalculator.Calculate(empty);
        var notApplicableResult = FormatCoverageCalculator.Calculate(allNotApplicable);

        Assert.False(emptyResult.IsCalculable);
        Assert.Null(emptyResult.Percentage);
        Assert.Equal(0m, emptyResult.ApplicablePoints);
        Assert.False(notApplicableResult.IsCalculable);
        Assert.Null(notApplicableResult.Percentage);
        Assert.Equal(0m, notApplicableResult.ApplicablePoints);
    }

    [Fact]
    public void Calculate_RejectsMissingEvidenceAndEvidenceWithoutARepositoryReference()
    {
        var missingEvidence = CreateFormat(
            Capability(FormatCoverageCategory.Parsing, FormatCoverageStatus.Implemented, evidence: []));
        var invalidReference = CreateFormat(
            Capability(
                FormatCoverageCategory.Parsing,
                FormatCoverageStatus.Implemented,
                evidence:
                [
                    TestEvidence with { RepositoryPath = " " }
                ]));

        Assert.Throws<ArgumentException>(() => FormatCoverageCalculator.Calculate(missingEvidence));
        Assert.Throws<ArgumentException>(() => FormatCoverageCalculator.Calculate(invalidReference));
    }

    [Fact]
    public void Calculate_RejectsMissingOrInvalidWeights()
    {
        var missing = CreateFormat(
            new FormatCoverageCapability(
                FormatCoverageCategory.Parsing,
                null,
                FormatCoverageStatus.Implemented,
                "Scorer test rationale.",
                FormatCoverageConfidence.High,
                [TestEvidence]));
        var zero = CreateFormat(
            Capability(FormatCoverageCategory.Parsing, FormatCoverageStatus.Implemented, weight: 0m));
        var negative = CreateFormat(
            Capability(FormatCoverageCategory.Parsing, FormatCoverageStatus.Implemented, weight: -1m));
        var changed = CreateFormat(
            Capability(FormatCoverageCategory.Parsing, FormatCoverageStatus.Implemented, weight: 19m));

        Assert.Throws<ArgumentException>(() => FormatCoverageCalculator.Calculate(missing));
        Assert.Throws<ArgumentException>(() => FormatCoverageCalculator.Calculate(zero));
        Assert.Throws<ArgumentException>(() => FormatCoverageCalculator.Calculate(negative));
        Assert.Throws<ArgumentException>(() => FormatCoverageCalculator.Calculate(changed));
    }

    [Fact]
    public void Calculate_RejectsDuplicateCategories()
    {
        var format = CreateFormat(
            Capability(FormatCoverageCategory.Parsing, FormatCoverageStatus.Implemented),
            Capability(FormatCoverageCategory.Parsing, FormatCoverageStatus.Partial));

        Assert.Throws<ArgumentException>(() => FormatCoverageCalculator.Calculate(format));
    }

    [Fact]
    public void Calculate_IsIndependentOfConfidenceAndDataVerificationKind()
    {
        var format = FormatCoverageCatalog.All.Single(candidate => candidate.Id == "MAP");
        var expected = FormatCoverageCalculator.Calculate(format);
        var lowerConfidence = format with
        {
            Capabilities = format.Capabilities!
                .Select(capability => capability with
                {
                    Confidence = FormatCoverageConfidence.Low,
                    Evidence = capability.Evidence!
                        .Select(evidence => evidence with
                        {
                            DataVerification = CoverageDataVerification.Unverified
                        })
                        .ToArray()
                })
                .ToArray()
        };

        var actual = FormatCoverageCalculator.Calculate(lowerConfidence);

        Assert.Equal(expected.EarnedPoints, actual.EarnedPoints);
        Assert.Equal(expected.ApplicablePoints, actual.ApplicablePoints);
        Assert.Equal(expected.Percentage, actual.Percentage);
    }

    private static FormatCoverageDefinition CreateFormat(params FormatCoverageCapability[] capabilities) =>
        new("TEST", "Test format", capabilities);

    private static FormatCoverageCapability Capability(
        FormatCoverageCategory category,
        FormatCoverageStatus status,
        decimal? weight = null,
        IReadOnlyList<FormatCoverageEvidence>? evidence = null) =>
        new(
            category,
            weight ?? FormatCoverageWeights.GetWeight(category),
            status,
            "Scorer test rationale.",
            FormatCoverageConfidence.High,
            evidence ?? [TestEvidence]);
}
