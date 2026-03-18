using Microsoft.ML;
using Microsoft.ML.Trainers;
using TrashMailPanda.Providers.ML.Training;
using Xunit;

namespace TrashMailPanda.Tests.Unit.ML;

[Trait("Category", "Unit")]
public class FeaturePipelineBuilderTests
{
    private readonly MLContext _mlContext = new(seed: 42);
    private readonly FeaturePipelineBuilder _builder = new();

    [Fact]
    public void FeatureColumnNames_HasCorrectCount()
    {
        // 28 numeric floats + 4 categorical-encoded + 2 text-featurized = 34
        Assert.Equal(34, FeaturePipelineBuilder.FeatureColumnNames.Length);
    }

    [Fact]
    public void FeatureColumnNames_AreDistinct()
    {
        var names = FeaturePipelineBuilder.FeatureColumnNames;
        Assert.Equal(names.Length, names.Distinct().Count());
    }

    [Fact]
    public void BuildPipeline_ReturnsPipeline_WithSdcaTrainer()
    {
        // Arrange
        var trainer = _mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(
            labelColumnName: "Label",
            featureColumnName: "Features");

        // Act: should not throw
        var pipeline = _builder.BuildPipeline(_mlContext, trainer);

        // Assert
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void BuildPipeline_ReturnsPipeline_WithLightGbmTrainer()
    {
        // Arrange — LightGbm trainer
        var trainer = _mlContext.MulticlassClassification.Trainers
            .LightGbm(labelColumnName: "Label", featureColumnName: "Features");

        // Act
        var pipeline = _builder.BuildPipeline(_mlContext, trainer);

        // Assert
        Assert.NotNull(pipeline);
    }

    [Fact]
    public void FeatureColumnNames_ContainsExpectedNumerics()
    {
        var names = FeaturePipelineBuilder.FeatureColumnNames;

        // verify representative numeric features are present
        Assert.Contains("SenderKnown", names);
        Assert.Contains("ContactStrength", names);
        Assert.Contains("EmailSizeLog", names);
        Assert.Contains("ThreadMessageCount", names);
        Assert.Contains("SenderFrequency", names);
    }

    [Fact]
    public void FeatureColumnNames_ContainsEncodedCategoricals()
    {
        var names = FeaturePipelineBuilder.FeatureColumnNames;

        // encoded categorical columns produced during pipeline
        Assert.Contains("SenderDomainEncoded", names);
        Assert.Contains("SpfResultEncoded", names);
        Assert.Contains("DkimResultEncoded", names);
        Assert.Contains("DmarcResultEncoded", names);
    }

    [Fact]
    public void FeatureColumnNames_ContainsFeaturizedTextColumns()
    {
        var names = FeaturePipelineBuilder.FeatureColumnNames;

        Assert.Contains("SubjectTextFeaturized", names);
        Assert.Contains("BodyTextShortFeaturized", names);
    }
}
