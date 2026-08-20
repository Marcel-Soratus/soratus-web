using System.Collections;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Soratus.Portal.Components.Pages.Klant;

namespace Soratus.Portal.Tests.Presentatie;

/// <summary>
/// De structuur van het urenscherm: welke gegevens de twee weergavecomponenten kúnnen tonen, en of
/// de formulieren erop aansluiten.
/// </summary>
/// <remarks>
/// <para><strong>Dit is de scherpste test van de tweede acceptatie-eis van fase 3.</strong> Die eis —
/// de klant ziet niets van de fiatteringsstroom — is geen eigenschap van de markup maar van de
/// typen die het klantcomponent aanneemt. Een test op markup kijkt naar het laatste station en
/// blijft groen zodra een pagina stukgaat; deze test kijkt naar wat er überhaupt te renderen valt.
/// </para>
///
/// <para>Hij loopt de parameters van <c>CustomerHours</c> af, en vandaar door elk type dat eraan
/// hangt: het viewmodel, de rijen, de maandstanden, het jaartotaal. Staat er ergens in die boom een
/// lid met "pending", "approv", "reject" of "etag" in de naam, dan bestaat er in dat component een
/// uitdrukking die de fiatteringsstroom op het scherm zet — en dan is het enige dat de klant nog
/// beschermt een <c>@if</c>, en dat is precies de oplossing die is afgekeurd.</para>
///
/// <para>De spiegel staat eronder: op <c>OperatorHours</c> hóórt elk van die woorden voor te komen.
/// Zonder die spiegel blijft de eerste test groen zodra iemand de fiatteringsstroom uit beide
/// componenten haalt, en dan is het scherm niet veiliger maar kapot.</para>
/// </remarks>
public class UrencomponentTests
{
    /// <summary>
    /// De woorddelen die op geen enkel type in de klantboom mogen voorkomen.
    /// </summary>
    /// <remarks>
    /// <para>Woorddelen en geen volledige namen, zodat <c>PendingHours</c>, <c>PendingCount</c> en
    /// <c>HasPending</c> alle drie worden gevonden zonder dat iemand de lijst hoeft bij te werken
    /// als er een vierde bij komt.</para>
    ///
    /// <para><strong>"status" staat er met opzet niet bij.</strong> <c>HourBalance.Status</c> is de
    /// stand van een maand tegenover de bundel — Binnen bundel, Boven bundel, Niets geboekt, Geen
    /// bundel — en die hoort de klant juist te zien; §3.6 vraagt hem expliciet. Wat er niet mag is
    /// <c>HourEntryStatus</c>, en dat type komt in de klantboom nergens voor omdat geen enkel lid
    /// ernaar verwijst. Zou "status" in deze lijst staan, dan zou de test de verkeerde eigenschap
    /// bewaken en bij de eerste juiste implementatie rood staan.</para>
    /// </remarks>
    private static readonly string[] Verboden = ["pending", "approv", "reject", "etag"];

    [Fact]
    public void HetKlantcomponentNeemtGeenEnkelTypeAanDatDeFiatteringsstroomDraagt()
    {
        var gevonden = Leden(typeof(CustomerHours))
            .Where(lid => Verboden.Any(woord =>
                lid.Contains(woord, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.True(
            gevonden.Length == 0,
            "De parameters van CustomerHours dragen een lid dat over de fiatteringsstroom gaat:\n" +
            string.Join("\n", gevonden) + "\n\n" +
            "De acceptatie van fase 3 is dat de klant niets van die stroom ziet, en dat hoort een " +
            "typeverschil te zijn en geen @if in de markup: wat er niet op het type staat kan niet " +
            "lekken, ook niet als iemand er over een half jaar een tooltip bij zet. Hoort dit " +
            "gegeven bij de operator, dan hoort het op OperatorHoursView of OperatorHourRow en niet " +
            "op de klantvariant.");
    }

    [Fact]
    public void HetOperatorcomponentDraagtDieStroomWel()
    {
        // De onmisbare spiegel. Zonder deze test blijft de test hierboven groen nadat de
        // fiatteringsstroom uit béide componenten is verdwenen — en dan is er geen scherm meer om
        // op te fiatteren, terwijl de zichtbaarheidstest tevreden is.
        var leden = Leden(typeof(OperatorHours));

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
        // beide hetzelfde viewmodel aannemen, dan bestaat er een uitdrukking die het operatormodel
        // op een klantscherm zet. Dat is de oplossing die is afgekeurd, en dit is de test die hem
        // tegenhoudt.
        var klant = Parameters(typeof(CustomerHours)).Select(p => p.PropertyType).ToArray();
        var operatorkant = Parameters(typeof(OperatorHours)).Select(p => p.PropertyType).ToArray();

        Assert.Empty(klant.Intersect(operatorkant));
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
    /// <remarks>
    /// Recursief door de eigen typen heen en niet door die van het framework: bij <c>string</c>,
    /// <c>decimal</c> en <c>DateTimeOffset</c> is er niets te zoeken, en zonder die grens loopt de
    /// wandeling de hele BCL in. De maatstaf is de assembly: alles uit <c>Soratus.Portal</c> gaat
    /// mee, de rest niet.
    /// </remarks>
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
    /// De eigen typen in deze typeverwijzing: het type zelf, of dat waar een lijst of een
    /// nullable om heen staat.
    /// </summary>
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

        if (kern.Assembly == typeof(CustomerHours).Assembly && !kern.IsEnum)
        {
            yield return kern;
        }
    }
}
