using TrashMailPanda.Shared.Models;

namespace TrashMailPanda.Shared;

public class ContactSignal
{
    public bool Known { get; init; }
    public RelationshipStrength Strength { get; init; }
}