using System.Buffers;
using Soratus.Agents.Contracts;

namespace Soratus.Portal.Mail;

/// <summary>
/// De bewerkingen op vrije tekst die elke uitgaande mail nodig heeft, ongeacht de lezer.
/// </summary>
/// <remarks>
/// <para><strong>Deze klasse staat hier omdat er twee doelen zijn.</strong> Een onderwerpregel is bij
/// een maandoverzicht en bij een storingsmelding hetzelfde soort ding: één regel, in een postbuslijst,
/// zonder tekens die die lijst uit elkaar zetten. Zou elk doel zijn eigen knip schrijven, dan bestaan
/// er twee opvattingen over "één regel" en gaan die schuiven — punt 13 zegt dat met zoveel woorden, en
/// dit is de plek waar het opnieuw zou zijn gebeurd.</para>
///
/// <para><strong>Wat hier níet staat is een oordeel over de lezer.</strong> Een storingsmelding aan
/// Soratus mág een stacktrace dragen en een maandoverzicht aan een klant niet; dat onderscheid zit in
/// de opmaakfunctie van het doel en niet hier. Deze klasse doet één ding: een veld dat één regel hoort
/// te zijn, tot één regel maken.</para>
/// </remarks>
internal static class MailText
{
    /// <summary>
    /// De grens waarop een naam in een onderwerpregel wordt ingekort.
    /// </summary>
    /// <remarks>
    /// Ruim boven elke echte bedrijfsnaam en ruim onder de lengte waarop een onderwerpregel in een
    /// postbuslijst onleesbaar wordt. De grens doet in de praktijk niets; hij staat er voor de dag
    /// dat iemand een heel adres in het naamveld van een klant zet.
    /// </remarks>
    internal const int NameLimit = 120;

    /// <summary>
    /// De tekens die uit een regel worden verwijderd voordat hij een onderwerpregel wordt.
    /// </summary>
    /// <remarks>
    /// <para>Dit is <em>geen</em> tweede definitie van "één regel". Waar een regel eindigt wordt
    /// door <see cref="MessageTruncation.Cut"/> bepaald en alleen daar — punt 13 van de
    /// fase-0-afwijkingen zegt met zoveel woorden dat twee kopieën van die beslissing gaan schuiven.
    /// Wat hier gebeurt is iets anders: tekens weghalen die in géén enkele regel horen. De tab en
    /// de verticale tab overleven de knip (het zijn geen regelovergangen) en zetten een
    /// onderwerpregel in een postbuslijst uit elkaar; NEL (U+0085), LINE SEPARATOR (U+2028) en
    /// PARAGRAPH SEPARATOR (U+2029) zijn regelovergangen die <c>IndexOfAny("\r\n")</c> niet ziet.
    /// </para>
    ///
    /// <para>Dit is bewust <em>geen</em> verdediging tegen kopinjectie. Communication Services krijgt
    /// het onderwerp als veld in een JSON-lichaam over HTTPS en niet als SMTP-kop, dus er is geen
    /// kop om in te injecteren. Zou die verdediging hier als reden staan, dan zou iemand hem later
    /// weghalen omdat de reden niet klopt — en dan verdwijnt de echte reden mee.</para>
    /// </remarks>
    private static readonly SearchValues<char> Unwanted =
        SearchValues.Create("\t\v\f\u0085\u2028\u2029");

    /// <summary>
    /// Houdt van een vrij tekstveld de eerste regel over, zonder tekens die in geen regel horen.
    /// </summary>
    /// <param name="value">De tekst uit de opslag of uit een formulier.</param>
    /// <param name="limit">De lengtegrens.</param>
    /// <returns>De tekst zoals hij op één regel mag staan. Nooit <c>null</c>.</returns>
    /// <remarks>
    /// <para>De knip komt uit <see cref="MessageTruncation.Cut"/> en dus uit
    /// <c>Soratus.Agents.Contracts</c> — dezelfde functie die de agentbibliotheek en de
    /// klantprojectie van de logregels gebruiken. Punt 13: één definitie van "één regel", op één
    /// plek, want drie kopieën gaan schuiven.</para>
    ///
    /// <para>Een leeg veld komt leeg terug en niet als <c>"(geen bericht)"</c>. Dat is de
    /// terugvalwaarde van <see cref="MessageTruncation.Cut"/> en die hoort bij een logregel: daar is
    /// een leeg bericht een fout van de agentbouwer die benoemd mag worden. Hier is het een leeg
    /// naamveld, en dan hoort de opmaakfunctie eromheen te besluiten wat er staat — zie
    /// <see cref="StatementText.Greeting"/>. Dezelfde afweging als in <c>Views/CustomerMessage.cs</c>.
    /// </para>
    /// </remarks>
    internal static string OneLine(string? value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var head = MessageTruncation.Cut(value, Math.Max(limit, MessageTruncation.MinimumLength))
            .Message
            .Trim();

        return head.AsSpan().ContainsAny(Unwanted)
            ? new string([.. head.Where(character => !Unwanted.Contains(character))]).Trim()
            : head;
    }
}

/// <summary>
/// Of een tekst als ontvanger van één bericht te gebruiken is.
/// </summary>
/// <remarks>
/// <para><strong>Dit is uitdrukkelijk geen tweede adresvalidatie.</strong> Of een adres een geldig
/// adres is, is bij het invoeren al vastgesteld — dat is portaalwerk uit fase 2 en het hoort niet twee
/// keer, anders bestaan er twee opvattingen over wat een adres is en weigert de ene wat de andere
/// heeft geaccepteerd. Wat hier wordt getoetst is smaller en anders: of deze tekst als één ontvanger
/// van één bericht te gebruiken is.</para>
///
/// <para>Waarom dat er niettemin staat, met twee gevallen erbij. Een adres uit de opslag is niet per
/// definitie door het formulier van vandaag gegaan: in de opslag staan documenten uit de
/// configuratiemigratie, en een adres dat als tekst in een JSON-bestand stond is nooit door een
/// veldcontrole gekomen. En het adres van de storingsmelder komt uit een app-setting, waar geen enkel
/// formulier tussen zit. Dit is de laatste plek voordat het buiten ons systeem gaat.</para>
///
/// <para><strong>Eén controle voor beide doelen, en dat is de reden dat hij hier staat en niet bij
/// <see cref="StatementRecipients"/>.</strong> Twee kopieën zouden gaan verschillen op precies het
/// geval waar het om gaat: <c>"Jan &lt;jan@x.nl&gt;, iemand@elders.nl"</c> als één adres opgeslagen is
/// een tweede ontvanger die niemand heeft toegevoegd, en dat is bij een storingsmelding niet minder
/// erg dan bij een maandoverzicht.</para>
/// </remarks>
internal static class MailAddresses
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
    /// weergavenaam kunnen veranderen. De regelovergangen en de tab staan erbij om dezelfde reden als
    /// in <see cref="MailText"/>.
    /// </remarks>
    private static readonly SearchValues<char> Forbidden =
        SearchValues.Create("<>,;:\\\"\'()[] \t\r\n\v\f\u0085\u2028\u2029");

    /// <summary>
    /// Of dit als e-mailadres van een ontvanger te gebruiken is.
    /// </summary>
    /// <param name="email">Het adres zoals het in de opslag of in de configuratie staat.</param>
    /// <returns><c>true</c> als het bruikbaar is.</returns>
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
