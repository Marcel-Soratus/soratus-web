using System.Globalization;

namespace Soratus.Portal.Data;

/// <summary>
/// De Azure-scope van één klant, in de vorm waarin Cost Management hem verlangt.
/// </summary>
/// <remarks>
/// <para><strong>Dit type bestaat omdat een resource group die niet bestaat HTTP 200 met nul rijen
/// geeft.</strong> Dat is gemeten (punt 30) en het is de gevaarlijkste eigenschap van deze hele lane:
/// een collector die zijn scope uit een weergavetekst afleidt en er één letter naast zit, krijgt geen
/// fout maar een geslaagd, leeg antwoord — en dat rolt door naar een factuur. De verdediging bestaat
/// uit twee delen die elkaar niet vervangen: <em>controleren wat te controleren is</em> (dit type) en
/// <em>tonen wat er is bevraagd</em> (<see cref="AzureCostDocument.Scope"/>).</para>
///
/// <para><strong>Waarom dit één veld is in de exacte ARM-padvorm en niet twee velden.</strong> De
/// afweging is echt en de andere kant is verdedigbaar; dit is waarom deze kant het is geworden:</para>
///
/// <list type="number">
///   <item><description>
///     <strong>Eén veld heeft twee toestanden en twee velden hebben drie.</strong> Leeg betekent "niet
///     ingericht" en dat is een geldige toestand. Met een abonnements-id én een resourcegroepnaam
///     bestaat er een derde toestand — de één ingevuld en de ander niet — die niets betekent en die
///     dus apart moet worden afgevangen, gemeld en getest. Een pad is één waarde: hij is er of hij is
///     er niet.
///   </description></item>
///   <item><description>
///     <strong>Het is letterlijk de tekenreeks die de deur uit gaat.</strong> De aanroep is
///     <c>POST https://management.azure.com{scope}/providers/Microsoft.CostManagement/query</c>, dus
///     er wordt niets samengesteld. Dat is belangrijker dan het lijkt: met twee velden bouwt de
///     collector het pad en bouwt het scherm het opnieuw voor de regel "bevraagd: …", en de eerste
///     keer dat die twee opbouwen verschillen staat er op het scherm een andere scope dan er is
///     bevraagd. Precies de tweede waarheid die punt 34 bij het opslagpercentage weigert.
///   </description></item>
///   <item><description>
///     <strong>De operator plakt, hij typt niet.</strong> Het veld <em>Resource-ID</em> op de
///     eigenschappenpagina van een resource group in Azure is exact deze tekenreeks. De invoerweg is
///     dus kopiëren, en dat is de weg met de minste tikfouten.
///   </description></item>
/// </list>
///
/// <para><strong>Wat er wél wordt gecontroleerd, en wat niet kan.</strong> Het abonnements-id is een
/// guid en dat is hard te controleren. Een resourcegroepnaam heeft regels — lengte en toegestane
/// tekens — en die zijn ook hard. Wat niet te controleren is, is of die resource group <em>bestaat</em>:
/// dat is precies de meting hierboven. Juist daarom hoort wat wél te controleren is ook echt
/// gecontroleerd te worden; dat is de enige laag die er is.</para>
///
/// <para><strong>De schrijfwijze van de resourcegroepnaam blijft zoals de operator hem invulde, en dat
/// is een gemeten keuze.</strong> Op 21 augustus 2026 gaf
/// <c>/subscriptions/501a66d2-…/resourcegroups/mbv</c> exact dezelfde 112 rijen als
/// <c>/subscriptions/501a66d2-…/resourceGroups/MBV</c> — zelfde kolommen, zelfde bedragen. Het pad is
/// dus hoofdletterongevoelig, en de echte resource group heet <c>MBV</c>. Daarmee valt de hoofdletter
/// weg als storingsoorzaak, en blijft er één reden over om hem niet aan te raken: de tekenreeks komt
/// onder een maand zonder regels op het scherm te staan als "bevraagd: …", en daar hoort te staan wat
/// er is ingevuld en niet wat wij ervan hebben gemaakt. De twee <em>vaste</em> delen van het pad
/// (<c>/subscriptions/</c> en <c>/resourceGroups/</c>) worden wél genormaliseerd, want die zijn van
/// Azure en niet van de operator — en dan zijn twee klantscopes met elkaar te vergelijken.</para>
///
/// <para>Er staat met opzet geen resource-niveau in dit type. Cost Management kan op abonnement,
/// beheergroep en resource group; het portaal heeft vandaag alleen recht op een resource group (B5 van
/// het haalbaarheidsonderzoek), en een vorm die meer toelaat dan er recht is, levert een scope op die
/// een geslaagd leeg antwoord geeft — de fout die dit type juist moet uitsluiten.</para>
/// </remarks>
/// <param name="SubscriptionId">Het abonnement waarin deze klantomgeving staat.</param>
/// <param name="ResourceGroup">De resourcegroepnaam, in de schrijfwijze waarin hij is ingevuld.</param>
public sealed record AzureScope(Guid SubscriptionId, string ResourceGroup)
{
    /// <summary>Het vaste eerste segment van een ARM-pad.</summary>
    private const string Subscriptions = "subscriptions";

