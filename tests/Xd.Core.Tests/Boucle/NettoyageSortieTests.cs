using Xd.Core.Boucle;

namespace Xd.Core.Tests.Boucle;

public sealed class NettoyageSortieTests
{
    [Theory]
    [InlineData("Salut ! Ça va ? 😊", "Salut ! Ça va ?")]
    [InlineData("Bravo 👍🏽 !", "Bravo !")]
    [InlineData("Je t'aime ❤️ fort", "Je t'aime fort")]
    [InlineData("C'est **important** et `technique`.", "C'est important et technique.")]
    [InlineData("# Titre\nUne ligne\n  ## Autre", "Titre Une ligne Autre")]
    [InlineData("Il fait 20 °C, ça coûte 5 € et 2 → 3.", "Il fait 20 °C, ça coûte 5 € et 2 → 3.")]
    [InlineData("Trop   d'espaces  ", "Trop d'espaces")]
    public void TexteALire_EstNettoye(string brut, string attendu)
    {
        Assert.Equal(attendu, NettoyageSortie.PourLaVoix(brut));
    }
}
