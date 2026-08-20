using System.Reflection;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Soratus.Portal.Components.Pages.Klant;
using Soratus.Portal.Components.Shared;
using Soratus.Portal.Data;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Zichtbaarheid;

/// <summary>
/// Of de drie formulieren van het urenscherm werkelijk aankomen: de namen in de markup tegenover de
/// modellen die ze binden.
/// </summary>
/// <remarks>
/// <para><strong>Dit is de stap die bUnit niet kan doen, en daarom de stap die apart moet worden
/// vastgelegd.</strong> Op static SSR bindt Blazor een formulier met de naam van de
/// <c>[SupplyParameterFromForm]</c>-eigenschap als voorvoegsel van elk <c>name</c>-attribuut, en met
/// de <c>FormName</c> om te bepalen wélk model er gebonden wordt. Klopt een van die twee niet, dan
/// komt de invoer nooit aan en verdwijnt hij stil — het formulier lijkt te werken en legt de
/// standaardwaarden vast.</para>
///
/// <para>Dat is geen theoretische fout. In dit werk stond er eerst één hulpmethode die het
/// voorvoegsel opzocht aan de hand van de veldnaam, en omdat de drie formulieren veldnamen delen
/// (<c>Month</c>, <c>Hours</c>, <c>By</c>, <c>Note</c>) kwam elk veld van het correctieformulier aan
/// als een veld van het boekformulier. Er was geen test die dat zag; deze wel.</para>
/// </remarks>
public class UrenformulierbindingTests : Portaalrendertest
{
    private static Type Urenpagina =>
        Paginaverzameling.MetRoute("/klant/{Slug}/uren")
        ?? throw new InvalidOperationException(
            "Er staat geen pagina op route '/klant/{Slug}/uren'.");

    /// <summary>De formuliermodellen van de urenpagina: de eigenschap en de naam van zijn formulier.</summary>
    private static IReadOnlyList<(PropertyInfo Property, string FormName)> Modellen =>
    [
        .. Urenpagina
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(p => (Property: p, Attribuut: p.GetCustomAttribute<SupplyParameterFromFormAttribute>()))
            .Where(x => x.Attribuut is not null)
            .Select(x => (x.Property, FormName: x.Attribuut!.FormName ?? string.Empty)),
    ];

    [Fact]
    public void ErStaanDrieFormuliermodellenOpDePagina()
    {
        // Zonder deze test kan alles hieronder over een lege verzameling lopen en toch groen zijn.
        // Drie: boeken, corrigeren, beoordelen.
        Assert.Equal(3, Modellen.Count);
    }

