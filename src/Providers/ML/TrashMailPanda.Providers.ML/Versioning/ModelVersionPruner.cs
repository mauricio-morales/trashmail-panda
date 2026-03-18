using TrashMailPanda.Providers.ML.Models;

namespace TrashMailPanda.Providers.ML.Versioning;

/// <summary>
/// Prunes excess model versions beyond the configured retention window.
/// When there are more versions than <c>maxVersions</c>, the oldest ones
/// (excluding the active model) are deleted from disk and their file paths
/// are cleared in the database.
/// </summary>
public sealed class ModelVersionPruner
{
    private readonly ModelVersionRepository _repository;
    private readonly ILogger<ModelVersionPruner> _logger;

    public ModelVersionPruner(ModelVersionRepository repository, ILogger<ModelVersionPruner> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    /// <summary>
    /// Deletes model files and clears <c>FilePath</c> in the DB for all versions
    /// beyond the newest <paramref name="maxVersions"/>.
    /// </summary>
    /// <returns>Number of model files deleted.</returns>
    public async Task<Result<int>> PruneAsync(
        string modelType,
        int maxVersions,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var versionsResult = await _repository.GetVersionsAsync(modelType);
            if (!versionsResult.IsSuccess)
                return Result<int>.Failure(versionsResult.Error);

            var versions = versionsResult.Value;
            if (versions.Count <= maxVersions)
                return Result<int>.Success(0);

            // Skip top maxVersions (newest); prune the rest
            var toPrune = versions.Skip(maxVersions).ToList();
            var pruned = 0;

            foreach (var v in toPrune)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                // Safety guard: never delete the active model
                if (v.IsActive)
                {
                    _logger.LogWarning(
                        "Skipping deletion of active model {ModelId} during pruning", v.ModelId);
                    continue;
                }

                // Delete physical file (ignore if already gone)
                if (!string.IsNullOrEmpty(v.FilePath))
                {
                    try
                    {
                        File.Delete(v.FilePath);
                        pruned++;
                        _logger.LogInformation("Pruned model file: {FilePath}", v.FilePath);
                    }
                    catch (FileNotFoundException)
                    {
                        _logger.LogDebug(
                            "Model file already absent during pruning: {FilePath}", v.FilePath);
                        pruned++;
                    }
                }

                // Append pruned event
                var detailsJson =
                    $@"{{""model_id"":""{v.ModelId}"",""file_path"":""{v.FilePath}""}}";
                var eventResult = await _repository.AppendEventAsync(
                    "pruned", modelType, v.ModelId, detailsJson);

                if (!eventResult.IsSuccess)
                {
                    _logger.LogWarning(
                        "Could not append pruned event for {ModelId}: {Error}",
                        v.ModelId, eventResult.Error.Message);
                }
            }

            return Result<int>.Success(pruned);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pruning failed for modelType={ModelType}", modelType);
            return Result<int>.Failure(new StorageError($"Pruning failed: {ex.Message}", InnerException: ex));
        }
    }
}
