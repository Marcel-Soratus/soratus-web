using Microsoft.Extensions.Logging.Abstractions;
using Soratus.Portal.Security;
using Soratus.Portal.Views;

namespace Soratus.Portal.Tests.Hulpmiddelen;

/// <summary>
/// De weergavelaag van het contractscherm voor de zichtbaarheidstests: de échte
/// <c>ContractViews</c> op een <see cref="Vasteportaalopslag"/>, met een stilstaande klok.
/// </summary>
/// <remarks>
/// <para>Bewust niet een eigen implementatie van <see cref="IContractViews"/> die de viewmodellen
/// met de hand vult. Dat is dezelfde afweging als bij <see cref="Weergavelaag"/>: wat er op het
/// scherm te zien is hoort uit dezelfde projectie te komen als in productie. Een fixture die het
/// klantpad stilletjes armer vult zou een zichtbaarheidstest groen laten staan omdat de fixture al
/// filterde en niet omdat de scheiding werkt.</para>
///
/// <para>Bijkomend en niet onbelangrijk: deze klasse noemt geen enkel veld van
/// <see cref="CustomerContractView"/> of <see cref="OperatorContractView"/> bij naam. Een nieuw of
/// hernoemd veld op die typen — of een melding die anders gaat heten — breekt hier dus niets, en
/// hoeft ook niets te breken: wat er op staat is een beslissing van de weergavelaag en die wordt op
/// typeniveau al bewaakt in <c>ContractZichtbaarheidTests</c>.</para>
///
/// <para><strong>De enige afwijking: de Entra-toestanden.</strong> Zie
/// <paramref name="metAlleEntratoestanden"/>.</para>
/// </remarks>
/// <param name="opslag">De opslag met de vaste gegevens.</param>
/// <param name="klanten">De klantenlijst, of <c>null</c> voor <see cref="Autorisatiebron.Standaard"/>.</param>
/// <param name="metAlleEntratoestanden">
/// Spreidt de toegangsregels over de drie waarden van <see cref="AccessEntraState"/> in plaats van
/// ze allemaal op <see cref="AccessEntraState.Unknown"/> te laten staan.
///
/// <para>Standaard <c>false</c>, en dat is met opzet de productiegetrouwe stand: het portaal heeft
/// geen leesrecht op Entra, dus vandaag is elke regel onbekend. Maar "actief" en "ontbreekt" zijn
/// geen dode waarden — ze bestaan omdat de controle erbij te zetten is zodra dat leesrecht er is, en
/// zonder deze vlag is er geen manier om te zien wat het scherm er dán mee doet. Dezelfde vorm en
/// dezelfde reden als <c>alleenBuitenProductie</c> in <see cref="VastePortaalweergaven"/>: een stand
/// die de productiecode vandaag niet oplevert, maar die het scherm hoort te kunnen tonen.</para>
///
/// <para>Zet hem <em>niet</em> aan in een test die naar de gewone klantcopy kijkt. De uitleg bij
/// "actief" en "ontbreekt" gaat over de app-rol in Entra en gebruikt daarvoor het woord rol — terecht,
/// want dat is er echt een — en dat is een ander onderwerp dan de aanduiding binnen een klant.</para>
/// </param>
internal sealed class VasteContractweergaven(
    Vasteportaalopslag opslag,
    IEnumerable<CustomerRecord>? klanten = null,
    bool metAlleEntratoestanden = false) : IContractViews
{
    private readonly IContractViews _echt = new ContractViews(
        opslag,
        Autorisatiebron.Klantenlijst(klanten ?? Autorisatiebron.Standaard()),
        Weergavelaag.Klok,
        NullLogger<ContractViews>.Instance);

    /// <inheritdoc />
    public async Task<CustomerContractView> BuildContractAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default)
    {
        var weergave = await _echt.BuildContractAsync(scope, cancellationToken).ConfigureAwait(false);

        return metAlleEntratoestanden
            ? weergave with
            {
                Access =
                [
                    .. weergave.Access.Select((rij, i) => rij with { EntraState = Toestand(i) }),
                ],
            }
            : weergave;
    }

    /// <inheritdoc />
    public async Task<OperatorContractView> BuildContractAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default)
    {
        var weergave = await _echt.BuildContractAsync(scope, cancellationToken).ConfigureAwait(false);

        return metAlleEntratoestanden
            ? weergave with
            {
                Access =
                [
                    .. weergave.Access.Select((rij, i) => rij with { EntraState = Toestand(i) }),
                ],
            }
            : weergave;
    }

    /// <summary>
    /// De toestand van de zoveelste regel: onbekend, actief, ontbrekend, en dan weer rond.
    /// </summary>
    /// <remarks>
    /// Onbekend eerst, want dat is de waarde die vandaag als enige voorkomt en dus de eerste die op
    /// het scherm hoort te kloppen.
    /// </remarks>
    private static AccessEntraState Toestand(int index) => (index % 3) switch
    {
        0 => AccessEntraState.Unknown,
        1 => AccessEntraState.Active,
        _ => AccessEntraState.Missing,
    };
}
