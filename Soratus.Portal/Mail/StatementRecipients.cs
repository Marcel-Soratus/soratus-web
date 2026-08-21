using System.Buffers;
using Soratus.Portal.Data;

namespace Soratus.Portal.Mail;

/// <summary>
/// Zoekt de contactpersoon van een klant op en controleert of hij als ontvanger te gebruiken is.
/// </summary>
/// <remarks>
/// <para><strong>Het adres komt uit de toegangsdocumenten en niet uit het contract.</strong> §3.7
/// zegt "mailen naar de contactpersoon" en §3.5 zet de contactpersoon op de contractkaart — maar dat
/// veld (<see cref="ContractDocument.Contact"/>) is een <em>naam</em> en geen adres. Het enige veld
/// in dit portaal dat een e-mailadres van de klant bevat, is
/// <see cref="AccessDocument.Email"/>, en dat adres is bovendien genormaliseerd en gecontroleerd op
/// het moment dat de operator het invoerde. Zou hier het naamveld van het contract worden gebruikt,
/// dan zou een operator die daar per ongeluk een adres in typt bepalen waar de mail heen gaat.</para>
///
/// <para><strong>Welke toegangsregels: die met de aanduiding "Beheerder klant".</strong> Dat is
/// volgens <see cref="PortalAccessRoles"/> geen bevoegdheid maar precies wat de aanduiding wél zegt:
/// wie we aanspreken. Een "Lezer" is iemand die mag meekijken en niet iemand die het maandoverzicht
/// hoort te krijgen — dat verschil bestaat in dit portaal nergens anders, en hier bestaat het wel.
/// Dat is de eerste keer dat de twee aanduidingen iets van elkaar onderscheiden, en het staat als
/// punt van twijfel in het rapport: de contracttekst zegt met zoveel woorden dat ze hetzelfde
/// leesrecht geven, en dat blijft waar — dit gaat niet over recht maar over adressering.</para>
/// </remarks>
internal static class StatementRecipients
{
    /// <summary>
    /// De maximale lengte van een e-mailadres.
    /// </summary>
    /// <remarks>
    /// 254 is de praktische bovengrens van een adres in een SMTP-envelop (RFC 5321 zet de envelop op
    /// 256 inclusief de punthaken). De grens staat er niet om spec-getrouw te zijn maar omdat een
    /// veld dat een adres hoort te bevatten en drie kilobyte lang is, geen adres bevat.
    /// </remarks>
    private const int AddressLimit = 254;

    /// <summary>
    /// De tekens die een e-mailadres onbruikbaar maken als ontvanger.
    /// </summary>
    /// <remarks>
    /// De punthaken en de scheidingstekens staan erbij omdat ze een adres in een lijst of in een
    /// weergavenaam kunnen veranderen: <c>"Jan &lt;jan@x.nl&gt;, iemand@elders.nl"</c> is als één
    /// adres opgeslagen een tweede ontvanger die niemand heeft toegevoegd. De regelovergangen en de
    /// tab staan erbij om dezelfde reden als in <see cref="StatementText"/>.
    /// </remarks>
    private static readonly SearchValues<char> Forbidden =
        SearchValues.Create("<>,;:\\\"\'()[] \t\r\n\v\f\u0085\u2028\u2029");

    /// <summary>
    /// Bepaalt aan wie het maandoverzicht van deze klant gaat.
    /// </summary>
    /// <param name="access">De toegangsdocumenten van deze klant.</param>
    /// <returns>
    /// De adressering, of de reden dat er geen ontvanger is. <see cref="StatementRefusal.NoRecipient"/>
    /// als er geen contactpersoon is vastgelegd, <see cref="StatementRefusal.RecipientInvalid"/> als
    /// er één is die niet als adres te gebruiken is.
    /// </returns>
    /// <remarks>
    /// <para><strong>Eén onbruikbaar adres weigert de hele verzending, ook als er een goed adres
    /// naast staat.</strong> Dat is de duurdere van de twee keuzes en hij is de juiste: de andere
    /// vorm — versturen naar wat wél klopt — levert een verzendbevestiging op die "verstuurd" zegt
    /// terwijl de persoon voor wie het overzicht bedoeld was niets heeft gekregen. Dat is een
    /// stille onwaarheid met een tijdstempel eronder, en die is in dit portaal al drie keer
    /// afgewezen.</para>
    ///
    /// <para><strong>De aanhef krijgt alleen een naam bij precies één ontvanger.</strong> Bij twee
    /// contactpersonen zou "Beste Jan," aan Marieke gaan. "Beste relatie," is dan het eerlijke
    /// antwoord — zie <see cref="StatementText.Greeting"/>.</para>
    /// </remarks>
    internal static (StatementAddressing? Addressing, StatementRefusal Refusal) Resolve(
        IReadOnlyList<AccessDocument> access)
    {
        ArgumentNullException.ThrowIfNull(access);

        var contacts = access
            .Where(document => string.Equals(
                document.Role,
                PortalAccessRoles.Administrator,
                StringComparison.Ordinal))
            .OrderBy(document => document.Email, StringComparer.Ordinal)
            .ToArray();

        if (contacts.Length == 0)
        {
            return (null, StatementRefusal.NoRecipient);
        }

        if (contacts.Any(document => !IsUsable(document.Email)))
        {
            return (null, StatementRefusal.RecipientInvalid);
        }

        var name = contacts.Length == 1 ? contacts[0].Name : null;

        return (
            new StatementAddressing([.. contacts.Select(document => document.Email)], name),
            StatementRefusal.None);
    }

    /// <summary>
    /// Of dit als e-mailadres van een ontvanger te gebruiken is.
    /// </summary>
    /// <param name="email">Het adres zoals het in het toegangsdocument staat.</param>
    /// <returns><c>true</c> als het bruikbaar is.</returns>
    /// <remarks>
    /// <para><strong>Dit is uitdrukkelijk geen tweede adresvalidatie.</strong> Of een adres een
    /// geldig adres is, is bij het invoeren al vastgesteld — dat is portaalwerk uit fase 2 en het
    /// hoort niet twee keer, anders bestaan er twee opvattingen over wat een adres is en weigert de
    /// ene wat de andere heeft geaccepteerd. Wat hier wordt getoetst is smaller en anders: of deze
    /// tekst als één ontvanger van één bericht te gebruiken is.</para>
    ///
    /// <para>Waarom dat er niettemin staat, met een geval erbij: een adres uit de opslag is niet per
    /// definitie door het formulier van vandaag gegaan. In de opslag staan documenten uit de
    /// configuratiemigratie, en een adres dat als tekst in een JSON-bestand stond is nooit door een
    /// veldcontrole gekomen. Dit is de laatste plek voordat het buiten ons systeem gaat.</para>
    /// </remarks>
    internal static bool IsUsable(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > AddressLimit)
        {
            return false;
        }

        if (email.AsSpan().ContainsAny(Forbidden))
        {
            return false;
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);

        // Precies één apenstaartje, met aan beide zijden iets, en een punt in het domein. Geen
        // reguliere expressie: die zou de indruk geven dat hier de adresdefinitie staat.
        return at > 0
            && at == email.LastIndexOf('@')
            && at < email.Length - 1
            && email.AsSpan(at + 1).Contains('.')
            && !email.EndsWith('.')
            && !email.Contains("..", StringComparison.Ordinal);
    }
}