    /// <summary>Het vaste tweede segment, in de schrijfwijze die Azure zelf gebruikt.</summary>
    private const string ResourceGroups = "resourceGroups";

    /// <summary>De langste resourcegroepnaam die Azure toestaat.</summary>
    /// <remarks>
    /// Negentig, uit de documentatie van Azure zelf (<c>Microsoft.Resources/resourceGroups</c>). Dit is
    /// geen grens die wij kiezen: een naam die langer is bestaat niet, dus een scope die hem noemt
    /// levert een geslaagd leeg antwoord op.
    /// </remarks>
    public const int MaximumResourceGroupLength = 90;

    /// <summary>
    /// De tekens die naast letters en cijfers in een resourcegroepnaam mogen staan.
    /// </summary>
    /// <remarks>
    /// Onderstrepingsteken, koppelstreepje, punt en ronde haakjes. Azure staat ook unicodeletters en
    /// -cijfers toe, en <see cref="char.IsLetterOrDigit(char)"/> dekt die — een naam met een accent is
    /// geldig in Azure en hoort hier niet te worden geweigerd.
    /// </remarks>
    private static readonly char[] Allowed = ['_', '-', '.', '(', ')'];

    /// <summary>
    /// Het pad zoals het naar Cost Management gaat.
    /// </summary>
    /// <remarks>
    /// Zonder afsluitende schuine streep en zonder het <c>/providers/…</c>-deel: dat laatste hangt aan
    /// de aanroep en niet aan de klant. Zie <see cref="AzureCostClient"/>.
    /// </remarks>
    public string Path => string.Create(
        CultureInfo.InvariantCulture,
        $"/{Subscriptions}/{SubscriptionId:D}/{ResourceGroups}/{ResourceGroup}");

    /// <inheritdoc />
    /// <remarks>
    /// Gelijk aan <see cref="Path"/>. Dat is hier geen gemak maar een grens: een scope die in een
    /// logregel of in een melding terechtkomt, hoort dezelfde tekenreeks te zijn als die naar de API
    /// gaat. Een record met de standaard-<c>ToString</c> zou daar
    /// <c>AzureScope { SubscriptionId = …, ResourceGroup = … }</c> neerzetten, en dan staat er in het
    /// log iets wat niet is bevraagd.
    /// </remarks>
    public override string ToString() => Path;

