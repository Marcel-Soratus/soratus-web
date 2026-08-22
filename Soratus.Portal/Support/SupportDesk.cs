using Microsoft.Extensions.DependencyInjection;
using Soratus.Portal.Data;
using Soratus.Portal.Security;
using Soratus.Portal.Views;

namespace Soratus.Portal.Support;

/// <summary>
/// Het schrijfpad van de klantkant: de vraag wordt vastgelegd, en daarna krijgt de eerstelijn hem te
/// zien.
/// </summary>
/// <remarks>
/// <para><strong>Deze klasse is alleen vanaf het klantpad bereikbaar, en dat is de plek waar
/// "in de operatorrol springt de agent er niet tussen" wordt afgedwongen.</strong> De operator plaatst
/// zijn antwoord rechtstreeks met <see cref="ISupportStore.PostReplyAsync"/> — er is geen methode op
/// deze klasse die een <see cref="CustomerWriteScope"/> neemt, dus er bestaat geen aanroep waarmee een
/// operatorbericht de eerstelijn wakker maakt. Het rolverschil is dus niet alleen een verschil in
/// weergave maar ook in welke code er bestaat.</para>
///
/// <para><strong>De volgorde is het ontwerp: eerst de vraag vastleggen, dan pas de eerstelijn.</strong>
/// Dezelfde regel als bij de mailkant (§29.1: de claim gaat vóór de mail) en om dezelfde reden — de
/// duurste fout bepaalt de ordening. Hier is de duurste fout een vraag die verdwijnt: valt de eerstelijn
/// om, of duurt hij te lang, of gooit hij een uitzondering, dan staat de vraag er nog en ziet een mens
/// hem. De andere volgorde — eerst laten antwoorden, dan alles wegschrijven — verliest bij dezelfde
/// storing de vraag zelf, en dan wacht een klant op een antwoord dat niemand heeft gezien.</para>
///
/// <para><strong>De naad wordt opgevraagd en niet geëist.</strong> <c>GetService</c> en niet
/// <c>GetRequiredService</c>, en <see cref="ISupportFirstLine"/> staat niet in <c>Program.cs</c>. Dat is
/// geen luiheid maar het besluit uit §29 van de fase-0-afwijkingen, dat een plaatshouder achter een naad
/// uitdrukkelijk afwijst: een plaatshouder die altijd escaleert is niet te onderscheiden van een
/// eerstelijn die het niet weet, en dat is een storing die zich voordoet als werkende functionaliteit.
/// Hier is de afwezigheid een eigen toestand met een eigen tekst op het scherm — zie
/// <see cref="SupportFirstLineState"/> — dus een niet-aangesloten eerstelijn is <em>zichtbaar</em> en
/// niet stil.</para>
///
/// <para><strong>Er is geen herhaling.</strong> Geen retry, geen tweede poging bij een tijdslimiet.
/// Vaste stelregel van dit project, en hier extra: een tweede aanroep kan een tweede bubbel opleveren, en
/// twee antwoorden op één vraag is verwarrender dan geen antwoord.</para>
/// </remarks>
internal sealed class SupportDesk(
    ISupportStore store,
    IPortalViews agents,
    IHourViews hours,
    IBillingViews billing,
    IServiceProvider services,
    TimeProvider timeProvider,
    ILogger<SupportDesk> logger)
{
    /// <summary>
    /// Of er een eerstelijn is aangesloten.
    /// </summary>
    /// <returns>De toestand die op het scherm hoort te staan.</returns>
    /// <remarks>
    /// Wordt bij elke weergave opnieuw gevraagd en niet één keer bij het opstarten gecachet. Dat is
    /// geen prestatiepunt — het is één opzoeking in de container — en het levert op dat een naad die
    /// tijdens de rit wordt bijgezet meteen meedoet.
    /// </remarks>
    internal SupportFirstLineState FirstLine() =>
        services.GetService<ISupportFirstLine>() is null
            ? SupportFirstLineState.NotConfigured
            : SupportFirstLineState.Available;

    /// <summary>
    /// Legt de vraag van de klant vast en laat de eerstelijn erop reageren.
    /// </summary>
    /// <param name="scope">Het leesrecht op deze klant, van de gebruiker die de vraag stelt.</param>
    /// <param name="question">De vraag.</param>
    /// <param name="cancellationToken">Annuleringstoken.</param>
    /// <returns>
    /// De uitkomst van het vastleggen van <em>de vraag</em>. Wat de eerstelijn ervan maakte staat niet
    /// in deze uitkomst, en dat is opzet: voor de klant is zijn vraag geslaagd zodra hij in de draad
    /// staat. Een melding over de eerstelijn zou van een geslaagde handeling een halve mislukking maken.
    /// </returns>
    internal async Task<PortalWriteResult<SupportMessageDocument>> AskAsync(
        CustomerScope scope,
        SupportQuestion question,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(question);

        var posted = await store.PostQuestionAsync(scope, question, cancellationToken)
            .ConfigureAwait(false);

        if (!posted.IsSaved)
        {
            return posted;
        }

        if (services.GetService<ISupportFirstLine>() is not { } firstLine)
        {
            // Geen eerstelijn: de vraag staat er, een mens antwoordt. Er komt géén bubbel — ook geen
            // escalatiebubbel, want er is niets geëscaleerd. Wat de klant leest staat op het scherm
            // (SupportFirstLineState.NotConfigured) en niet in de draad; een bericht met een merkteken
            // van een agent die niet bestaat zou een agent suggereren die er is.
            logger.LogDebug(
                "Geen eerstelijn aangesloten; de vraag van {CustomerId} wacht op een mens.",
                scope.CustomerId);

            return posted;
        }

        SupportEnquiry? enquiry = null;
        SupportAnswer? answer = null;

        try
        {
            enquiry = new SupportEnquiry
            {
                Question = SupportBody.Clean(question.Text),
                Grounds = await GroundsAsync(scope, cancellationToken).ConfigureAwait(false),
            };

            answer = await firstLine.AnswerAsync(enquiry, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Alles opvangen, en dat is hier verdedigbaar: achter deze naad hangt code die wij niet
            // hebben geschreven, en de vraag staat al in de draad. Doorgooien zou een 500 opleveren op
            // een pagina waar de handeling van de klant is geslaagd — dan denkt hij dat zijn vraag niet
            // is aangekomen en stuurt hij hem opnieuw.
            //
            // OperationCanceledException gaat wél door: dat is de klant die zijn tabblad sluit, en dan
            // hoort er niets meer te gebeuren. Anders dan bij de mail (§29.3) valt hier niets weg door
            // niets te doen — de vraag is vastgelegd vóór deze regel.
            logger.LogError(
                exception,
                "De eerstelijn liep vast op een vraag van {CustomerId}. Er komt een escalatie in de "
                + "draad; de vraag zelf staat er al.",
                scope.CustomerId);
        }

        // enquiry kan null zijn als het opbouwen van de grondslagen zelf omviel. Dan is er niets
        // aangeboden, en dan kan er per definitie geen antwoord met een aangeboden grondslag zijn.
        enquiry ??= new SupportEnquiry { Question = SupportBody.Clean(question.Text), Grounds = [] };

        var recorded = await store
            .RecordFirstLineAsync(scope, enquiry, answer, cancellationToken)
            .ConfigureAwait(false);

        if (!recorded.IsSaved)
        {
            // Gemeld en niet doorgegeven. De vraag staat in de draad en een mens ziet hem; dat het
            // antwoord van de eerstelijn niet is vastgelegd is een storing voor ons en niet een fout
            // van de klant.
            logger.LogWarning(
                "Het antwoord van de eerstelijn op de vraag van {CustomerId} is niet vastgelegd: "
                + "{Message}",
                scope.CustomerId,
                recorded.Message);
        }

        return posted;
    }

    /// <summary>
    /// Bouwt alles waarop een antwoord mag rusten, uit de weergaven die de klant zelf al mag zien.
    /// </summary>
    /// <remarks>
    /// <para><strong>Drie lezingen per vraag, en dat is de prijs van deze vorm.</strong> De agentlijst,
    /// het urenjaar en het facturatiejaar worden bij elke gestelde vraag opgehaald. Dat is een POST van
    /// een mens die een vraag typt, dus de frequentie is die van een gesprek en niet die van een
    /// schermverversing. De prijs is bewust betaald: het alternatief is de eerstelijn een sleutel geven
    /// waarmee hij zelf gegevens opvraagt, en dan is de verzameling waarop hij zich mag baseren niet
    /// meer af te bakenen.</para>
    ///
    /// <para><strong>De weergaven zijn de klantoverloads, en dat is geen detail.</strong>
    /// <see cref="IPortalViews.BuildAgentsAsync(CustomerScope, CancellationToken)"/>,
    /// <see cref="IHourViews.BuildHoursAsync(CustomerScope, HoursQuery, string, CancellationToken)"/> en
    /// <see cref="IBillingViews.BuildBillingAsync(CustomerScope, int, CancellationToken)"/> leveren de
    /// types waar de rolgrens al in zit: geen omgevingsdetail, geen fiatteringsstroom, geen
    /// dienstuitsplitsing, geen beheeropslag. Een grondslag kan die dingen dus niet dragen, want de
    /// bron die hij leest heeft ze niet. Zou hier de operatoroverload staan, dan was die eigenschap weg
    /// en moest er een woordenlijstcontrole voor in de plaats komen.</para>
    ///
    /// <para>Het jaar is het huidige jaar in de Nederlandse zone, dezelfde grens als het urenscherm
    /// gebruikt. Zouden die twee verschillen, dan gaat een vraag op 1 januari over een ander jaar dan
    /// het scherm waar de bronregel naartoe wijst.</para>
    /// </remarks>
    private async Task<IReadOnlyList<SupportGround>> GroundsAsync(
        CustomerScope scope,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var year = TimeZoneInfo.ConvertTime(now, PortalTimeZone.Display).Year;

        var agentsView = await agents.BuildAgentsAsync(scope, cancellationToken).ConfigureAwait(false);
        var hoursView = await hours
            .BuildHoursAsync(scope, HoursQuery.ForYear(year), selectedMonth: null, cancellationToken)
            .ConfigureAwait(false);
        var billingView = await billing
            .BuildBillingAsync(scope, year, cancellationToken)
            .ConfigureAwait(false);

        return SupportGrounds.Combine(
            SupportGrounds.FromAgents(agentsView, now),
            SupportGrounds.FromHours(hoursView),
            SupportGrounds.FromBilling(billingView));
    }
}
