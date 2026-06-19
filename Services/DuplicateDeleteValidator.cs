namespace CopyFinder.Services;

public sealed record DuplicateDeleteCandidate(
    int GroupId,
    string DuplicatePath,
    string KeptPath,
    long ExpectedSize,
    string ExpectedHash);

public sealed record DuplicateDeleteValidation(
    bool CanDelete,
    string? FailureMessage)
{
    public static DuplicateDeleteValidation Success { get; } = new(true, null);

    public static DuplicateDeleteValidation Fail(string message)
    {
        return new DuplicateDeleteValidation(false, message);
    }
}

public static class DuplicateDeleteValidator
{
    public static async Task<DuplicateDeleteValidation> ValidateAsync(
        DuplicateDeleteCandidate candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            var duplicatePath = Path.GetFullPath(candidate.DuplicatePath);
            var keptPath = Path.GetFullPath(candidate.KeptPath);
            if (string.Equals(duplicatePath, keptPath, StringComparison.OrdinalIgnoreCase))
            {
                return DuplicateDeleteValidation.Fail($"Group {candidate.GroupId}: duplicate path is the kept file.");
            }

            if (!SafeFile.FileExists(duplicatePath))
            {
                return DuplicateDeleteValidation.Fail($"Group {candidate.GroupId}: duplicate file no longer exists: {candidate.DuplicatePath}");
            }

            if (!SafeFile.FileExists(keptPath))
            {
                return DuplicateDeleteValidation.Fail($"Group {candidate.GroupId}: kept file no longer exists: {candidate.KeptPath}");
            }

            var duplicateInfo = new FileInfo(duplicatePath);
            if (duplicateInfo.Length != candidate.ExpectedSize)
            {
                return DuplicateDeleteValidation.Fail($"Group {candidate.GroupId}: duplicate file changed since scan: {candidate.DuplicatePath}");
            }

            var keptInfo = new FileInfo(keptPath);
            if (keptInfo.Length != candidate.ExpectedSize)
            {
                return DuplicateDeleteValidation.Fail($"Group {candidate.GroupId}: kept file changed since scan: {candidate.KeptPath}");
            }

            var duplicateHash = await SafeFile.ComputeSha256Async(duplicatePath, cancellationToken);
            if (!string.Equals(duplicateHash, candidate.ExpectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return DuplicateDeleteValidation.Fail($"Group {candidate.GroupId}: duplicate file changed since scan: {candidate.DuplicatePath}");
            }

            var keptHash = await SafeFile.ComputeSha256Async(keptPath, cancellationToken);
            if (!string.Equals(keptHash, candidate.ExpectedHash, StringComparison.OrdinalIgnoreCase))
            {
                return DuplicateDeleteValidation.Fail($"Group {candidate.GroupId}: kept file changed since scan: {candidate.KeptPath}");
            }

            return DuplicateDeleteValidation.Success;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return DuplicateDeleteValidation.Fail($"Group {candidate.GroupId}: validation failed before deletion: {ex.Message}");
        }
    }

}
