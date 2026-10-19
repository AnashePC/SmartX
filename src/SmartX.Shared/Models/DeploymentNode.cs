namespace SmartX.Shared.Models;

/// <summary>
/// One node in a multi-tier deployment hierarchy, e.g.
/// Facility A -> Zone 1 -> Sub-Zone B -> Device (ESP32-04).
/// </summary>
public sealed class DeploymentNode
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>"Facility" | "Zone" | "SubZone" | "Device".</summary>
    public string NodeType { get; set; } = string.Empty;

    public List<DeploymentNode> Children { get; set; } = new();
}

public sealed record ValidationResult(bool IsValid, IReadOnlyList<string> Errors);

/// <summary>
/// Recursively walks a deployment tree to confirm every node is safely
/// configured: unique IDs mesh-wide, no node nested deeper than allowed,
/// and leaf-only "Device" nodes never carry children of their own.
/// </summary>
public static class DeploymentValidator
{
    public const int MaxDepth = 6;

    public static ValidationResult Validate(DeploymentNode root)
    {
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        ValidateRecursive(root, depth: 0, path: root.Name, seenIds, errors);

        return new ValidationResult(errors.Count == 0, errors);
    }

    /// <summary>
    /// Base case: node is null, or depth exceeds the configured maximum.
    /// Recursive case: validate this node, then recurse into every child,
    /// extending the human-readable path (e.g. "Facility A -> Zone 1 -> Sub-Zone B")
    /// so any error message pinpoints exactly where in the tree it happened.
    /// </summary>
    private static void ValidateRecursive(
        DeploymentNode? node,
        int depth,
        string path,
        HashSet<string> seenIds,
        List<string> errors)
    {
        if (node is null)
        {
            errors.Add($"Null node encountered at '{path}'.");
            return;
        }

        if (string.IsNullOrWhiteSpace(node.Id))
        {
            errors.Add($"Node at '{path}' is missing a unique identifier.");
        }
        else if (!seenIds.Add(node.Id))
        {
            errors.Add($"Duplicate node id '{node.Id}' detected at '{path}'.");
        }

        if (depth > MaxDepth)
        {
            errors.Add($"Deployment tree exceeds max depth of {MaxDepth} at '{path}'.");
            return; // stop recursing further down this branch
        }

        if (string.Equals(node.NodeType, "Device", StringComparison.OrdinalIgnoreCase)
            && node.Children.Count > 0)
        {
            errors.Add($"Device node '{path}' cannot contain child nodes.");
        }

        foreach (var child in node.Children)
        {
            var childPath = $"{path} -> {child.Name}";
            ValidateRecursive(child, depth + 1, childPath, seenIds, errors);
        }
    }
}
