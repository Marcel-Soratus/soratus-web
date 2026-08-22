using Soratus.Portal.Security;
using Soratus.Portal.Views;

namespace Soratus.Portal.Support;

/// <summary>
/// De enige implementatie van <see cref="ISupportViews"/>.
/// </summary>
/// <remarks>
/// <para><strong>Twee projecties uit dezelfde documenten, en de één komt niet uit de ander.</strong> Dat
/// is punt 14 van de fase-0-afwijkingen: bestond er een pad van de volle vorm naar de smalle, dan is er
/// een pad waarlangs een veld kan meeliften. Wat ze delen is de <em>bubbel</em>, en dat is een type dat
/// beide rollen mogen zien — zie <see cref="SupportBubble"/> voor waarom dat hier geen inconsistentie
/// is.</para>
///
/// <para><strong>De reactietermijn komt van het contractscherm en niet uit een tweede lezing.</strong>
/// <see cref="IContractViews"/> levert per rol het type dat die rol mag zien, en de SLA staat op beide.
/// Zou deze klasse het contractdocument zelf lezen, dan stond er een tweede projectie van het contract
/// in het portaal — en dan is de SLA die de klant op support leest niet gegarandeerd de SLA op zijn
/// contractkaart.</para>
/// </remarks>
internal sealed class SupportProjection(
    ISupportStore store,
    IContractViews contracts,
    TimeProvider timeProvider) : ISupportViews
{
    /// <inheritdoc />
    public async Task<CustomerSupportView> BuildThreadAsync(
        CustomerScope scope,
        SupportThreadQuery query,
        SupportFirstLineState firstLine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        var page = await store.ReadThreadAsync(scope, query, cancellationToken).ConfigureAwait(false);
        var contract = await contracts.BuildContractAsync(scope, cancellationToken).ConfigureAwait(false);

        return new CustomerSupportView
        {
            CustomerId = scope.CustomerId,
            DisplayName = scope.DisplayName,
            GeneratedAt = timeProvider.GetUtcNow(),

            // Alleen de bubbels die te tonen zijn. Wat er wegvalt is voor de klant niets — hij ziet
            // het bericht niet — en voor de operator een regel in OperatorSupportView.Unusable. Dat is
            // de spiegel: weglaten zonder spiegel is stil verliezen.
            Bubbles = [.. page.Messages.Select(m => Bubble(scope.CustomerId, m)).OfType<SupportBubble>()],
            OlderPath = page.OlderThan is { } older
                ? SupportText.OlderPath(scope.CustomerId, older)
                : null,
            FirstLine = firstLine,
            SlaNotice = SupportText.SlaNotice(contract.Sla),
            EmptyNotice = EmptyForCustomer(firstLine),
        };
    }

    /// <inheritdoc />
    public async Task<OperatorSupportView> BuildThreadAsync(
        CustomerWriteScope scope,
        SupportThreadQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        var page = await store.ReadThreadAsync(scope, query, cancellationToken).ConfigureAwait(false);
        var contract = await contracts.BuildContractAsync(scope, cancellationToken).ConfigureAwait(false);

        return new OperatorSupportView
        {
            CustomerId = scope.CustomerId,
            DisplayName = scope.DisplayName,
            GeneratedAt = timeProvider.GetUtcNow(),
            Bubbles = [.. page.Messages.Select(m => Bubble(scope.CustomerId, m)).OfType<SupportBubble>()],
            OlderPath = page.OlderThan is { } older
                ? SupportText.OlderPath(scope.CustomerId, older)
                : null,
            SlaNotice = SupportText.SlaNotice(contract.Sla),

            // Operator-only, en dit zijn de twee lijsten die het klanttype niet heeft.
            Handoffs =
            [
                .. page.Messages
                    .Where(m => m is { Author: SupportAuthor.FirstLine, Escalation: not null })
                    .Select(m => new OperatorHandoff(m.CreatedAt, m.Escalation!.Value)),
            ],
            Unusable =
            [
                .. page.Messages
                    .Select(m => (Message: m, Why: WhyUnusable(scope.CustomerId, m)))
                    .Where(pair => pair.Why is not null)
                    .Select(pair => new OperatorUnusableMessage(
                        pair.Message.CreatedAt,
                        pair.Message.Id,
                        pair.Why!)),
            ],
            EmptyNotice =
                "Deze klant heeft nog niets gevraagd en er is nog niets naar hem gestuurd. Wat je "
                + "hier schrijft komt in zijn portaal te staan.",
        };
    }

    // ── Binnenwerk ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maakt van één document een bubbel, of <c>null</c> als dat niet kan.
    /// </summary>
    /// <remarks>
    /// <para><strong>Deze methode is de leeskant van de eis van fase 5.</strong> De schrijfkant kan geen
    /// antwoord zonder bron wégschrijven; deze kant kan er geen tónen. Dat is met opzet dubbel, om
    /// dezelfde reden als de knip van punt 13 op twee plekken staat: de schrijfkant dekt niet de
    /// documenten die langs een ander pad in de container terecht zijn gekomen, en de identiteit van
    /// het portaal heeft schrijfrecht op de hele container.</para>
    ///
    /// <para>De volgorde van de beslissingen, met de reden:</para>
    /// <list type="number">
    ///   <item><description>
    ///     <strong>Lege tekst valt af, ongeacht de afzender.</strong> Een bubbel zonder tekst zegt
    ///     niets en draagt bij de eerstelijn wél een merkteken.
    ///   </description></item>
    ///   <item><description>
    ///     <strong><see cref="SupportAuthor.Unknown"/> valt af.</strong> Zie de opmerkingen bij die
    ///     waarde: een tekst die we niet kunnen toewijzen hoort niet met een van onze twee stemmen op
    ///     een scherm.
    ///   </description></item>
    ///   <item><description>
    ///     <strong>Bij de eerstelijn gaat de escalatie vóór de grondslag.</strong> Onze schrijfkant zet
    ///     er nooit beide, dus dit geval kan alleen uit een beschadigd of vreemd document komen — en dan
    ///     is "hij wist het niet" de veilige lezing en "hier is je antwoord" de gevaarlijke. Zou de
    ///     grondslag voorgaan, dan zou een document met beide velden een bewering opleveren.
    ///   </description></item>
    ///   <item><description>
    ///     <strong>Een antwoord zonder bronregel of zonder pad valt af.</strong> Niet met een streepje,
    ///     niet met "bron onbekend": weg. Zie <see cref="SupportAnswerBubble"/> — er is geen vorm om het
    ///     in te zetten.
    ///   </description></item>
    /// </list>
    /// </remarks>
    private static SupportBubble? Bubble(string customerId, SupportMessageDocument message)
    {
        var text = SupportBody.Clean(message.Text);

        if (text.Length == 0)
        {
            return null;
        }

        switch (message.Author)
        {
            case SupportAuthor.Customer:
                return new SupportSaidBubble(message.CreatedAt, text, message.Who, fromCustomer: true);

            case SupportAuthor.Soratus:
                return new SupportSaidBubble(message.CreatedAt, text, message.Who, fromCustomer: false);

            case SupportAuthor.FirstLine when message.Escalation is not null:
                return new SupportHandoffBubble(message.CreatedAt, text);

            case SupportAuthor.FirstLine
                when message.GroundKind is { } kind
                    && SupportText.GroundLabel(kind, message.GroundKey) is { } label
                    && SupportText.GroundPath(customerId, kind, message.GroundKey) is { } path:
                return new SupportAnswerBubble(message.CreatedAt, text, label, path);

            default:
                return null;
        }
    }

    /// <summary>
    /// Waarom dit bericht niet als bubbel te tonen is, of <c>null</c> als het gewoon kan.
    /// </summary>
    /// <remarks>
    /// <para>Precies de tegenhanger van <see cref="Bubble"/>, en met opzet een eigen functie in plaats
    /// van een tweede lijst die de eerste aanvult. De reden staat in de werkwijze van dit project: elke
    /// "de klant ziet dit niet" krijgt een spiegel, want anders is een scherm dat voor iedereen niets
    /// rendert ook groen.</para>
    ///
    /// <para>Er staat een test op dat deze twee functies elkaar precies aanvullen: elk document levert
    /// óf een bubbel óf een reden op, en nooit beide en nooit geen van beide. Zonder die test zou een
    /// bericht dat wegvalt zonder reden geen spoor achterlaten.</para>
    /// </remarks>
    private static string? WhyUnusable(string customerId, SupportMessageDocument message)
    {
        if (Bubble(customerId, message) is not null)
        {
            return null;
        }

        if (SupportBody.Clean(message.Text).Length == 0)
        {
            return "Er bleef geen tekst over na het schonen.";
        }

        return message.Author switch
        {
            SupportAuthor.Unknown =>
                "De afzender is niet toe te wijzen. Dit bericht staat niet in het portaal van de klant.",
            SupportAuthor.FirstLine =>
                "Een antwoord van de eerstelijn zonder aanwijsbare bron. Dit bericht staat niet in het "
                + "portaal van de klant.",
            _ => "Niet te tonen.",
        };
    }

    /// <summary>
    /// De melding als de draad van de klant nog leeg is.
    /// </summary>
    /// <remarks>
    /// <para>Twee teksten, want de twee toestanden zijn twee verschillende mededelingen. Is er geen
    /// eerstelijn, dan hoort er niet te staan dat er direct antwoord komt — dat is dezelfde eerlijkheid
    /// als "historische logs lopen ~1 min achter" uit de ontwerpregels van de spec.</para>
    /// </remarks>
    private static string EmptyForCustomer(SupportFirstLineState firstLine) =>
        firstLine == SupportFirstLineState.Available
            ? "Nog geen berichten. Stel je vraag; komt er geen antwoord dat op je eigen gegevens rust, "
              + "dan gaat hij door naar een mens van Soratus."
            : "Nog geen berichten. Stel je vraag; een mens van Soratus antwoordt.";
}
