namespace Soratus.Support.FirstLine;

/// <summary>
/// Waarom de eerstelijn geen feit kiest en de vraag naar een mens gaat.
/// </summary>
/// <remarks>
/// <para><strong>Drie waarden en niet vier.</strong> Het portaal kent er vier
/// (<c>SupportEscalation</c>), en de vierde — "het portaal heeft het antwoord niet aangenomen" — is
/// het oordeel van het portaal en niet van het model. Die waarde staat hier daarom met opzet niet:
/// een model dat zijn eigen antwoord onbruikbaar mag verklaren, kan een fout van het portaal
/// nabootsen, en dan is in de logregel niet meer te zien wie het heeft afgekeurd.</para>
///
/// <para><strong>Een enum en geen tekst</strong>, om de reden die bij <c>SupportEscalation</c> staat:
/// een reden die als tekst reist komt op een dag uit een <c>catch</c>-blok. Hier is dat een graad
/// scherper dan daar, want de tekst zou van buiten ons proces komen — uit een taalmodel dat een
/// klantvraag heeft gelezen.</para>
/// </remarks>
public enum FirstLineHandoff
{
    /// <summary>
    /// De eerstelijn weet het niet zeker.
    /// </summary>
    /// <remarks>
    /// De eerste waarde, dus de waarde van een niet-gezette enum, en van de drie de enige die niets
    /// beweert. Dezelfde ordening en dezelfde reden als bij <c>SupportEscalation.NotSure</c>.
    /// </remarks>
    NotSure,

    /// <summary>De vraag gaat niet over de feiten die zijn aangeboden.</summary>
    OutsideTheData,

    /// <summary>De vraag vraagt een besluit of een toezegging en geen feit.</summary>
    NeedsAHuman,
}

/// <summary>
/// Eén vraag van één klant, met alles waaruit gekozen mag worden.
/// </summary>
/// <remarks>
/// <para><strong>Wat er niet op staat.</strong> Geen klantnaam, geen klantslug, geen e-mailadres,
/// geen contract, geen agentversie, geen omgevingsdetail, geen gespreksgeschiedenis. Dit type is de
/// volledige opsomming van wat er over een klant het portaalproces verlaat, en het is met opzet zo
/// klein dat die opsomming in één regel past: de vraag, en de regels waaruit gekozen wordt.</para>
///
/// <para><strong>De feiten zijn platte regels en geen objecten met een sleutel.</strong> Dat is geen
/// versimpeling maar de grens: wie een sleutel meestuurt, stuurt iets mee waarmee de andere kant kan
/// gaan zoeken. Hier is er niets om mee te zoeken — de regels zijn af, ze zijn door het portaal
/// opgemaakt uit zijn eigen klantweergaven, en ze noemen zelf al waar ze over gaan ("De agent
/// voorraad-sync heeft de status …", "In juli 2026 staan …"). Er is dus geen apart label nodig, en
/// dat is gemeten en niet aangenomen: zie <c>SupportText.AgentFact</c>, <c>HoursFact</c> en
/// <c>BillingFact</c>.</para>
/// </remarks>
public sealed record FirstLineQuestion
{
    /// <summary>
    /// De vraag van de klant, zoals hij die heeft getypt (al geschoond door het portaal).
    /// </summary>
    /// <remarks>
    /// <para><strong>Vrije tekst, en dus mogelijk een instructie aan het model.</strong> Daar wordt
    /// niets tegen gefilterd, en dat is een besluit met een grens eronder: het ergste dat een
    /// geslaagde instructie oplevert, is dat er een <em>ander</em> feit uit
    /// <see cref="Facts"/> wordt gekozen dan het feit dat bij de vraag hoort. Er is geen weg naar een
    /// verzonnen feit (er is geen tekstveld terug), geen weg naar het feit van een andere klant (die
    /// staat niet in de lijst) en geen weg naar vrije tekst op het scherm van de klant. Zie §47.7 van
    /// de fase-0-afwijkingen voor waarom een filter op instructieachtige zinnen hier is afgewezen.
    /// </para>
    /// </remarks>
    public required string Text { get; init; }

    /// <summary>
    /// De feiten waaruit gekozen mag worden, één regel per feit, in de volgorde van het portaal.
    /// </summary>
    /// <remarks>
    /// <para><strong>De volgorde is de betekenis.</strong> Een keuze is een plaats in déze lijst, dus
    /// een lijst die onderweg wordt gesorteerd, gefilterd of gededupliceerd maakt van een goed
    /// antwoord een verkeerd antwoord — en wel een stil verkeerd antwoord, want de vorm blijft
    /// kloppen. Deze lijst wordt daarom één keer opgebouwd en één keer teruggelezen; zie
    /// <c>ChoosingFirstLine</c> in het portaal, waar beide kanten van die afspraak in één methode
    /// staan.</para>
    ///
    /// <para>Een lege lijst is een geldige toestand — een klant zonder agents, zonder uren en zonder
    /// gemeten maand. Er valt dan niets te kiezen, en er hoort dan ook niets gevraagd te worden: zie
    /// <see cref="AzureOpenAiChooser"/>, die in dat geval geen aanroep doet.</para>
    /// </remarks>
    public required IReadOnlyList<string> Facts { get; init; }
}

