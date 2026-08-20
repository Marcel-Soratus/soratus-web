using Soratus.Portal.Security;

namespace Soratus.Portal.Data;

/// <summary>
/// De enige toegang tot de portaaleigen gegevens: klanten, contracten en toegang.
/// </summary>
/// <remarks>
/// <para><strong>Elke methode begint met een scope, en de scope zegt of je leest of schrijft.</strong>
/// De rolmatrix (§2) geeft de klant leesrecht op contract en toegang en de operator lezen +
/// bewerken. Dat verschil zit hier in het typesysteem: leesmethoden nemen een
/// <see cref="CustomerScope"/>, schrijfmethoden een <see cref="CustomerWriteScope"/> of een
/// <see cref="PortalWriteScope"/>. Een klantpagina kan een schrijfmethode niet aanroepen — niet
/// omdat het verboden is, maar omdat het argument niet te maken is.</para>
///
/// <para><strong>Waarom de operator leest met een schrijfbewijs.</strong> De leesoverloads zijn
/// <see cref="CustomerScope"/> (de klant) en <see cref="CustomerWriteScope"/> (de operator), en niet
/// <see cref="OperatorCustomerScope"/> zoals bij de telemetrie. Dat heeft een concrete reden:
/// <see cref="OperatorCustomerScope"/> bestaat alleen voor een klant met een ingerichte
/// telemetrie-opslag, en de klant die er nog geen heeft is precies degene wiens contract je aan het
/// invullen bent. Op het contractscherm is de operator er hoe dan ook om te bewerken; hij vraagt
/// daar dus één bewijs aan en gebruikt dat voor beide. Er wordt geen recht mee opgerekt — de
/// rolmatrix geeft de operator op deze gegevens lezen én bewerken.</para>
///
/// <para><strong>Waarom hier geen opslaglocatie in de scope zit.</strong> Bij de telemetrie draagt
/// de scope de endpoint, omdat elke klant zijn eigen account krijgt en een verkeerde locatie de
/// gegevens van een ander zijn. Deze gegevens staan op één plek voor alle klanten samen (zie
/// <see cref="PortalDataLocation"/>); de grens ligt binnen de container, op de partitiesleutel, en
/// die komt uit de scope. De locatie komt daarom uit de configuratie — precies één keer, in de
/// implementatie.</para>
///
/// <para>Er is precies één implementatie: <see cref="CosmosPortalDataStore"/>. Geen seed-variant en
/// geen in-memory variant, om dezelfde reden als bij <see cref="IAgentTelemetryStore"/>: een
/// mocklaag die blijft hangen wordt de plek waar het verschil tussen demo en werkelijkheid gaat
/// zitten.</para>
/// </remarks>
public interface IPortalDataStore
{
    /// <summary>
    /// Het contract van deze klant, zoals de klant het mag lezen (§3.5).
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het contract, of <c>null</c> als er nog geen contract is vastgelegd.</returns>
    /// <remarks>
    /// <c>null</c> is een gewone uitkomst en geen fout: een klant in onboarding heeft nog geen
    /// contract. Het scherm hoort dat te zeggen in plaats van een kaart met streepjes te tonen.
    /// </remarks>
    Task<ContractDocument?> GetContractAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Het contract van deze klant, voor de operator die het mag bewerken.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het contract, of <c>null</c> als er nog geen contract is vastgelegd.</returns>
    /// <remarks>
    /// Dit is de lezing waar het formulier op wordt gebouwd, en daarmee de plek waar de etag
    /// vandaan komt die straks als <c>If-Match</c> meegaat.
    /// </remarks>
    Task<ContractDocument?> GetContractAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Wie er namens deze klant toegang heeft, zoals de klant het mag lezen.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De toegangen, op e-mailadres gesorteerd.</returns>
    Task<IReadOnlyList<AccessDocument>> GetAccessAsync(
        CustomerScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Wie er namens deze klant toegang heeft, voor de operator die het mag wijzigen.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>De toegangen, op e-mailadres gesorteerd.</returns>
    Task<IReadOnlyList<AccessDocument>> GetAccessAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// De klantregistratie zoals hij in de opslag staat, voor het klantbeheerformulier.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// Het document, of <c>null</c> als deze klant alleen uit de configuratie komt en de migratie
    /// nog niet heeft gelopen.
    /// </returns>
    Task<CustomerDocument?> GetCustomerAsync(
        CustomerWriteScope scope,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Maakt een klant aan met zijn contract en zijn eerste toegangen (§3.9).
    /// </summary>
    /// <param name="scope">Het schrijfrecht op de portaalgegevens.</param>
    /// <param name="request">Alle velden van het formulier.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// Het klantdocument, of een conflict als de slug al bestaat, of een melding als de invoer niet
    /// klopt.
    /// </returns>
    /// <remarks>
    /// <para><strong>Dit is één transactionele batch.</strong> Klantdocument, contractdocument en
    /// elk toegangsdocument delen de partitiesleutel, en Cosmos schrijft binnen één partitiesleutel
    /// alles of niets. Er bestaat dus geen halve klant: geen klant zonder contract, en geen toegang
    /// naar een klant die niet is aangemaakt. Dat is de reden dat de partitiesleutel de klantslug is
    /// en niet het documenttype.</para>
    ///
    /// <para><strong>Wat dit níet dekt.</strong> Een klant inrichten is meer dan deze documenten: er
    /// hoort een Azure-omgeving bij en een rol-toewijzing in Entra, en die twee zijn geen Cosmos en
    /// dus niet transactioneel. Wat er dan halverwege kan stoppen, stopt daar — buiten deze aanroep.
    /// De portaalgegevens dragen dat zichtbaar: een klant zonder telemetrie-endpoint komt op het
    /// overzicht als "status onbekend", en een toegang zonder
    /// <see cref="AccessDocument.InvitedAt"/> staat op het scherm als "uitnodiging nog niet
    /// verstuurd". De halve toestand is dus leesbaar in plaats van stil.</para>
    /// </remarks>
    Task<PortalWriteResult<CustomerDocument>> CreateCustomerAsync(
        PortalWriteScope scope,
        NewCustomerRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Wijzigt de klantvelden: naam, omgeving en waar zijn telemetrie staat.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="edit">De gewijzigde velden, met de etag waarop ze zijn gebaseerd.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het nieuwe document, of een conflict, of een melding.</returns>
    Task<PortalWriteResult<CustomerDocument>> SaveCustomerAsync(
        CustomerWriteScope scope,
        CustomerEdit edit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Legt het contract van deze klant vast of wijzigt het (§3.5).
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="edit">De contractvelden, met de etag waarop ze zijn gebaseerd.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>Het nieuwe contract, of een conflict, of een melding.</returns>
    /// <remarks>
    /// Bij <see cref="ContractEdit.BasedOnETag"/> op <c>null</c> wordt het document aangemaakt en is
    /// "iemand anders was er net eerder" ook een conflict. Er is geen waarde waarmee je de controle
    /// overslaat.
    /// </remarks>
    Task<PortalWriteResult<ContractDocument>> SaveContractAsync(
        CustomerWriteScope scope,
        ContractEdit edit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Geeft één e-mailadres toegang tot deze klant (§3.5).
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="grant">Het e-mailadres, de naam en de rol.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De nieuwe toegang, of een conflict als dit adres al toegang had, of een melding bij ongeldige
    /// invoer.
    /// </returns>
    /// <remarks>
    /// Alleen Soratus deelt toegang uit; er is geen klantrol die dit mag. Dat is het besluit op de
    /// openstaande vraag uit §9, en het is de reden dat deze methode een schrijfbewijs vraagt dat
    /// een klantgebruiker niet kan krijgen.
    ///
    /// Toegang geven is hier vastleggen, niet uitnodigen. De Entra-rol blijft handwerk; zie
    /// <see cref="AccessDocument.InvitedAt"/>.
    /// </remarks>
    Task<PortalWriteResult<AccessDocument>> GrantAccessAsync(
        CustomerWriteScope scope,
        AccessGrant grant,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trekt de toegang van één e-mailadres in.
    /// </summary>
    /// <param name="scope">Het schrijfrecht op deze klant.</param>
    /// <param name="email">Het e-mailadres, in welke schrijfwijze dan ook.</param>
    /// <param name="basedOnETag">
    /// De etag van de rij zoals hij op het scherm stond, of <c>null</c> om hem te verwijderen zoals
    /// hij nu is.
    /// </param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De verwijderde toegang, of een conflict als iemand anders hem intussen heeft gewijzigd of al
    /// heeft ingetrokken.
    /// </returns>
    /// <remarks>
    /// Het document wordt echt verwijderd en niet als ingetrokken gemarkeerd. "Wie mag hierbij" is
    /// daarmee de aanwezigheid van een document en niet een veld dat iemand in een query kan
    /// vergeten. De prijs — er blijft geen spoor — staat als open punt in het rapport.
    /// </remarks>
    Task<PortalWriteResult<AccessDocument>> RevokeAccessAsync(
        CustomerWriteScope scope,
        string email,
        string? basedOnETag,
        CancellationToken cancellationToken = default);
}
