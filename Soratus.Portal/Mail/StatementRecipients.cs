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

        if (contacts.Any(document => !MailAddresses.IsUsable(document.Email)))
        {
            return (null, StatementRefusal.RecipientInvalid);
        }

        var name = contacts.Length == 1 ? contacts[0].Name : null;

        return (
            new StatementAddressing([.. contacts.Select(document => document.Email)], name),
            StatementRefusal.None);
    }
}
