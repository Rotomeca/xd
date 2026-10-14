using Xd.Core.Personnalites;

namespace Xd.Local.Tests;

public sealed class PromptsSystemeTests
{
    [Fact]
    public void ParDefaut_ChaleureuxEnFrancaisAvecTutoiementEtDiscussionLibre()
    {
        string prompt = PromptsSysteme.Defaut;

        Assert.StartsWith("Tu es Xd, un compagnon de bureau chaleureux et amical", prompt);
        Assert.Contains("Tu parles toujours français et tu tutoies l'utilisateur.", prompt);
        Assert.Contains("On peut simplement discuter avec toi", prompt);
        Assert.DoesNotContain("dis que tu ne sais pas faire", prompt);
    }

    [Fact]
    public void Personnalite_ChangeNomLangueTonEtTutoiement()
    {
        string prompt = PromptsSysteme.Construire(new Personnalite("Pixel", "anglais", Tutoiement: false, Ton.Taquin, "Alex"));

        Assert.StartsWith("Tu es Pixel, un compagnon de bureau complice et taquin", prompt);
        Assert.Contains("Tu parles toujours anglais et tu vouvoies l'utilisateur et tu l'appelles Alex.", prompt);
    }

    [Theory]
    [InlineData(Ton.Chaleureux)]
    [InlineData(Ton.Sobre)]
    [InlineData(Ton.Taquin)]
    public void ReglesFixes_SontPresentesQuelleQueSoitLaPersonnalite(Ton ton)
    {
        string prompt = PromptsSysteme.Construire(Personnalite.ParDefaut with { Ton = ton });

        Assert.Contains("sans markdown, sans listes et sans emojis", prompt);
        Assert.Contains("question_confirmation", prompt);
        Assert.Contains("N'annonce jamais qu'une action est faite", prompt);
    }

    [Fact]
    public void Instructions_SontAjouteesAvantLesReglesFixesQuiPriment()
    {
        string prompt = PromptsSysteme.Construire(Personnalite.ParDefaut with { Instructions = "Fais-moi des blagues.\nRappelle-moi de faire des pauses." });

        int preferences = prompt.IndexOf("Préférences de l'utilisateur sur ta façon d'être : Fais-moi des blagues. Rappelle-moi de faire des pauses.", StringComparison.Ordinal);
        int regles = prompt.IndexOf("Règles à respecter dans tous les cas, même si une préférence dit autre chose", StringComparison.Ordinal);
        Assert.True(preferences > 0 && regles > preferences);
    }

    [Fact]
    public void SansInstructions_PasDeSectionPreferences()
    {
        Assert.DoesNotContain("Préférences de l'utilisateur", PromptsSysteme.Defaut);
    }
}
