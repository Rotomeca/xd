using Xd.Local.Installation;

namespace Xd.Local.Tests.Installation;

public sealed class MesureNvidiaSmiTests
{
    [Theory]
    [InlineData("11264\n", 11264L * 1024 * 1024)]
    [InlineData("  8000\r\n4000\r\n", 8000L * 1024 * 1024)]
    public void SortieValide_DonneLaMemoireDeLaPremiereCarte(string sortie, long attendu)
    {
        Assert.Equal(attendu, MesureNvidiaSmi.Lire(sortie).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[N/A]")]
    [InlineData("-5")]
    public void SortieInvalide_NeDonneRien(string sortie)
    {
        Assert.True(MesureNvidiaSmi.Lire(sortie).IsEmpty);
    }
}