/// <summary>
/// Wat de eerstelijn teruggeeft: de plaats van één feit, of een overdracht aan een mens.
/// </summary>
/// <remarks>
/// <para><strong>Er is geen tekstveld, in geen van beide vormen.</strong> Dat is dezelfde regel als
/// bij <c>SupportAnswer</c> in het portaal, hier een laag dieper doorgetrokken: daar is de uitkomst
/// een verwijzing naar een grondslag, hier is het een geheel getal. Een verzonnen bedrag heeft geen
/// veld om in te reizen, en een verzonnen zin ook niet.</para>
///
/// <para><strong>Fabrieken en geen constructor</strong>, zodat de toestand "geen van beide gevuld"
/// niet bestaat. Dezelfde constructie als <c>SupportAnswer</c> en <c>HoursQuery</c>.</para>
/// </remarks>
public sealed record FirstLineChoice
{
    /// <summary>Alleen de twee fabrieken maken een keuze.</summary>
    private FirstLineChoice(int? index, FirstLineHandoff? handoff)
    {
        Index = index;
        Handoff = handoff;
    }

    /// <summary>
    /// De plaats van het gekozen feit in <see cref="FirstLineQuestion.Facts"/>, nulgebaseerd, of
    /// <c>null</c> bij een overdracht.
    /// </summary>
    public int? Index { get; }

    /// <summary>De reden van de overdracht, of <c>null</c> als er een feit is gekozen.</summary>
    public FirstLineHandoff? Handoff { get; }

    /// <summary>
    /// Er is een feit gekozen: dit is zijn plaats in de aangeboden lijst.
    /// </summary>
    /// <param name="index">De plaats, nulgebaseerd.</param>
    /// <returns>De keuze.</returns>
    /// <remarks>
    /// <para><strong>Hier staat met opzet géén controle op het bereik.</strong> Dat is niet
    /// vergeten en het is de belangrijkste opmerking in dit bestand.</para>
    ///
    /// <para>De lijst waar deze plaats in wordt gezocht is van de andere kant van deze naad, en de
    /// kant die de lijst bezit is de enige die over het bereik kan oordelen. Zou hier een controle
    /// staan, dan lag hetzelfde oordeel op twee plekken — en twee stukken code die per ongeluk
    /// hetzelfde doen, dekken elkaars afwezigheid (punt 41 van de fase-0-afwijkingen). Een
    /// controle híer die de andere kant niet meer heeft, zou bovendien vertrouwen zijn: de lengte
    /// van de lijst zou dan uit dit proces komen in plaats van uit het portaal.</para>
    ///
    /// <para><strong>En een buitenbereikse plaats levert een overdracht op en geen uitzondering, en
    /// vooral geen afkapping naar de dichtstbijzijnde geldige plaats.</strong> Afkappen kiest een
    /// plausibel verkeerd feit — een antwoord met een bronregel eronder die er niet bij hoort — en
    /// dat is precies de fout die dit hele ontwerp onmogelijk wil maken. Zie
    /// <c>ChoosingFirstLine</c>.</para>
    /// </remarks>
    public static FirstLineChoice Fact(int index) => new(index, handoff: null);

    /// <summary>
    /// Geen feit: de vraag gaat naar een mens.
    /// </summary>
    /// <param name="reason">Waarom.</param>
    /// <returns>De keuze.</returns>
    public static FirstLineChoice ToAHuman(FirstLineHandoff reason) => new(index: null, reason);
}

/// <summary>
/// Kiest één van de aangeboden feiten, of draagt over aan een mens.
/// </summary>
/// <remarks>
/// <para><strong>Dit is de hele opdracht aan het model, en daarom is de prompt klein en de fout
/// klein.</strong> Het model schrijft niets: het kiest een nummer. Wat er dan nog fout kan gaan is
/// dat het het verkeerde nummer kiest, en dat is een fout die een klant kan zien — de bronregel
/// onder de bubbel noemt de agent of de maand en verwijst naar het scherm. Dat is het verschil met
/// een verzonnen getal, dat er hetzelfde uitziet als een echt getal.</para>
///
/// <para><strong>Deze naad staat buiten <c>Soratus.Portal</c>, en dat is de kern.</strong> Wie hem
/// implementeert kan geen grondslag maken, geen tekst teruggeven, geen klant opzoeken en niets naar
/// Cosmos schrijven — niet omdat het verboden is, maar omdat dit project de types waarmee dat zou
/// moeten niet eens ziet. Zie §47 van <c>docs/agent-portal/fase-0-afwijkingen.md</c>.</para>
/// </remarks>
public interface IFirstLineChooser
{
    /// <summary>
    /// Kiest, of draagt over.
    /// </summary>
    /// <param name="question">De vraag en de feiten.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// <para>De keuze, of <c>null</c>.</para>
    /// <para><c>null</c> betekent iets anders dan een overdracht en dat onderscheid is er met opzet:
    /// een overdracht is een <em>besluit</em> van de eerstelijn ("hier gaat geen van deze feiten
    /// over"), <c>null</c> is een <em>storing</em> ("wij hebben het niet kunnen vragen"). De klant
    /// leest in beide gevallen dezelfde zin — er is één escalatietekst — maar de operator ziet het
    /// verschil in de logregel, en dat is waar hij het nodig heeft.</para>
    /// </returns>
    Task<FirstLineChoice?> ChooseAsync(
        FirstLineQuestion question,
        CancellationToken cancellationToken = default);
}
