using System.Text.Json;
using Xd.Core.Boucle;
using Xd.Core.Execution;

namespace Xd.Core.Tests.Boucle;

public sealed class HistoriqueConversationTests
{
    private static IReadOnlyList<Message> TourAvecOutil(string demande) =>
    [
        new MessageUtilisateur(demande),
        new MessageAssistant([new BlocAppelOutil($"id-{demande}", "etat_machine", JsonDocument.Parse("{}").RootElement)]),
        new MessageResultatsOutils([new ResultatOutil($"id-{demande}", "RAM : 40 %", false)]),
        new MessageAssistant([new BlocTexte("RAM à 40 %.")]),
    ];

    [Fact]
    public void AuDelaDeLaLimite_OublieLesToursEntiersLesPlusAnciens()
    {
        var historique = new HistoriqueConversation(toursMax: 2);

        IReadOnlyList<Message> deux = TourAvecOutil("deux");
        IReadOnlyList<Message> trois = TourAvecOutil("trois");

        historique.AjouterTour(TourAvecOutil("un"));
        historique.AjouterTour(deux);
        historique.AjouterTour(trois);

        Assert.Equal([.. deux, .. trois], historique.Messages);
    }

    [Fact]
    public void LimiteNulle_EstRefusee()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HistoriqueConversation(toursMax: 0));
    }
}
