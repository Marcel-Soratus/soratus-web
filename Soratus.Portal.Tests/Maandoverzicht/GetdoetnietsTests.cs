using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Soratus.Portal.Mail;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Maandoverzicht;

/// <summary>
/// Dat geen enkele queryparameter van deze kaart iets doet.
/// </summary>
/// <remarks>
/// <para><strong>Waarom deze tests bestaan.</strong> De kaart heeft twee queryparameters, <c>?jaar=</c>
/// en <c>?vaststellen=</c>, en die tweede is een werkwoord. Een handeling in een <c>GET</c> is aan te
/// roepen door een link in een mail, door een prefetch van een browser, door een linkchecker, door een
/// spamfilter dat elke URL in een bericht opent, en door een tabblad dat na een herstart zijn adressen
/// opnieuw bezoekt. Bij een gewoon scherm is dat hinderlijk. Hier zou het de deur openzetten naar een
/// tweede maandoverzicht, want <em>vaststellen dat er niets is aangekomen</em> is precies de handeling
/// die opnieuw versturen toestaat.</para>
///
/// <para><strong>Hoe het ligt:</strong> <c>?vaststellen=</c> is uitsluitend een <em>keuze in het
/// scherm</em>. Hij bepaalt vóór welke maand het formulier wordt opgemaakt en verder niets; de
/// vaststelling zelf is een <c>POST</c> van dat formulier. Dat is geen bewering maar wat hieronder
/// wordt gemeten: na het renderen van het adres staat de bevestiging nog op precies dezelfde stand,
/// zonder vaststelling, en is er niets verstuurd.</para>
///
/// <para><strong>Wat deze tests níet meten, en dat hoort erbij.</strong> bUnit rendert een
/// <c>EditForm</c> als <c>&lt;form blazor:onsubmit="1"&gt;</c> en niet als <c>&lt;form
/// method="post"&gt;</c> met een antiforgery-token — dat is de renderer van bUnit en niet die van
/// static SSR. Gemeten, niet aangenomen: het staat in de opgemaakte markup. Dat de <c>POST</c>
/// werkelijk een <c>POST</c> met een token is, volgt hier dus uit de vorm (<c>EditForm</c> met
/// <c>FormName</c>, dezelfde vorm als de drie formulieren op het urenscherm) en niet uit een meting.
/// Die meting kan pas als deze kaart op een pagina met een route staat en er een echte host
/// omheen kan.</para>
/// </remarks>
public class GetdoetnietsTests : BunitContext
{
    private const string Adres = "http://localhost/klant/acme-logistiek/facturatie";

    private bool _geregistreerd;

    [Fact]
    public async Task EenGetMetVaststellenVerandertNietsInDeOpslag()
    {
        var bank = await MetOnbekendeUitkomst();
        var voor = bank.Bevestigingen.Document(Maandoverzichtbank.AfgeslotenMaand)!;

        await RenderAsync(bank, $"{Adres}?vaststellen={Maandoverzichtbank.AfgeslotenMaand}");

        var na = bank.Bevestigingen.Document(Maandoverzichtbank.AfgeslotenMaand)!;

        // Dezelfde stand, geen vaststelling, geen tweede poging. Als het bezoeken van dit adres iets
        // zou doen, dan zou juist dít document veranderen — het is het enige dat er staat.
        Assert.Equal(StatementSendState.Unknown, na.State);
        Assert.Null(na.ReleaseNote);
        Assert.Null(na.ReleasedAt);
        Assert.Null(na.ReleasedBy);
        Assert.Equal(voor.Attempts, na.Attempts);
        Assert.Equal(voor.ETag, na.ETag);
    }

    [Fact]
    public async Task GeenEnkeleQueryparameterVerstuurtEenMail()
    {
        var bank = new Maandoverzichtbank();

        foreach (var adres in new[]
        {
            Adres,
            $"{Adres}?jaar=2026",
            $"{Adres}?vaststellen={Maandoverzichtbank.AfgeslotenMaand}",
            $"{Adres}?vaststellen={Maandoverzichtbank.AfgeslotenMaand}&jaar=2026",
        })
        {
            await RenderAsync(bank, adres);
        }

        Assert.Empty(bank.Verzender.Verstuurd);
        Assert.Equal(0, bank.Bevestigingen.Claims);
    }

