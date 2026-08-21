using System.Buffers;
using System.Globalization;
using Soratus.Agents.Contracts;
using Soratus.Portal.Components.Pages;
using Soratus.Portal.Data;

namespace Soratus.Portal.Mail;

/// <summary>
/// De woorden en de vormen van het maandoverzicht: wat er in de mail staat, en wat er op het
/// operatorscherm over de verzending staat.
/// </summary>
/// <remarks>
/// <para><strong>Alle tekst die een klant leest staat in dit bestand, en nergens anders.</strong> Dat
/// is geen ordelijkheid maar de kern van de beveiliging van deze map: een mail is een klantoppervlak
/// waar geen operator nog naar kijkt, en de manier om te controleren dat er niets in sluipt wat er
/// niet in hoort, is dat er precies één plek is waar de zinnen ontstaan. De opmaakfunctie neemt
/// getallen en een naam aan en verder niets.</para>
///
/// <para><strong>Getallen komen uit <see cref="ContractText"/> en worden hier niet opnieuw
/// opgemaakt.</strong> Dat koppelt deze map aan de paginamap, en dat is de goedkoopste van de twee
/// kwaden: een klant vergelijkt het bedrag in de mail met het bedrag op zijn scherm, en twee
/// opmaakdefinities laten die twee op een dag verschillen — de vorm die in dit werk al drie keer met
/// gekopieerde CSS is misgegaan. Maandnamen komen om dezelfde reden uit
/// <see cref="HourMonths.Label(string)"/>.</para>
/// </remarks>
internal static class StatementText
{
    /// <summary>
    /// De grens waarop een naam in de onderwerpregel wordt ingekort.
    /// </summary>
    /// <remarks>
    /// Ruim boven elke echte bedrijfsnaam en ruim onder de lengte waarop een onderwerpregel in een
    /// postbuslijst onleesbaar wordt. De grens doet in de praktijk niets; hij staat er voor de dag
    /// dat iemand een heel adres in het naamveld van een klant zet.
    /// </remarks>
    internal const int NameLimit = 120;

    /// <summary>
    /// De melding bij een enumwaarde die geen naam heeft.
    /// </summary>
    /// <remarks>
    /// Deze tekst is voor een ontwikkelaar en komt op geen scherm en in geen mail. Hij bestaat omdat
    /// een gecaste enumwaarde geen betekenis heeft: er is niets zinnigs over te zeggen tegen een
    /// operator, en een zin bedenken zou die waarde legitimeren.
    /// </remarks>
    private const string Unnamed =
        "Deze waarde heeft geen naam in de enum en hoort niet te bestaan.";

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
    /// Het onderwerp van het maandoverzicht.
    /// </summary>
    /// <param name="customerName">De klantnaam zoals hij in de opslag staat.</param>
    /// <param name="month">De maand als <c>jjjj-MM</c>.</param>
    /// <returns>Bijvoorbeeld <c>Maandoverzicht augustus 2026 — Bakker B.V.</c>.</returns>
    /// <remarks>
    /// De klantnaam staat erin en niet de klantslug. Een slug is een interne aanduiding; hij staat
    /// wel in het portaaladres, maar in een onderwerpregel zou hij de indruk geven dat de klant een
    /// nummer is.
    /// </remarks>
    internal static string Subject(string customerName, string month) =>
        $"Maandoverzicht {HourMonths.Label(month)} — {OneLine(customerName, NameLimit)}";

    /// <summary>
    /// Houdt van een vrij tekstveld de eerste regel over, zonder tekens die in geen regel horen.
    /// </summary>
    /// <param name="value">De tekst uit de opslag of uit een formulier.</param>
    /// <param name="limit">De lengtegrens.</param>
    /// <returns>De tekst zoals hij in een mail mag staan. Nooit <c>null</c>.</returns>
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
    /// <see cref="Greeting"/>. Dezelfde afweging als in <c>Views/CustomerMessage.cs</c>.</para>
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

    /// <summary>
    /// De aanhef van de mail.
    /// </summary>
    /// <param name="contactName">De naam van de contactpersoon, of <c>null</c>.</param>
    /// <returns><c>Beste Jan Bakker,</c> of <c>Beste relatie,</c>.</returns>
    /// <remarks>
    /// <para>Geen naam levert "Beste relatie," op en niet "Beste ," of "Beste null,". Dat lijkt een
    /// detail en het is het soort fout dat werkelijk de deur uit gaat: het naamveld op een
    /// toegangsregel is optioneel (<see cref="AccessDocument.Name"/> is nullable), dus dit geval
    /// bestaat bij de eerste klant die met alleen een e-mailadres is vastgelegd.</para>
    ///
    /// <para><strong>Het e-mailadres is hier uitdrukkelijk niet de terugvaloptie.</strong> Dat was de
    /// eerste opzet: geen naam, dan het adres. Een aanhef "Beste jan.bakker@example.nl," verraadt aan
    /// iedereen die meeleest welk adres wij van deze persoon in onze administratie hebben staan, en
    /// bij twee ontvangers verraadt hij het adres van de ander.</para>
    /// </remarks>
    internal static string Greeting(string? contactName)
    {
        var name = OneLine(contactName, NameLimit);

        return name.Length == 0 ? "Beste relatie," : $"Beste {name},";
    }

