using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Soratus.Portal.Components.Pages.Klant;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Presentatie;

/// <summary>
/// De structuur van het facturatiescherm: welke gegevens de twee weergavecomponenten kúnnen tonen.
/// </summary>
/// <remarks>
/// <para><strong>Dit is de scherpste test van §2 op dit scherm.</strong> "Facturatie: Azure per dienst
/// + beheeropslag — nee" is geen eigenschap van de markup maar van de typen die het klantcomponent
/// aanneemt. Een test op markup kijkt naar het laatste station en blijft groen zodra een pagina
/// stukgaat; deze test kijkt naar wat er überhaupt te renderen valt.</para>
///
/// <para>Hij loopt de parameters van <c>CustomerBilling</c> af en vandaar door elk eigen type dat
/// eraan hangt. Staat er ergens in die boom een lid met "surcharge", "line", "subtotal", "scope" of
/// "failure" in de naam, dan bestaat er in dat component een uitdrukking die onze marge of de
/// bevraagde omgeving op het scherm zet — en dan is het enige dat de klant nog beschermt een
/// <c>@if</c>, en dat is precies de oplossing die is afgekeurd.</para>
///
/// <para>De spiegel staat eronder: op <c>OperatorBilling</c> hóórt elk van die woorden voor te komen.
/// Zonder die spiegel blijft de eerste test groen zodra iemand de uitsplitsing uit béide componenten
/// haalt, en dan is het scherm niet veiliger maar leeg.</para>
///
/// <para>Dezelfde vorm en dezelfde reflectiewandeling als <see cref="UrencomponentTests"/>; dat is
/// bewust geen gedeelde hulpklasse, want de twee lijsten met verboden woorden zijn het onderwerp van
/// elk van die tests en horen naast hun eigen scherm te staan.</para>
/// </remarks>
public class FactuurcomponentTests
{
    /// <summary>
    /// De woorddelen die op geen enkel type in de klantboom mogen voorkomen.
    /// </summary>
    /// <remarks>
    /// <para>Woorddelen en geen volledige namen, zodat <c>SurchargePercentage</c> en
    /// <c>SurchargeAmount</c> beide worden gevonden zonder dat iemand de lijst hoeft bij te werken als
    /// er een derde bij komt.</para>
    ///
    /// <para><strong>"total" en "charged" staan er met opzet niet bij.</strong> Het door te belasten
    /// bedrag en het maandtotaal zijn precies wat §2 de klant wél geeft ("Facturatie: bedragen en
    /// status — ja"). Zou "total" in deze lijst staan, dan bewaakt de test de verkeerde eigenschap en
    /// staat hij rood bij de eerste juiste implementatie.</para>
    ///
    /// <para><strong>"state" staat er ook niet bij, en dat is een echte afweging.</strong>
    /// <c>AzureCostState</c> onderscheidt "geen regels" van "onbekend", en dat verschil is
    /// bedrijfsvoering: de klant hoort te lezen dat het bedrag nog niet vaststaat, niet welke van de
    /// drie oorzaken het is. Dat gegeven staat daarom niet op <see cref="CustomerChargeRow"/> — maar
    /// het woorddeel "state" zou ook <c>IsFinal</c> niet raken en zou hier dus niets toevoegen boven
    /// de test die de typen zelf vergelijkt.</para>
    /// </remarks>
    private static readonly string[] Verboden = ["surcharge", "line", "subtotal", "scope", "failure"];

