using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Soratus.Portal.Mail;
using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Maandoverzicht;

/// <summary>
/// De kaart zoals hij op het scherm staat.
/// </summary>
/// <remarks>
/// <para>Een eigen <see cref="BunitContext"/> en niet de <c>Portaalrendertest</c> uit
/// <c>Zichtbaarheid</c>: die basisklasse hoort bij pagina's met een route en wordt door twee andere
/// sessies bewerkt. Deze kaart heeft geen route — hij hangt onder de facturatiepagina — dus hij zou
/// daar toch een eigen opzet nodig hebben.</para>
///
/// <para><strong>Wat deze tests toevoegen boven de typecontroles:</strong> dat de kaart überhaupt
/// rendert. Een component dat compileert en waarvan de parametervorm klopt, kan nog altijd bij het
/// eerste verzoek omvallen op een ontbrekende registratie of een verkeerde formuliernaam. Dat is in
/// dit project al eerder gebeurd, en het valt dan pas op nadat het scherm er staat.</para>
/// </remarks>
public class MaandoverzichtkaartTests : BunitContext
{
    [Fact]
    public async Task DeKaartRendertEnZegtDatHijInProefdraaimodusStaat()
    {
        var markup = (await RenderAsync(droog: true)).Markup;

        Assert.Contains("Maandoverzicht mailen", markup, StringComparison.Ordinal);
        Assert.Contains("PROEFDRAAI", markup, StringComparison.Ordinal);
        Assert.Contains("Proefdraaien", markup, StringComparison.Ordinal);

        // De afgesloten maand staat in de keuzelijst en de lopende niet.
        Assert.Contains("juli 2026", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("augustus 2026", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZonderIngerichteMailStaatErGeenKnop()
    {
        var markup = (await RenderAsync(ingericht: false)).Markup;

        Assert.Contains("Mailen is niet ingericht", markup, StringComparison.Ordinal);

        // Geen knop die suggereert dat je kunt versturen zolang dat niet kan (ontwerpregel §1).
        Assert.DoesNotContain("Versturen", markup, StringComparison.Ordinal);
        Assert.DoesNotContain("Proefdraaien", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EenLegeGeschiedenisZegtDatErNooitEenPogingIsGedaan()
    {
        var markup = (await RenderAsync()).Markup;

        Assert.Contains("er is nooit een poging gedaan", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EenOnbekendeUitkomstStaatMetLabelEnGlyphOpHetScherm()
    {
        var bank = new Maandoverzichtbank();
        bank.Verzender.Uitkomst = MailDelivery.Unknown;

        await bank.Dienst.SendAsync(
            await bank.SchrijfrechtAsync(),
            Maandoverzichtbank.AfgeslotenMaand);

        var markup = (await RenderAsync(bank)).Markup;

        // Nooit kleur zonder woordlabel (§8), en de glyph erbij.
        Assert.Contains("Onbekend", markup, StringComparison.Ordinal);
        Assert.Contains("badge--degraded", markup, StringComparison.Ordinal);
        Assert.Contains("◐", markup, StringComparison.Ordinal);

        // En de uitleg dat het portaal het niet opnieuw probeert.
        Assert.Contains("probeert dit niet opnieuw", markup, StringComparison.Ordinal);

        // De vaststelling is een tweede adres en geen tweede knop op dezelfde plek; zie de
        // toelichting in de kaart over static SSR.
        Assert.Contains("vaststellen=2026-07", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EenVerstuurdOverzichtToontDeOperatieEnHetBedragUitDeMail()
    {
        var bank = new Maandoverzichtbank();

        await bank.Dienst.SendAsync(
            await bank.SchrijfrechtAsync(),
            Maandoverzichtbank.AfgeslotenMaand);

        var markup = (await RenderAsync(bank)).Markup;

        Assert.Contains("Verstuurd", markup, StringComparison.Ordinal);
        Assert.Contains("badge--live", markup, StringComparison.Ordinal);
        Assert.Contains("operatie-0001", markup, StringComparison.Ordinal);
        Assert.Contains("286,79", markup, StringComparison.Ordinal);
        Assert.Contains(Vasteportaalopslag.Beheerderadres, markup, StringComparison.Ordinal);

        // Er staat geen knop om nogmaals te versturen op een maand die al is verstuurd.
        Assert.DoesNotContain("vaststellen=2026-07", markup, StringComparison.Ordinal);
    }

    private async Task<IRenderedComponent<MonthlyStatementCard>> RenderAsync(
        bool droog = true,
        bool ingericht = true)
    {
        var opties = ingericht
            ? Maandoverzichtbank.Ingericht()
            : new PortalMailOptions { DryRun = droog };

        opties.DryRun = droog;

        return await RenderAsync(new Maandoverzichtbank(opties: opties));
    }

    private async Task<IRenderedComponent<MonthlyStatementCard>> RenderAsync(Maandoverzichtbank bank)
    {
        Services.AddSingleton<IStatementViews>(new StatementViews(
            bank.Bevestigingen,
            Options.Create(bank.Opties),
            new Stilstaandeklok(Testgegevens.Nu)));
        Services.AddSingleton(bank.Dienst);
        Services.AddSingleton<IStatementStore>(bank.Bevestigingen);

        var scope = await bank.SchrijfrechtAsync();

        return Render<MonthlyStatementCard>(parameters => parameters.Add(
            kaart => kaart.Scope,
            scope));
    }
}
