using ChessGame.Api.Models;

namespace ChessGame.Api.Services;

public static class GachaDrawEngine
{
    public static GachaRates CalculateRates(
        IEnumerable<(string Rarity, int Weight)> pool)
    {
        var rates = new GachaRates();
        foreach (var (rarity, weight) in pool)
        {
            if (weight <= 0)
                throw new ArgumentOutOfRangeException(nameof(pool));

            switch (rarity.ToUpperInvariant())
            {
                case "COMMON": rates.Common = checked(rates.Common + weight); break;
                case "RARE": rates.Rare = checked(rates.Rare + weight); break;
                case "EPIC": rates.Epic = checked(rates.Epic + weight); break;
                case "LEGENDARY": rates.Legendary = checked(rates.Legendary + weight); break;
                default: throw new ArgumentException("Unknown item rarity.", nameof(pool));
            }
        }

        return rates;
    }
    public static string ChooseRarity(
        GachaRates rates,
        GachaPityLimits limits,
        int epicCurrent,
        int legendaryCurrent,
        int randomWeight)
    {
        if (randomWeight < 0 || randomWeight >= rates.Total)
            throw new ArgumentOutOfRangeException(nameof(randomWeight));

        if (legendaryCurrent + 1 >= limits.Legendary)
            return "LEGENDARY";

        var rarity = randomWeight < rates.Common ? "COMMON"
            : randomWeight < rates.Common + rates.Rare ? "RARE"
            : randomWeight < rates.Common + rates.Rare + rates.Epic ? "EPIC"
            : "LEGENDARY";

        if (epicCurrent + 1 >= limits.Epic && rarity is "COMMON" or "RARE")
            return "EPIC";

        return rarity;
    }

    public static (int Epic, int Legendary) Advance(
        int epicCurrent, int legendaryCurrent, string rarity)
    {
        return (
            rarity is "EPIC" or "LEGENDARY" ? 0 : epicCurrent + 1,
            rarity == "LEGENDARY" ? 0 : legendaryCurrent + 1);
    }
}