    [Fact]
    public void HetKlantcomponentNeemtGeenEnkelTypeAanDatDeMargeOfDeUitsplitsingDraagt()
    {
        var gevonden = Leden(typeof(CustomerBilling))
            .Where(lid => Verboden.Any(woord =>
                lid.Contains(woord, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(
            gevonden.Length == 0,
            "De parameters van CustomerBilling dragen een lid dat volgens §2 operator-only is:\n"
            + string.Join("\n", gevonden) + "\n\n"
            + "§2 zegt: \"Facturatie: Azure per dienst + beheeropslag — nee\" voor de klant. Dat hoort "
            + "een typeverschil te zijn en geen @if in de markup: wat er niet op het type staat kan "
            + "niet lekken, ook niet als iemand er over een half jaar een tooltip bij zet. Hoort dit "
            + "gegeven bij de operator, dan hoort het op OperatorBillingView of OperatorChargeRow en "
            + "niet op de klantvariant.");
    }

    [Fact]
    public void HetOperatorcomponentDraagtDieGegevensWel()
    {
        // De onmisbare spiegel. Zonder deze test blijft de test hierboven groen nadat de uitsplitsing
        // uit béide componenten is verdwenen — en dan is er geen scherm meer waarop een operator zijn
        // marge kan controleren, terwijl de zichtbaarheidstest tevreden is.
        var leden = Leden(typeof(OperatorBilling));

        foreach (var woord in Verboden)
        {
            Assert.Contains(
                leden,
                lid => lid.Contains(woord, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void DeTweeComponentenNemenNietHetzelfdeViewmodelAan()
    {
        // De vorm van de scheiding, in één regel. Zou er één component met een vlag staan, of zouden
        // beide hetzelfde viewmodel aannemen, dan bestaat er een uitdrukking die het operatormodel op
        // een klantscherm zet.
        //
        // string wordt overgeslagen: OperatorBilling neemt de geselecteerde maand als string aan, en
        // dat is geen viewmodel maar een stukje URL. Zonder die uitzondering meet deze test de
        // typeoverlap van een parametertype dat over niets gaat.
        var klant = Parameters(typeof(CustomerBilling))
            .Select(p => p.PropertyType)
            .Where(t => t != typeof(string))
            .ToArray();

        var operatorkant = Parameters(typeof(OperatorBilling))
            .Select(p => p.PropertyType)
            .Where(t => t != typeof(string))
            .ToArray();

        Assert.NotEmpty(klant);
        Assert.NotEmpty(operatorkant);
        Assert.Empty(klant.Intersect(operatorkant));
    }

    [Fact]
    public void DeKlantvariantDraagtDeOperatorredenenNiet()
    {
        // MonthlyChargeGap heeft een waarde die NoSurchargeAgreed heet, en de mededeling "er is nog
        // geen opslag afgesproken" vertelt een klant dat er een opslag ís. De klantvariant heeft daarom
        // een eigen enum met vier waarden die onze marge niet noemen.
        //
        // Een enum en geen string: een reden die als tekst reist, kan uit een catch-blok komen — en dan
        // staat de tekst van een uitzondering in de inbox van een klant. Dat is de fout van de punten
        // 13 en 14 voor de derde keer, nu in een mail.
        var gap = typeof(CustomerChargeRow).GetProperty(nameof(CustomerChargeRow.Gap));

        Assert.NotNull(gap);
        Assert.Equal(typeof(CustomerChargeGap), gap.PropertyType);

        Assert.DoesNotContain(
            Enum.GetNames<CustomerChargeGap>(),
            naam => naam.Contains("surcharge", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeOperatorvariantDraagtDieRedenenWel()
    {
        // De spiegel: zonder deze test mag MonthlyChargeGap zijn precisie verliezen en weet een
        // operator niet meer welke afspraak ontbreekt.
        var gap = typeof(OperatorChargeRow).GetProperty(nameof(OperatorChargeRow.Gap));

        Assert.NotNull(gap);
        Assert.Contains(
            Enum.GetNames<Soratus.Portal.Data.MonthlyChargeGap>(),
            naam => naam.Contains("surcharge", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>De <c>[Parameter]</c>-eigenschappen van een component.</summary>
    private static IEnumerable<PropertyInfo> Parameters(Type component) =>
        component
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null);

    /// <summary>
    /// Elke ledennaam die vanaf de parameters van dit component bereikbaar is.
    /// </summary>
    /// <param name="component">Het componenttype.</param>
    /// <returns>De namen, met het type ervoor zodat een falende assertie te lezen is.</returns>
    private static IReadOnlyList<string> Leden(Type component)
    {
        var gezien = new HashSet<Type>();
        var namen = new List<string>();

        foreach (var parameter in Parameters(component))
        {
            Loop(parameter.PropertyType);
        }

        return namen;

        void Loop(Type type)
        {
            foreach (var deel in Onderdelen(type))
            {
                if (!gezien.Add(deel))
                {
                    continue;
                }

                foreach (var lid in deel.GetProperties(BindingFlags.Instance | BindingFlags.Public))
                {
                    namen.Add($"{deel.Name}.{lid.Name}");
                    Loop(lid.PropertyType);
                }
            }
        }
    }

    /// <summary>
    /// De eigen typen in deze typeverwijzing: het type zelf, of dat waar een lijst of een nullable
    /// om heen staat.
    /// </summary>
    /// <remarks>
    /// Recursief door de eigen typen heen en niet door die van het framework: bij <c>string</c>,
    /// <c>decimal</c> en <c>DateTimeOffset</c> is er niets te zoeken, en zonder die grens loopt de
    /// wandeling de hele BCL in.
    /// </remarks>
    private static IEnumerable<Type> Onderdelen(Type type)
    {
        var kern = Nullable.GetUnderlyingType(type) ?? type;

        if (kern.IsGenericType && typeof(IEnumerable).IsAssignableFrom(kern))
        {
            foreach (var argument in kern.GetGenericArguments())
            {
                foreach (var deel in Onderdelen(argument))
                {
                    yield return deel;
                }
            }

            yield break;
        }

        if (kern.Assembly == typeof(CustomerBilling).Assembly && !kern.IsEnum)
        {
            yield return kern;
        }
    }
}