    /// <summary>
    /// Leest een scope uit de tekst die een operator invult of die in een document staat.
    /// </summary>
    /// <param name="text">De tekst, of <c>null</c>.</param>
    /// <param name="scope">De scope, of <c>null</c> als de tekst leeg of onbruikbaar is.</param>
    /// <returns><c>true</c> als er een scope uit kwam.</returns>
    /// <remarks>
    /// <para><strong>Leeg levert <c>false</c> met <c>null</c> en dat is geen fout.</strong> "Er is geen
    /// scope vastgelegd" is een geldige toestand — punt 15, hier op de plek waar hij de collector
    /// raakt: een klant zonder scope wordt niet bevraagd, en dan staat er op het facturatiescherm dat
    /// er niets is ingericht en niet € 0,00. Wie het onderscheid tussen leeg en onbruikbaar nodig
    /// heeft, gebruikt <see cref="Validate"/>.</para>
    /// </remarks>
    public static bool TryParse(string? text, out AzureScope? scope)
    {
        scope = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var parts = text.Trim().Trim('/').Split('/');

        if (parts.Length != 4
            || !string.Equals(parts[0], Subscriptions, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[2], ResourceGroups, StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParseExact(parts[1], "D", out var subscription)
            || ResourceGroupError(parts[3]) is not null)
        {
            return false;
        }

        scope = new AzureScope(subscription, parts[3]);
        return true;
    }

    /// <summary>
    /// Wat er niet klopt aan de ingevulde scope, of <c>null</c> als hij klopt of leeg is.
    /// </summary>
    /// <param name="text">De tekst uit het formulier.</param>
    /// <returns>De melding voor het formulier, of <c>null</c>.</returns>
    /// <remarks>
    /// <para><strong>Leeg geeft <c>null</c>: niets invullen is toegestaan.</strong> Een klant die nog
    /// niet is ingericht heeft geen Azure-scope, en een verplicht veld zou daar een verzonnen pad
    /// opleveren — hetzelfde mechanisme waarmee een verplicht contractnummer een verzonnen nummer
    /// oplevert (zie <see cref="ContractEdit.Validate"/>). En een verzonnen pad is hier duurder dan een
    /// verzonnen nummer: het geeft HTTP 200 met nul rijen, dus het ziet uit als een antwoord.</para>
    ///
    /// <para>De meldingen noemen wat er wordt verwacht en waar het te vinden is. Dat is geen
    /// vriendelijkheid: de enige betrouwbare invoerweg is kopiëren uit Azure, en een melding die dat
    /// niet zegt laat iemand het opnieuw intypen.</para>
    /// </remarks>
    public static string? Validate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var trimmed = text.Trim();

        if (trimmed.Length > MaximumScopeLength)
        {
            return $"Deze Azure-scope is langer dan {MaximumScopeLength} tekens en kan dus geen "
                + "abonnement met een resource group zijn.";
        }

        var parts = trimmed.Trim('/').Split('/');

        if (parts.Length != 4
            || !string.Equals(parts[0], Subscriptions, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(parts[2], ResourceGroups, StringComparison.OrdinalIgnoreCase))
        {
            return "Een Azure-scope heeft de vorm "
                + "/subscriptions/<abonnements-id>/resourceGroups/<naam>. Dat is het veld Resource-ID "
                + "op de eigenschappenpagina van de resource group in Azure; kopieer het daarvandaan "
                + "in plaats van het te typen.";
        }

        if (!Guid.TryParseExact(parts[1], "D", out _))
        {
            return $"'{parts[1]}' is geen abonnements-id. Dat is een guid met streepjes, "
                + "bijvoorbeeld 501a66d2-de54-4d4f-9f7c-1fbb55bec17f.";
        }

        return ResourceGroupError(parts[3]);
    }

    /// <summary>
    /// De langste scope die nog een abonnement met een resource group kan zijn.
    /// </summary>
    /// <remarks>
    /// De twee vaste segmenten, een guid van zesendertig tekens, de schuine strepen en een naam van
    /// maximaal negentig. Deze grens staat er om de meldingen bruikbaar te houden: zonder hem zou een
    /// per ongeluk geplakte lap tekst als geheel in een foutmelding op het scherm belanden.
    /// </remarks>
    public const int MaximumScopeLength =
        1 + 13 + 1 + 36 + 1 + 14 + 1 + MaximumResourceGroupLength;

    /// <summary>
    /// Wat er niet klopt aan een resourcegroepnaam, of <c>null</c>.
    /// </summary>
    /// <param name="name">De naam.</param>
    /// <returns>De melding, of <c>null</c>.</returns>
    /// <remarks>
    /// De regels komen van Azure: één tot negentig tekens, letters, cijfers, <c>_</c>, <c>-</c>,
    /// <c>.</c> en ronde haakjes, en niet eindigend op een punt. Een naam die deze regels schendt
    /// bestaat niet, en een scope die hem noemt geeft dus een geslaagd leeg antwoord in plaats van een
    /// fout — dat is de reden dat dit hier wordt tegengehouden en niet aan de API wordt overgelaten.
    /// </remarks>
    private static string? ResourceGroupError(string name)
    {
        if (name.Length == 0)
        {
            return "Vul de naam van de resource group in achter /resourceGroups/.";
        }

        if (name.Length > MaximumResourceGroupLength)
        {
            return $"Een resourcegroepnaam is ten hoogste {MaximumResourceGroupLength} tekens lang.";
        }

        foreach (var character in name)
        {
            if (!char.IsLetterOrDigit(character) && !Allowed.Contains(character))
            {
                return $"'{name}' kan geen resourcegroepnaam zijn: het teken '{character}' mag daar "
                    + "niet in staan. Azure staat letters, cijfers, _, -, . en ronde haakjes toe.";
            }
        }

        return name.EndsWith('.')
            ? "Een resourcegroepnaam eindigt niet op een punt."
            : null;
    }
}
