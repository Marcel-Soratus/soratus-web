using Soratus.Portal.Data;
using Soratus.Portal.Security;

namespace Soratus.Portal.Views;

/// <summary>
/// Bouwt de viewmodels van de tabbladen op het agentdetail: Logs, Runs en Configuratie (§3.3).
/// </summary>
/// <remarks>
/// <para><strong>Waarom dit niet op <see cref="IPortalViews"/> staat.</strong> Die interface bouwt
/// een <em>pagina</em> op: één aanroep, één moment, alle getallen uit dezelfde lijst. De methoden
/// hier bedienen de <em>interacties</em> op een pagina die er al staat — een chip die aan gaat, een
/// zoekterm, "meer laden", een tik van de live tail. Dat zijn aanroepen die tientallen keren per
/// bezoek langskomen met wisselende argumenten, en dat is een ander soort methode dan
/// <c>BuildOverviewAsync</c>. Ze op één interface zetten zou van die interface het verzamelpunt van
/// alles maken.</para>
///
/// <para>Dezelfde afspraken gelden onverkort. Elke methode begint met een scope; er is nergens een
/// losse <c>string customerId</c>. De overload bepaalt de vorm die je terugkrijgt: een
/// <see cref="CustomerScope"/> levert het klanttype, een <see cref="OperatorCustomerScope"/> het
/// operatortype. En alles wat te rekenen valt is hier al gerekend.</para>
///
/// <para><strong>Elke methode doet zijn eigen zichtbaarheidscontrole, en dat is geen dubbel
/// werk.</strong> Dat <c>BuildAgentDetailAsync</c> voor een acceptatie-agent <c>null</c> geeft aan
/// een klant beschermt alleen de kop van het scherm. De tabbladen zijn losse aanroepen met de
/// agentnaam erin; zonder eigen controle zou de logweergave van een acceptatie-agent op te vragen
/// zijn terwijl het detail 404 geeft — en dan is het bestaan van die agent alsnog vast te stellen.
/// Elke klant-overload hier controleert daarom zelf dat de agent bestaat, van deze klant is én in
/// productie draait, en geeft <c>null</c> als een van de drie niet klopt. Alle drie hetzelfde
/// antwoord, om dezelfde reden als bij het detail.</para>
///
/// <para>Kosten: die controle is een point read op de agentcontainer, de goedkoopste leesactie die
/// Cosmos kent (gemeten ongeveer 1 RU). Dat is de prijs van een grens die niet afhangt van de vraag
/// of het scherm hem eerder al heeft gesteld.</para>
/// </remarks>
public interface IAgentDetailViews
{
    /// <summary>
    /// Bouwt het tabblad Logs op zoals de klant het ziet.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="agentName">De technische naam uit de URL.</param>
    /// <param name="query">Niveaufilter, zoekterm, runId en paginering.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De weergave, of <c>null</c> als de agent niet bestaat, van een andere klant is, óf niet in
    /// productie draait.
    /// </returns>
    /// <remarks>
    /// <para><strong>De klantvariant draagt geen <c>extra</c>.</strong> Dat is geen weglating in de
    /// projectie maar een ander type: <see cref="CustomerLogLine"/> heeft het veld niet. Zie
    /// <see cref="CustomerAgentLogsView"/> voor waarom, en <c>fase-0-afwijkingen.md</c> §12 voor het
    /// besluit.</para>
    ///
    /// <para><strong>Bij "meer laden" hoort <see cref="LogQuery.AsOf"/> mee te gaan.</strong> Geef
    /// <see cref="CustomerAgentLogsView.GeneratedAt"/> van de eerste pagina terug in dat veld, samen met het
    /// vervolgtoken. Zonder die bovengrens kijkt pagina twee naar een andere verzameling dan pagina
    /// één en kan een regel dubbel of helemaal niet in beeld komen. Een vervolgtoken zonder
    /// <see cref="LogQuery.AsOf"/> levert daarom een <see cref="ArgumentException"/> op, en niet
    /// stilzwijgend een verschoven lijst.</para>
    /// </remarks>
    Task<CustomerAgentLogsView?> BuildLogsAsync(
        CustomerScope scope,
        string agentName,
        LogQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bouwt het tabblad Logs op zoals de operator het ziet.
    /// </summary>
    /// <param name="scope">Het operatorrecht op deze klant.</param>
    /// <param name="agentName">De technische naam uit de URL.</param>
    /// <param name="query">Niveaufilter, zoekterm, runId en paginering.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave, of <c>null</c> als de agent niet bestaat of van een andere klant is.</returns>
    /// <remarks>
    /// Geen omgevingsfilter: de operator hoort de logs van zijn acceptatie-agents te kunnen lezen.
    /// Dat is precies waarom deze overload bestaat en de klantweergave niet met een vlag te
    /// verruimen is.
    /// </remarks>
    Task<OperatorAgentLogsView?> BuildLogsAsync(
        OperatorCustomerScope scope,
        string agentName,
        LogQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Haalt op wat er ná de cursor bij is gekomen, voor de klantweergave. Dit is de live tail.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="agentName">De technische naam uit de URL.</param>
    /// <param name="query">
    /// Dezelfde filters als de tabel. Paginering en <see cref="LogQuery.AsOf"/> doen niet mee.
    /// </param>
    /// <param name="since">
    /// Waar de lezer is gebleven: <see cref="CustomerAgentLogsView.TailFrom"/> bij de eerste tik, daarna
    /// <see cref="CustomerAgentLogTail.Cursor"/> van de vorige.
    /// </param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De nieuwe regels, of <c>null</c> als de agent niet (meer) zichtbaar is.</returns>
    /// <remarks>
    /// De filters gaan bewust mee. Een tail die alles doorlaat zou regels in een gefilterde tabel
    /// schuiven die er volgens het filter niet in horen, en dan is de tabel iets anders dan zijn
    /// chips beweren.
    /// </remarks>
    Task<CustomerAgentLogTail?> TailLogsAsync(
        CustomerScope scope,
        string agentName,
        LogQuery query,
        LogCursor since,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Haalt op wat er ná de cursor bij is gekomen, voor de operatorweergave.
    /// </summary>
    /// <param name="scope">Het operatorrecht op deze klant.</param>
    /// <param name="agentName">De technische naam uit de URL.</param>
    /// <param name="query">Dezelfde filters als de tabel.</param>
    /// <param name="since">Waar de lezer is gebleven.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De nieuwe regels, of <c>null</c> als de agent niet bestaat.</returns>
    Task<OperatorAgentLogTail?> TailLogsAsync(
        OperatorCustomerScope scope,
        string agentName,
        LogQuery query,
        LogCursor since,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bouwt het tabblad Runs op zoals de klant het ziet.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="agentName">De technische naam uit de URL.</param>
    /// <param name="pageSize">Hoeveel runs per pagina, of <c>null</c> voor de standaard.</param>
    /// <param name="continuationToken">Het vervolgtoken van de vorige pagina.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De weergave, of <c>null</c> als de agent niet bestaat, van een andere klant is, óf niet in
    /// productie draait.
    /// </returns>
    Task<AgentRunsView?> BuildRunsAsync(
        CustomerScope scope,
        string agentName,
        int? pageSize = null,
        string? continuationToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bouwt het tabblad Runs op zoals de operator het ziet.
    /// </summary>
    /// <param name="scope">Het operatorrecht op deze klant.</param>
    /// <param name="agentName">De technische naam uit de URL.</param>
    /// <param name="pageSize">Hoeveel runs per pagina, of <c>null</c> voor de standaard.</param>
    /// <param name="continuationToken">Het vervolgtoken van de vorige pagina.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave, of <c>null</c> als de agent niet bestaat of van een andere klant is.</returns>
    Task<AgentRunsView?> BuildRunsAsync(
        OperatorCustomerScope scope,
        string agentName,
        int? pageSize = null,
        string? continuationToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bouwt het read-only tabblad Configuratie op zoals de klant het ziet.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="agentName">De technische naam uit de URL.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De weergave, of <c>null</c> als de agent niet bestaat, van een andere klant is, óf niet in
    /// productie draait.
    /// </returns>
    Task<CustomerAgentConfigurationView?> BuildConfigurationAsync(
        CustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Bouwt het read-only tabblad Configuratie op zoals de operator het ziet.
    /// </summary>
    /// <param name="scope">Het operatorrecht op deze klant.</param>
    /// <param name="agentName">De technische naam uit de URL.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De weergave, of <c>null</c> als de agent niet bestaat of van een andere klant is.</returns>
    Task<OperatorAgentConfigurationView?> BuildConfigurationAsync(
        OperatorCustomerScope scope,
        string agentName,
        CancellationToken cancellationToken = default);
}
