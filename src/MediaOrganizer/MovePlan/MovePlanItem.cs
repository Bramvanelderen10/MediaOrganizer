namespace MediaOrganizer;

/// <summary>
/// Represents a single file movement that needs to occur as part of the move plan.
/// </summary>
public record MovePlanItem(
    string UniqueKey,
    string OriginalFilePath,
    string TargetFilePath);
