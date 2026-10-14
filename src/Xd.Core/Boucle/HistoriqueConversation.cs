namespace Xd.Core.Boucle;

/// <summary>
/// Historique glissant : ne garde que les derniers tours de conversation.
/// </summary>
/// <remarks>
/// Un tour commence par la phrase de l'utilisateur et se termine par un texte de l'assistant.
/// L'historique n'est coupé qu'entre deux tours, jamais entre un <c>tool_use</c> et son <c>tool_result</c>.
/// Non thread-safe : les demandes sont traitées l'une après l'autre.
/// </remarks>
public sealed class HistoriqueConversation
{
    private readonly int _toursMax;
    private readonly Queue<IReadOnlyList<Message>> _tours = new();

    /// <param name="toursMax">Nombre de tours conservés.</param>
    public HistoriqueConversation(int toursMax)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(toursMax, 1);
        _toursMax = toursMax;
    }

    /// <summary>Messages des tours conservés, du plus ancien au plus récent.</summary>
    public IReadOnlyList<Message> Messages => [.. _tours.SelectMany(tour => tour)];

    /// <summary>Ajoute un tour complet et oublie les plus anciens au-delà de la limite.</summary>
    /// <param name="tour">Messages du tour, de la phrase de l'utilisateur au texte final de l'assistant.</param>
    public void AjouterTour(IReadOnlyList<Message> tour)
    {
        _tours.Enqueue(tour);
        while (_tours.Count > _toursMax)
            _tours.Dequeue();
    }
}