    /// <summary>Een bedrag in euro, in de vorm die ook op het scherm staat.</summary>
    /// <param name="amount">Het bedrag.</param>
    /// <returns>Bijvoorbeeld <c>€ 125,00</c>.</returns>
    /// <remarks>
    /// Er is geen overload voor <c>decimal?</c>, en dat is opzet. Een onbekend bedrag krijgt geen
    /// opmaak omdat het niet in een mail komt: de verzending wordt geweigerd
    /// (<see cref="StatementRefusal.AmountUnknown"/>). Zou hier een methode staan die van
    /// <c>null</c> "onbekend" maakt, dan bestaat er een pad waarlangs een maandoverzicht met een gat
    /// erin alsnog verstuurbaar is — en op een factuurregel is "onbekend" niet minder verkeerd dan
    /// € 0,00.
    /// </remarks>
    internal static string Money(decimal amount) => $"€ {ContractText.Amount(amount)}";

    /// <summary>Een aantal uren.</summary>
    /// <param name="hours">Het aantal.</param>
    /// <returns>Bijvoorbeeld <c>7,5 uur</c>.</returns>
    internal static string Hours(decimal hours) => $"{ContractText.Number(hours)} uur";

    /// <summary>
    /// Het adres van het urenscherm van deze klant, voor de verwijzing in de mail.
    /// </summary>
    /// <param name="baseUri">Het adres van het portaal, met of zonder afsluitende schuine streep.</param>
    /// <param name="customerId">De klantslug.</param>
    /// <param name="month">De maand als <c>jjjj-MM</c>.</param>
    /// <returns>Het volledige adres.</returns>
    /// <remarks>
    /// <para><strong>Dit is de reden dat de urenspecificatie niet in de mail staat.</strong> De
    /// omschrijving van een urenregel is vrije tekst die door een koppeling kan zijn geschreven —
    /// de MCP-server neemt hem letterlijk over uit een gesprek met een taalmodel — en de mail is de
    /// enige plek waar zulke tekst buiten het bereik van een operator komt. Achter een aanmelding
    /// staat hij op een scherm dat een mens kan lezen en corrigeren; in een postbus staat hij
    /// definitief. De mail noemt dus de bedragen en verwijst voor de regels naar het portaal.</para>
    ///
    /// <para>De slug staat in dit adres, en dat is geen lek: het is het adres dat de klant zelf in
    /// zijn browser heeft staan.</para>
    /// </remarks>
    internal static string PortalPath(string baseUri, string customerId, string month) =>
        $"{baseUri.TrimEnd('/')}/klant/{customerId}/uren?maand={month}";

