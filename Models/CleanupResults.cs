namespace PCCare.Models;

public sealed record CleanupScanResult(long SizeBytes, int ItemCount);

public sealed record CleanupCategoryResult(long FreedBytes, int DeletedCount, long RemainingBytes, int RemainingCount);

public sealed record CleanupProgress(double Percent, string Message, long FreedBytes = 0, int DeletedCount = 0);
