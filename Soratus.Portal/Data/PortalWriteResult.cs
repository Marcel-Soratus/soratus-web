namespace Soratus.Portal.Data;

/// <summary>
/// Hoe een schrijfactie is afgelopen.
/// </summary>
public enum PortalWriteStatus
{
    /// <summary>Weggeschreven. <see cref="PortalWriteResult{T}.Value"/> is het nieuwe document.</summary>
    Saved,

    /// <summary>
    /// Niet weggeschreven omdat de invoer niet klopt. Het formulier hoort de melding te tonen en
    /// de ingevulde waarden te laten staan.
    /// </summary>
    Invalid,

    /// <summary>
    /// Niet weggeschreven omdat er intussen iemand anders was.
    /// <see cref="PortalWriteResult{T}.Current"/> draagt wat er nu in de opslag staat.
    /// </summary>
    Conflict,
}

/// <summary>
/// De uitkomst van een schrijfactie op de portaaleigen opslag.
/// </summary>
/// <typeparam name="T">Het documenttype dat is geschreven.</typeparam>
/// <remarks>
/// <para><strong>Een resultaat en geen exception, met één uitzondering.</strong> Een verouderde
/// etag en een ongeldig e-mailadres zijn geen storingen: ze horen bij het normale gebruik van een
/// formulier door twee mensen tegelijk. Er hoort een scherm op te volgen en geen foutpagina.
/// Wát wél werpt is de opslag die niet bereikbaar is of waar het schrijfrecht ontbreekt — zie
/// <see cref="PortalDataNotProvisionedException"/>. Dat is een inrichtingsfout en die hoort
/// luidruchtig te zijn.</para>
///
/// <para><strong>Bij een conflict komt het huidige document mee.</strong> Dat is het verschil
/// tussen "opslaan is mislukt, probeer opnieuw" en een scherm dat kan tonen wat er intussen
/// veranderd is. Zonder die waarde is het enige dat de operator kan doen zijn eigen invoer
/// nogmaals versturen, en dan is de laatste schrijver alsnog de winnaar — precies het stille
/// overschrijven dat de etag hoort te voorkomen.</para>
/// </remarks>
public sealed class PortalWriteResult<T>
    where T : class
{
    private PortalWriteResult(PortalWriteStatus status, T? value, T? current, string? message)
    {
        Status = status;
        Value = value;
        Current = current;
        Message = message;
    }

    /// <summary>Hoe het is afgelopen.</summary>
    public PortalWriteStatus Status { get; }

    /// <summary>Het weggeschreven document, met zijn nieuwe etag. Alleen bij <see cref="PortalWriteStatus.Saved"/>.</summary>
    public T? Value { get; }

    /// <summary>
    /// Wat er nu in de opslag staat. Alleen bij <see cref="PortalWriteStatus.Conflict"/>, en
    /// <c>null</c> als het document intussen is verwijderd.
    /// </summary>
    public T? Current { get; }

    /// <summary>De melding voor het scherm, in het Nederlands. Leeg bij succes.</summary>
    public string? Message { get; }

    /// <summary>Of de schrijfactie is gelukt.</summary>
    public bool IsSaved => Status == PortalWriteStatus.Saved;

    /// <summary>Gelukt.</summary>
    /// <param name="value">Het weggeschreven document.</param>
    /// <returns>Het resultaat.</returns>
    public static PortalWriteResult<T> Saved(T value) =>
        new(PortalWriteStatus.Saved, value, current: null, message: null);

    /// <summary>De invoer klopt niet.</summary>
    /// <param name="message">Wat er niet klopt, in het Nederlands.</param>
    /// <returns>Het resultaat.</returns>
    public static PortalWriteResult<T> Invalid(string message) =>
        new(PortalWriteStatus.Invalid, value: null, current: null, message);

    /// <summary>Iemand anders was er eerder.</summary>
    /// <param name="message">Wat er is gebeurd, in het Nederlands.</param>
    /// <param name="current">Wat er nu in de opslag staat, of <c>null</c> als het weg is.</param>
    /// <returns>Het resultaat.</returns>
    public static PortalWriteResult<T> Conflict(string message, T? current) =>
        new(PortalWriteStatus.Conflict, value: null, current, message);
}