    [Fact]
    public async Task EenGetMetVaststellenRendertEenFormulierEnGeenUitkomst()
    {
        var bank = await MetOnbekendeUitkomst();

        var markup = (await RenderAsync(
            bank,
            $"{Adres}?vaststellen={Maandoverzichtbank.AfgeslotenMaand}")).Markup;

        // Er staat een formulier met de maand en de etag als verborgen velden, en een knop die
        // ingediend moet worden. Het formulier is de handeling; het adres is de keuze.
        Assert.Contains("name=\"Vaststelling.Month\" value=\"2026-07\"", markup, StringComparison.Ordinal);
        Assert.Contains("name=\"Vaststelling.ETag\"", markup, StringComparison.Ordinal);
        Assert.Contains("type=\"submit\"", markup, StringComparison.Ordinal);

        // En de stand op het scherm is nog onveranderd: er staat geen "niet verstuurd" en geen
        // vaststelling. Zonder deze twee regels zou de test hierboven groen blijven terwijl het
        // scherm al doet alsof er iets is gebeurd.
        Assert.Contains("Onbekend", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Vastgesteld door", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EenGedeeldeLinkNaarEenMaandDieNietMagLevertGeenFormulier()
    {
        // Een verstuurd overzicht is niet achteraf onverzonden te verklaren. Een link naar zo'n maand
        // hoort dus niets op te leveren — geen formulier en geen melding dat het niet mag, want
        // zonder de link is er niets aan de hand.
        var bank = new Maandoverzichtbank();
        await bank.Dienst.SendAsync(await bank.SchrijfrechtAsync(), Maandoverzichtbank.AfgeslotenMaand);

        var markup = (await RenderAsync(
            bank,
            $"{Adres}?vaststellen={Maandoverzichtbank.AfgeslotenMaand}")).Markup;

        Assert.DoesNotContain("Vaststelling.Month", markup, StringComparison.Ordinal);
        Assert.Contains("Verstuurd", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EenVerzonnenMaandInHetAdresLevertNiets()
    {
        var bank = await MetOnbekendeUitkomst();

        var markup = (await RenderAsync(bank, $"{Adres}?vaststellen=augustus")).Markup;

        Assert.DoesNotContain("Vaststelling.Month", markup, StringComparison.Ordinal);
        Assert.Equal(
            StatementSendState.Unknown,
            bank.Bevestigingen.Document(Maandoverzichtbank.AfgeslotenMaand)!.State);
    }

    private static async Task<Maandoverzichtbank> MetOnbekendeUitkomst()
    {
        var bank = new Maandoverzichtbank();
        bank.Verzender.Uitkomst = MailDelivery.Unknown;

        await bank.Dienst.SendAsync(
            await bank.SchrijfrechtAsync(),
            Maandoverzichtbank.AfgeslotenMaand);

        return bank;
    }

    private async Task<IRenderedComponent<MonthlyStatementCard>> RenderAsync(
        Maandoverzichtbank bank,
        string adres)
    {
        // Een vlag en geen GetService-controle. Dat laatste stond er eerst en viel om: het opvragen
        // van een dienst initialiseert de provider van bUnit, en daarna is registreren verboden. De
        // melding ("New services cannot be registered … after the first services has been retrieved")
        // wees precies de verkeerde kant op — hij klinkt als een renderprobleem en het was de
        // controle zelf.
        if (!_geregistreerd)
        {
            _geregistreerd = true;

            Services.AddSingleton<IStatementViews>(new StatementViews(
                bank.Bevestigingen,
                Options.Create(bank.Opties),
                new Stilstaandeklok(Testgegevens.Nu)));
            Services.AddSingleton(bank.Dienst);
            Services.AddSingleton<IStatementStore>(bank.Bevestigingen);
        }

        Services.GetRequiredService<BunitNavigationManager>().NavigateTo(adres);

        var scope = await bank.SchrijfrechtAsync();

        return Render<MonthlyStatementCard>(parameters => parameters.Add(
            kaart => kaart.Scope,
            scope));
    }
}