    [Fact]
    public void ElkFormulierHeeftEenEigenNaam()
    {
        // Zonder een naam per formulier bindt Blazor élk model uit élke POST — dat staat letterlijk
        // in de documentatie van SupplyParameterFromFormAttribute.FormName: "If not specified, the
        // value will be mapped from any incoming form post within the current form mapping scope."
        // Met drie formulieren op één pagina zou een boeking dus ook het correctieformulier vullen.
        var namen = Modellen.Select(m => m.FormName).ToArray();

        Assert.DoesNotContain(string.Empty, namen);
        Assert.Equal(namen.Length, namen.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void DeFormuliernamenInDeMarkupZijnPreciesDieVanDeModellen()
    {
        // De koppeling tussen het attribuut op de eigenschap en de FormName op de kaart. Beide
        // verwijzen naar dezelfde constante, maar dat is een eigenschap van de broncode en niet van
        // het gerenderde scherm — en het is precies de plek waar een verschrijving een formulier stil
        // niets laat doen.
        MeldOperatorAan();

        var opDeKaarten = Kaarten("?" + Beoordeling())
            .Select(kaart => kaart.FormName)

            // OfType en niet Where(… is not null): dat laatste laat het statische type string?
            // staan, en dan vergelijkt Assert.Equal een HashSet<string?> met een HashSet<string>
            // (CS8620). Dezelfde filtering, maar het type klopt erna.
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            Modellen.Select(m => m.FormName).ToHashSet(StringComparer.Ordinal),
            opDeKaarten);
    }

    [Fact]
    public void ElkVeldDraagtHetVoorvoegselVanZijnEigenModel()
    {
        // De kern. Voor elk formuliermodel en elk veld erop moet er een name-attribuut in de markup
        // staan met precies dat voorvoegsel. Een veld dat onder een ander voorvoegsel staat komt aan
        // bij het verkeerde model, en een veld dat helemaal ontbreekt komt nergens aan.
        MeldOperatorAan();

        var markup = Markup(null) + Markup(Beoordeling());

        var ontbreekt = new List<string>();

        foreach (var (property, _) in Modellen)
        {
            foreach (var veld in Velden(property.PropertyType))
            {
                var naam = $"name=\"{property.Name}.{veld}\"";

                if (!markup.Contains(naam, StringComparison.Ordinal))
                {
                    ontbreekt.Add(naam);
                }
            }
        }

        Assert.True(
            ontbreekt.Count == 0,
            "Deze velden van een formuliermodel staan met dat voorvoegsel niet in de markup:\n" +
            string.Join("\n", ontbreekt) + "\n\n" +
            "Model binding op static SSR bindt met de naam van de [SupplyParameterFromForm]-" +
            "eigenschap als voorvoegsel. Staat er een ander voorvoegsel, dan komt het veld aan bij " +
            "het verkeerde model; staat het er niet, dan verdwijnt de invoer stil bij het versturen. " +
            "Gebruik BookField, CorrectField of JudgeField in Uren.razor — nooit een eigen " +
            "tekenreeks.");
    }

    [Fact]
    public void GeenTweeFormulierenDelenEenVeldnaam()
    {
        // De spiegel van de test hierboven, en de fout die er werkelijk zat: de drie modellen delen
        // veldnamen, dus als er ergens één voorvoegsel voor alle drie wordt gebruikt, blijft de test
        // hierboven groen — hij vindt name="Boeking.Month" en die staat er dan twee keer. Deze test
        // telt daarom hoe vaak elk name-attribuut voorkomt: één keer per veld en niet twee.
        MeldOperatorAan();

        var markup = Markup(null);
        var dubbel = new List<string>();

        foreach (var (property, _) in Modellen)
        {
            foreach (var veld in Velden(property.PropertyType))
            {
                var naam = $"name=\"{property.Name}.{veld}\"";
                var aantal = Voorkomens(markup, naam);

                if (aantal > 1)
                {
                    dubbel.Add($"{naam} — {aantal} keer");
                }
            }
        }

        Assert.True(
            dubbel.Count == 0,
            "Deze name-attributen staan meer dan één keer op het scherm:\n" +
            string.Join("\n", dubbel) + "\n\n" +
            "Twee velden met dezelfde naam in twee formulieren is niet meteen fout — elk formulier " +
            "bindt apart — maar binnen één formulier is het dat wel, en het is het teken dat er één " +
            "voorvoegsel voor meerdere modellen wordt gebruikt.");
    }

    [Fact]
    public void HetBoekformulierBiedtGeenMaandAanDieDeSchrijfkantWeigert()
    {
        // De keuzelijst komt uit OperatorHoursView.BookableMonths en dus uit de datalaag. Zou het
        // scherm zijn eigen maanden verzinnen, dan is een boeking op een maand die niet in de tabel
        // staat een boeking die niemand terugvindt.
        MeldOperatorAan();

        var cut = Render(null);
        var opties = cut
            .FindAll("select[name='Boeking.Month'] option")
            .Select(optie => optie.GetAttribute("value") ?? string.Empty)
            .ToArray();

        Assert.NotEmpty(opties);
        Assert.All(opties, maand => Assert.Null(HourMonths.Validate(maand)));
    }

    [Fact]
    public void HetBoekformulierBiedtDeCategorieCorrectieNietAan()
    {
        // Besluit 16: een correctie is een eigen aanroep met een eigen formulier. Zou hij in deze
        // lijst staan, dan is een correctie niet meer van een boeking te onderscheiden.
        MeldOperatorAan();

        var opties = Render(null)
            .FindAll("select[name='Boeking.Category'] option")
            .Select(optie => optie.GetAttribute("value") ?? string.Empty)
            .ToArray();

        Assert.NotEmpty(opties);
        Assert.DoesNotContain(HourCategories.Correction, opties);
    }

    // ── Gereedschap ─────────────────────────────────────────────────────────────────────────────

    /// <summary>De querystring die de beoordelingskaart in beeld brengt.</summary>
    private string Beoordeling()
    {
        var wachtend = Opslag.Urenregels().First(regel => regel.Status == HourEntryStatus.Pending);

        return $"maand={wachtend.Month}&beoordeel={wachtend.Id}&actie={HourText.RejectAction}";
    }

    private IRenderedComponent<Bunit.Rendering.ContainerFragment> Render(string? query)
    {
        Services.GetRequiredService<NavigationManager>()
            .NavigateTo($"/klant/{EigenKlant}/uren" + (query is null ? string.Empty : "?" + query));

        return RenderPagina(Urenpagina);
    }

    private string Markup(string? query) => Render(query).Markup;

    private IReadOnlyList<FormCard> Kaarten(string query) =>
        [.. Render(query.TrimStart('?')).FindComponents<FormCard>().Select(k => k.Instance)];

    /// <summary>De velden van een formuliermodel: elke beschrijfbare eigenschap.</summary>
    private static IEnumerable<string> Velden(Type model) =>
        model
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(p => p.CanWrite)
            .Select(p => p.Name);

    private static int Voorkomens(string tekst, string naald)
    {
        var aantal = 0;
        var index = 0;

        while ((index = tekst.IndexOf(naald, index, StringComparison.Ordinal)) >= 0)
        {
            aantal++;
            index += naald.Length;
        }

        return aantal;
    }
}
