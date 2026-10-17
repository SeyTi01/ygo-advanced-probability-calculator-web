namespace YGOProbabilityCalculatorBlazor.Models;

// Presentation addresses only. Counting continues to use the separate typed lists.
public enum ComboRequirementKind { Category, Card, Group }
public readonly record struct ComboRequirementPosition(ComboRequirementKind Kind, int Index);
