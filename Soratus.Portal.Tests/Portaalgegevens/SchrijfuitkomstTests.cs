using System.Reflection;
using Soratus.Portal.Data;

namespace Soratus.Portal.Tests.Portaalgegevens;

/// <summary>
/// Wat de operator ziet als er iets misgaat bij het opslaan — en dat dat klopt met wat er is
/// gebeurd.
/// </summary>
/// <remarks>
/// <para>Twee operators met dezelfde contractkaart open is geen storing maar gewoon gebruik. De
/// gelijktijdigheidscontrole loopt over <c>_etag</c>: de versie die op het scherm stond gaat als
/// <c>If-Match</c> mee, en wie op een verouderde versie werkt krijgt geen stille overschrijving maar
/// een conflict. Deze tests staan op de <em>uitkomst</em> van die controle, want dat is het deel dat
/// de gebruiker ziet en het deel dat zonder Cosmos te meten is.</para>
///
/// <para>Het belangrijkste hier is niet dat een conflict een conflict heet. Het is dat het huidige
/// document meekomt. Zonder die waarde kan de operator alleen zijn eigen invoer nogmaals versturen,
/// en dan wint de laatste schrijver alsnog — precies het stille overschrijven dat de etag hoort te
/// voorkomen.</para>
/// </remarks>
public class SchrijfuitkomstTests
{
    // ── De drie uitkomsten ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void EenGeslaagdeSchrijfactieDraagtHetNieuweDocumentEnGeenMelding()
    {
        var resultaat = PortalWriteResult<ContractDocument>.Saved(Contract("\"0x8DC2\""));

        Assert.Equal(PortalWriteStatus.Saved, resultaat.Status);
        Assert.True(resultaat.IsSaved);
        Assert.NotNull(resultaat.Value);
        Assert.Null(resultaat.Message);

        // Geen "huidig document" bij succes: dat veld hoort bij een conflict, en een gevulde waarde
        // zou een scherm laten tonen dat er iets te vergelijken valt.
        Assert.Null(resultaat.Current);
    }

    [Fact]
    public void EenAfgekeurdeInvoerDraagtEenMeldingEnGeenDocument()
    {
        var resultaat = PortalWriteResult<ContractDocument>.Invalid("De ingangsdatum hoort jjjj-mm-dd te zijn.");

        Assert.Equal(PortalWriteStatus.Invalid, resultaat.Status);
        Assert.False(resultaat.IsSaved);
        Assert.Null(resultaat.Value);
        Assert.Null(resultaat.Current);
        Assert.False(string.IsNullOrWhiteSpace(resultaat.Message));
    }

    [Fact]
    public void EenConflictDraagtHetHuidigeDocumentZodatHetSchermKanTonenWatErVeranderde()
    {
        var huidig = Contract("\"0x8DC9\"");
        var resultaat = PortalWriteResult<ContractDocument>.Conflict("Iemand anders was eerder.", huidig);

        Assert.Equal(PortalWriteStatus.Conflict, resultaat.Status);
        Assert.False(resultaat.IsSaved);
        Assert.Null(resultaat.Value);
        Assert.Same(huidig, resultaat.Current);
        Assert.False(string.IsNullOrWhiteSpace(resultaat.Message));
    }

    [Fact]
    public void EenConflictOpEenDocumentDatIsVerwijderdDraagtNiets()
    {
        // Het andere geval: er was een etag, dus er stond een document, en nu is het weg. Dan valt
        // er niets te vergelijken en hoort de melding dat te zeggen in plaats van een leeg formulier
        // te tonen.
        var resultaat = PortalWriteResult<AccessDocument>.Conflict(
            "Deze toegang bestaat niet meer.",
            current: null);

        Assert.Equal(PortalWriteStatus.Conflict, resultaat.Status);
        Assert.Null(resultaat.Current);
        Assert.False(string.IsNullOrWhiteSpace(resultaat.Message));
    }

    [Fact]
    public void EenMislukteSchrijfactieIsNooitAlsGeslaagdTeLezen()
    {
        // IsSaved is de vraag die het scherm stelt. Zou die op iets anders dan Saved true worden,
        // dan gaat een formulier door na een conflict en is de wijziging verdwenen zonder melding.
        Assert.False(PortalWriteResult<ContractDocument>.Invalid("x").IsSaved);
        Assert.False(PortalWriteResult<ContractDocument>.Conflict("x", Contract("\"1\"")).IsSaved);
        Assert.False(PortalWriteResult<ContractDocument>.Conflict("x", current: null).IsSaved);
        Assert.True(PortalWriteResult<ContractDocument>.Saved(Contract("\"1\"")).IsSaved);
    }