    /// <summary>
    /// Wat er op het operatorscherm staat bij een weigering.
    /// </summary>
    /// <param name="refusal">De reden.</param>
    /// <returns>Eén Nederlandse zin.</returns>
    /// <remarks>
    /// <para>Deze teksten zijn voor een operator en komen in geen enkele mail. Dat is de scheiding
    /// die de punten 13 en 14 van de fase-0-afwijkingen aanbrengen: wat een intern gegeven noemt
    /// hoort op een scherm en niet in een postbus. Ze staan hier bij de mailteksten omdat het
    /// dezelfde weigering is, en niet omdat ze dezelfde lezer hebben.</para>
    ///
    /// <para><strong>Elke benoemde waarde heeft een eigen tak, en de <c>_</c>-tak werpt.</strong>
    /// Dat is niet hetzelfde als een <c>default</c> met een terugvaltekst, en het verschil is de
    /// reden dat het zo staat. Een nieuwe waarde in <see cref="StatementRefusal"/> levert CS8509 op
    /// — de compiler meldt dat de switch niet volledig is — en dit project staat op nul
    /// waarschuwingen, dus die melding komt niet weg te zakken. Een terugvaltekst zou hem stil
    /// opvangen en dan staat er bij een nieuwe weigering een zin die niet over die weigering gaat.
    /// De <c>_</c>-tak is er alleen voor een waarde die geen enkele naam heeft — een cast van een
    /// getal — en die hoort te werpen en niet te vertellen.</para>
    /// </remarks>
    internal static string Refusal(StatementRefusal refusal) => refusal switch
    {
        StatementRefusal.None =>
            "Er is niets geweigerd.",
        StatementRefusal.MailNotConfigured =>
            "Mailen is niet ingericht: er is geen Communication Services-endpoint of geen "
            + "afzenderadres geconfigureerd. Er is niets verstuurd en niets vastgelegd.",
        StatementRefusal.NoFigures =>
            "Over deze maand zijn geen bedragen gemeten. Een maandoverzicht zonder bedragen zegt "
            + "niets; er is niets verstuurd.",
        StatementRefusal.AmountUnknown =>
            "Een bedrag dat in het overzicht hoort is onbekend. Onbekend is niet nul, en € 0,00 in "
            + "een maandoverzicht is geen leeg veld maar een verkeerd bedrag. Er is niets verstuurd.",
        StatementRefusal.AmountsIncomplete =>
            "De kostenmeting over deze maand is nog niet volledig. Een overzicht met een halve dag "
            + "Azure erin is niet aan het bedrag te zien; er is niets verstuurd.",
        StatementRefusal.NoRecipient =>
            "Er is bij deze klant geen contactpersoon met de aanduiding "
            + $"\"{PortalAccessRoles.Administrator}\" vastgelegd. Zonder ontvanger is er niets te "
            + "versturen; leg de contactpersoon vast op het contractscherm.",
        StatementRefusal.RecipientInvalid =>
            "Een van de vastgelegde e-mailadressen is niet als ontvanger te gebruiken. Er is niets "
            + "verstuurd — ook niet naar de adressen die wél klopten.",
        StatementRefusal.MonthNotClosed =>
            "Er is geen afgesloten maand om een overzicht van te maken: deze maand is nog niet "
            + "voorbij, of het is geen maand. Een maandoverzicht over een lopende maand noemt een "
            + "bedrag dat morgen anders is; er is niets verstuurd.",
        StatementRefusal.Rejected =>
            "Communication Services heeft het bericht geweigerd. Er is niets verstuurd. De reden "
            + "staat in de logregel bij deze poging en niet hier — een foutmelding van een "
            + "dienstverlener is geen tekst voor een scherm waar een klantnaam boven staat.",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, Unnamed),
    };

    /// <summary>
    /// Wat er op het operatorscherm staat bij een verzendtoestand.
    /// </summary>
    /// <param name="state">De toestand.</param>
    /// <returns>Eén woord of korte woordgroep, met een glyph erbij zoals §8 vraagt.</returns>
    /// <remarks>
    /// "Verstuurd" en niet "Afgeleverd". Communication Services heeft het bericht aangenomen; wat er
    /// daarna in het postsysteem van de klant gebeurt weten wij niet. Zie
    /// <see cref="StatementSendState.Sent"/>, en §7 van het haalbaarheidsrapport voor dezelfde
    /// afweging bij de status van een SnelStart-factuur.
    /// </remarks>
    internal static string StateLabel(StatementSendState state) => state switch
    {
        StatementSendState.Sent => "Verstuurd",
        StatementSendState.NotSent => "Niet verstuurd",
        StatementSendState.Unknown => "Onbekend",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, Unnamed),
    };

    /// <summary>
    /// De uitleg bij een verzendtoestand, voor de operator.
    /// </summary>
    /// <param name="state">De toestand.</param>
    /// <returns>Eén of twee Nederlandse zinnen.</returns>
    internal static string StateNotice(StatementSendState state) => state switch
    {
        StatementSendState.Sent =>
            "Communication Services heeft het bericht aangenomen. Dat is niet hetzelfde als "
            + "afgeleverd: een spamfilter of een volle postbus komt daarna, en dat zien wij niet.",
        StatementSendState.NotSent =>
            "Er is zeker niets verstuurd. Deze maand is opnieuw te versturen.",
        StatementSendState.Unknown =>
            "Het is niet vast te stellen of dit overzicht is aangekomen. Het portaal probeert dit "
            + "niet opnieuw: een tweede maandoverzicht naar dezelfde klant is erger dan een dag "
            + "later mailen. Stel vast wat er is gebeurd en leg dat hieronder vast; daarna kan er "
            + "opnieuw worden verstuurd.",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, Unnamed),
    };

    /// <summary>
    /// De maand als <c>jjjj-MM</c>, in de Nederlandse kalender.
    /// </summary>
    /// <param name="moment">Het moment.</param>
    /// <returns>Bijvoorbeeld <c>2026-08</c>.</returns>
    /// <remarks>
    /// Via <see cref="HourMonths.Of(DateTimeOffset)"/> en niet met een eigen omzetting, zodat de
    /// maandgrens van dit scherm dezelfde is als die van het urenscherm. Zouden die twee
    /// verschillen, dan hoort werk van 31 juli op het ene scherm bij juli en op het andere bij
    /// augustus.
    /// </remarks>
    internal static string MonthOf(DateTimeOffset moment) => HourMonths.Of(moment);

    /// <summary>
    /// De maand vóór de maand waarin dit moment valt.
    /// </summary>
    /// <param name="moment">Het moment.</param>
    /// <returns>Bijvoorbeeld <c>2026-07</c> op een dag in augustus 2026.</returns>
    /// <remarks>
    /// De standaardmaand van het scherm. Een maandoverzicht gaat over een afgesloten maand, dus over
    /// de vorige — niet over de maand waarin je kijkt.
    /// </remarks>
    internal static string PreviousMonthOf(DateTimeOffset moment)
    {
        var day = TimeZoneInfo.ConvertTime(moment, Views.PortalTimeZone.Display).DateTime;

        return new DateTime(day.Year, day.Month, 1, 0, 0, 0, DateTimeKind.Unspecified)
            .AddMonths(-1)
            .ToString("yyyy-MM", CultureInfo.InvariantCulture);
    }
}
