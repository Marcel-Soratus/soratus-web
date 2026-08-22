using Soratus.Portal.Tests.Hulpmiddelen;

namespace Soratus.Portal.Tests.Portaalgegevens;

/// <summary>
/// Dat het omgevingsblok een interactief eiland blijft, want dát is wat de twee scopevelden beschermt.
/// </summary>
/// <remarks>
/// <para><strong>Dit is de vreemdste test van deze lane en hij bewaakt het echte gat.</strong>
/// <c>IPortalDataStore.SaveCustomerAsync</c> vervangt het héle klantdocument, dus een veld dat het
/// formulier niet meestuurt wordt bij het bewaren leeggemaakt. Voor een veld dat het formulier wél draagt
/// is dat opgelost — <c>ContracteilandTests</c> en <c>KlantdocumentveldenTests</c> meten dat. Maar er is een
/// tweede weg naar hetzelfde gevolg, en die zit niet in de code maar in de <em>rendermode</em>.</para>
///
/// <para>Het scenario: een operator heeft het contractscherm open, er wordt uitgerold, en daarna drukt hij
/// op Bewaren. Bij een static-SSR-formulier gaat er dan een POST de deur uit die het nieuwe veld niet
/// bevat, het model bindt <c>null</c>, en <c>null</c> betekent hier "wissen" — precies de vorm die dit
/// portaal een keer heeft platgelegd met een lege app-setting die als <c>""</c> bond in plaats van als
/// afwezig. Bij een <c>InteractiveServer</c>-eiland kan dat niet: de formulierstaat leeft in een circuit,
/// een uitrol beëindigt dat circuit, en de gebruiker krijgt de reconnect-modal in plaats van een POST met
/// een ontbrekend veld.</para>
///
/// <para><strong>De bescherming zit dus in de rendermode en niet in de mapping, en dat is fragiel.</strong>
/// Dat eiland omzetten naar static SSR is een redelijke wijziging — bijvoorbeeld om een render te
/// versnellen — en het gevolg zou stil zijn: twee scopevelden weg, geen fout, geen rode test, en op het
/// scherm de tekst "geen bord vastgelegd" die met opzet is geschreven om waar te zijn. Deze test maakt van
/// dat gevolg een beslissing die iemand tegenkomt.</para>
///
/// <para><strong>En dit is geen nieuwe schuld van de sprintlane.</strong> <c>AzureScope</c> stond al onder
/// dezelfde bescherming en niemand had het opgeschreven; het bord van §3.4 is de tweede die eronder valt,
/// en dat maakte het zichtbaar. Dat verschil telt bij het lezen: dit is een vondst en geen gevolg van dit
/// werk.</para>
///
/// <para>Een broncodetest en niet een render, en dat is een grens en geen gemak: een rendermode is geen
/// eigenschap van een gerenderde boom die bUnit teruggeeft — bUnit rendert alles in één proces en negeert
/// de rendermode met opzet. Wat er te lezen valt is de aanroep in <c>Contract.razor</c>, en dat is precies
/// de regel die iemand zou wijzigen.</para>
/// </remarks>
public class OmgevingsblokrendermodeTests
{
    /// <summary>Het bestand met de aanroep van het omgevingsblok.</summary>
    private const string Bestand = "Components/Pages/Klant/Contract.razor";

    /// <summary>De naam van het component dat interactief moet blijven.</summary>
    private const string Component = "ContractPanel";

    [Fact]
    public void HetContractschermRendertHetOmgevingsblokInteractief()
    {
        var regel = Aanroep();

        Assert.True(
            regel.Contains("@rendermode=\"InteractiveServer\"", StringComparison.Ordinal)
            || regel.Contains("@rendermode=\"RenderMode.InteractiveServer\"", StringComparison.Ordinal),
            $"De aanroep van {Component} in {Bestand} rendert niet meer interactief:\n\n    {regel}\n\n"
            + "Dat is geen prestatiekeuze maar de enige bescherming tegen het stil wissen van "
            + "CustomerDocument.AzureScope en CustomerDocument.DevOpsScope.\n\n"
            + "SaveCustomerAsync vervangt het héle klantdocument. Bij een static-SSR-formulier gaat er na "
            + "een uitrol een POST de deur uit van een pagina die vóór die uitrol is gerenderd; die POST "
            + "bevat een veld dat toen nog niet bestond niet, het model bindt null, en null betekent hier "
            + "'wissen'. Het gevolg is onzichtbaar: de kostenmeting en de sprintweergave van die klant "
            + "staan uit, en beide schermen melden netjes dat er niets is ingericht.\n\n"
            + "Bij een InteractiveServer-eiland kan dat niet: de formulierstaat leeft in een circuit, een "
            + "uitrol beëindigt dat circuit, en de gebruiker krijgt de reconnect-modal.\n\n"
            + "Wil je hier toch static SSR, dan hoort eerst het onderliggende gat dicht: laat "
            + "SaveCustomerAsync een veld dat de bewerking niet draagt met rust in plaats van het te "
            + "wissen, of geef de bewerking een expliciet onderscheid tussen 'leeg' en 'niet meegestuurd'.");
    }

    [Fact]
    public void DeAanroepVanHetOmgevingsblokIsTeVindenEnStaatEenKeerInDeBoom()
    {
        // De onmisbare tegenhanger van elke broncodetest: die kijkt of er iets in een tekst staat, en dat
        // is alleen iets waard als de tekst wordt gevonden. Zou het component worden hernoemd of verhuisd,
        // dan valt deze test met een melding die dat zegt in plaats van dat de test hierboven stil groen
        // blijft op een bestand dat niet bestaat.
        //
        // En één keer: staat het blok op twee plekken, dan dekt de test hierboven er één en is de andere
        // vrij.
        var pad = Path.Combine(Broncode.Portaalproject.FullName, Bestand.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(pad), $"{Bestand} is niet gevonden. Verhuist dat bestand, dan hoort deze test mee.");

        var aanroepen = File.ReadAllLines(pad)
            .Where(regel => regel.Contains($"<{Component}", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(aanroepen);
    }

    /// <summary>De regel met de aanroep van het omgevingsblok.</summary>
    /// <returns>De regel, inclusief de attributen erachter.</returns>
    /// <remarks>
    /// De hele regel en niet alleen de componentnaam: de rendermode staat als attribuut op dezelfde regel.
    /// Zou iemand hem over meerdere regels verdelen, dan valt deze test — en dat is de goede kant, want dan
    /// hoort iemand te kijken waarom deze test bestaat.
    /// </remarks>
    private static string Aanroep()
    {
        var pad = Path.Combine(
            Broncode.Portaalproject.FullName,
            Bestand.Replace('/', Path.DirectorySeparatorChar));

        return File.ReadAllLines(pad)
            .FirstOrDefault(regel => regel.Contains($"<{Component}", StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"In {Bestand} staat geen aanroep van {Component}. Is het component hernoemd of verhuisd, "
                + "dan hoort deze test mee te veranderen — hij bewaakt de rendermode waarop de "
                + "gegevensintegriteit van twee scopevelden rust.");
    }
}