    [Fact]
    public void EenResultaatIsAlleenViaDeDrieFabriekenTeMaken()
    {
        // Saved is de eerste waarde van de enum en dus de default(PortalWriteStatus). Dat is alleen
        // ongevaarlijk zolang er geen pad bestaat dat een resultaat maakt zonder een status te
        // kiezen: de constructor is privé en de drie fabrieken zetten hem allemaal. Komt er een
        // vierde manier bij, dan hoort iemand hier langs te komen.
        var fabrieken = typeof(PortalWriteResult<ContractDocument>)
            .GetMethods(BindingFlags.Static | BindingFlags.Public)
            .Where(m => m.ReturnType == typeof(PortalWriteResult<ContractDocument>))
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["Conflict", "Invalid", "Saved"], fabrieken);
        Assert.Empty(typeof(PortalWriteResult<ContractDocument>)
            .GetConstructors(BindingFlags.Instance | BindingFlags.Public));
    }

    // ── De etag is een schrijfvoorwaarde en geen gegeven ────────────────────────────────────────

    [Fact]
    public void ElkeBewerkingDraagtDeEtagWaaropHijIsGebaseerd()
    {
        // Zonder dit veld is er geen gelijktijdigheidscontrole mogelijk: de opslag zou dan de versie
        // die er staat met zichzelf vergelijken en elke schrijfactie slagen.
        Assert.NotNull(typeof(ContractEdit).GetProperty("BasedOnETag"));
        Assert.NotNull(typeof(CustomerEdit).GetProperty("BasedOnETag"));
    }

    [Fact]
    public void HetIntrekkenVanToegangNeemtDeEtagVanDeRij()
    {
        // Een toegangsregel heeft geen bewerkingsformulier — hij bestaat of hij bestaat niet — dus
        // de etag komt hier als losse parameter mee in plaats van in een edit-record.
        var methode = typeof(IPortalDataStore).GetMethod(nameof(IPortalDataStore.RevokeAccessAsync));

        Assert.NotNull(methode);
        Assert.Contains(
            "basedOnETag",
            methode.GetParameters().Select(p => p.Name),
            StringComparer.Ordinal);
    }

    [Fact]
    public void DeEtagKomtVanDeAanroeperEnNietVanEenVerseLezing()
    {
        // Dit is het verschil tussen een controle en een schijncontrole, en het is aan de signatuur
        // te zien: zou de opslag de etag zelf ophalen, dan zou de aanroeper hem niet hoeven mee te
        // geven. Elke schrijfmethode die een bestaand document raakt neemt hem dus mee — van het
        // formulier dat de operator open had staan.
        var schrijvend = typeof(IPortalDataStore)
            .GetMethods()
            .Where(m => m.Name is nameof(IPortalDataStore.SaveContractAsync)
                or nameof(IPortalDataStore.SaveCustomerAsync)
                or nameof(IPortalDataStore.RevokeAccessAsync))
            .ToArray();

        Assert.Equal(3, schrijvend.Length);

        foreach (var methode in schrijvend)
        {
            var draagtEtag = methode.GetParameters().Any(p =>
                p.Name?.Contains("ETag", StringComparison.OrdinalIgnoreCase) == true
                || p.ParameterType.GetProperty("BasedOnETag") is not null);

            Assert.True(
                draagtEtag,
                $"IPortalDataStore.{methode.Name} krijgt geen etag mee. Dan kan de opslag alleen " +
                "zijn eigen huidige versie als voorwaarde gebruiken, en dat is de opslag met " +
                "zichzelf vergelijken: elke schrijfactie slaagt en twee operators overschrijven " +
                "elkaar stil.");
        }
    }

    [Fact]
    public void EenKlantAanmakenKentGeenEtagWantErIsNogNiets()
    {
        // De tegenhanger, zodat de test hierboven geen "elke methode moet een etag" wordt. Bij
        // aanmaken is de voorwaarde dat er nog níets staat; dat is een CreateItem en die heeft geen
        // versie om zich op te baseren.
        var methode = typeof(IPortalDataStore).GetMethod(nameof(IPortalDataStore.CreateCustomerAsync));

        Assert.NotNull(methode);
        Assert.DoesNotContain(
            "etag",
            methode.GetParameters().Select(p => p.Name!.ToLowerInvariant()),
            StringComparer.Ordinal);
        Assert.Null(typeof(NewCustomerRequest).GetProperty("BasedOnETag"));
    }

    [Fact]
    public void HetContractdocumentDraagtDeEtagUitDeOpslagEnZetHemZelfNooit()
    {
        // Cosmos vult _etag. Zouden wij hem zetten, dan zou de controle op onze waarde lopen in
        // plaats van op de versie die de opslag bijhoudt.
        Assert.Equal("_etag", Jsonnaam(typeof(ContractDocument), "ETag"));
        Assert.Equal("_etag", Jsonnaam(typeof(CustomerDocument), "ETag"));
        Assert.Equal("_etag", Jsonnaam(typeof(AccessDocument), "ETag"));
    }

    [Fact]
    public void DeOpslagIsNietBereikbaarIsGeenFormuliermeldingMaarEenStoring()
    {
        // Een verouderde etag en een ongeldig adres horen bij het normale gebruik van een formulier
        // en leveren een resultaat. Een opslag die niet bereikbaar is of waar het schrijfrecht
        // ontbreekt is een inrichtingsfout, en die hoort luidruchtig te zijn in plaats van als
        // nette melding op een scherm te eindigen.
        Assert.True(typeof(Exception).IsAssignableFrom(typeof(PortalDataNotProvisionedException)));
        Assert.Null(typeof(PortalWriteStatus).GetField("Unavailable"));
        Assert.Null(typeof(PortalWriteStatus).GetField("Error"));
    }

    private static string? Jsonnaam(Type type, string property) =>
        type.GetProperty(property)
            ?.GetCustomAttributes<System.Text.Json.Serialization.JsonPropertyNameAttribute>()
            .SingleOrDefault()
            ?.Name;

    private static ContractDocument Contract(string etag) => new()
    {
        Id = PortalDocumentIds.Contract,
        PartitionKey = "bakker",
        CustomerId = "bakker",
        Number = "SOR-2026-003",
        ETag = etag,
    };
}
