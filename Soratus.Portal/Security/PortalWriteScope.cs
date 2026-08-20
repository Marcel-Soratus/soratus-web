namespace Soratus.Portal.Security;

/// <summary>
/// Het bewijs dat de huidige gebruiker portaaleigen gegevens mag <em>wijzigen</em>.
/// </summary>
/// <remarks>
/// <para><strong>Waarom dit naast <see cref="OperatorScope"/> bestaat.</strong> Tot fase 2 leest dit
/// portaal alleen; elke scope was daarmee een leesrecht. De rolmatrix (§2) zegt over contract en
/// toegang: klant <em>lezen</em>, operator <em>lezen + bewerken</em>. Dat zijn twee rechten, en een
/// leesrecht is geen schrijfrecht.</para>
///
/// <para>Het zou kunnen lijken alsof <see cref="OperatorCustomerScope"/> al genoeg is: die bewijst
/// immers dat je operator bent. Maar dat type wordt aan élke operatorpagina gegeven om iets te
/// kunnen <em>tonen</em> — de agentlijst, het detail, de logs. Zou een schrijfmethode dat type
/// accepteren, dan heeft elke operatorpagina die een klant rendert al een schrijfbewijs in handen,
/// en is aan geen enkele signatuur meer te zien welke aanroep de opslag verandert. Met een eigen
/// type staat in de signatuur van de methode dat hij schrijft, en is er precies één manier om het
/// argument te bemachtigen: er expliciet om vragen bij
/// <see cref="ICustomerScopeResolver.ResolveWriteAsync(System.Security.Claims.ClaimsPrincipal?,System.Threading.CancellationToken)"/>.
/// </para>
///
/// <para>Net als de andere scopes is de constructor <c>internal</c> en roept alleen
/// <see cref="CustomerScopeResolver"/> hem aan. Voeg hier geen fabrieksmethode toe.</para>
///
/// <para><strong>Wat dit type níet draagt: een opslaglocatie.</strong> Daarin wijkt het bewust af
/// van <see cref="CustomerScope"/>. Die draagt zijn locatie omdat telemetrie <em>per klant</em> in
/// een eigen account staat: daar is de locatie de isolatiegrens, en een verkeerde locatie betekent
/// de gegevens van iemand anders. De portaaleigen opslag is er één, voor alle klanten samen — zie
/// <see cref="Soratus.Portal.Data.PortalDataLocation"/> voor waarom dat zo hoort. De grens ligt daar
/// binnen de container, op de partitiesleutel, en die komt uit
/// <see cref="CustomerWriteScope.CustomerId"/>. Een locatie op deze scope zou suggereren dat er iets
/// te kiezen valt.</para>
/// </remarks>
public sealed class PortalWriteScope
{
    /// <summary>
    /// Alleen <see cref="CustomerScopeResolver"/> mag scopes maken.
    /// </summary>
    internal PortalWriteScope(OperatorScope operatorScope)
    {
        Operator = operatorScope;
    }

    /// <summary>Het bewijs van de operatorrol waar dit schrijfrecht uit volgt.</summary>
    public OperatorScope Operator { get; }

    /// <summary>
    /// Wie de wijziging op zijn naam krijgt: de naam uit het token, of anders de <c>oid</c>.
    /// </summary>
    /// <remarks>
    /// Gaat mee als <c>changedBy</c> en <c>grantedBy</c> op elk document dat wordt geschreven. Niet
    /// omdat er een audittrail is — die is er niet, zie het rapport — maar omdat het laatste
    /// wijzigingsmoment met een naam erbij het verschil maakt tussen "dit stond hier al" en "dit
    /// heeft iemand vorige week veranderd".
    /// </remarks>
    public string Actor =>
        string.IsNullOrWhiteSpace(Operator.DisplayName) ? Operator.Subject : Operator.DisplayName;
}
