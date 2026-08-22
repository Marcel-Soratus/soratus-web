using Soratus.Support.FirstLine;

namespace Soratus.Portal.Support;

/// <summary>
/// De eerstelijn achter de naad: hij legt de feiten voor en leest de gekozen plaats terug.
/// </summary>
/// <remarks>
/// <para><strong>Dit is het enige stuk van de eerstelijn dat binnen <c>Soratus.Portal</c>
/// staat, en het weet niets van een model.</strong> Geen prompt, geen HTTP, geen endpoint, geen
/// deployment, geen instelling. Wat het doet is twee dingen omzetten: grondslagen naar regels tekst,
/// en een geheel getal terug naar een grondslag. Alles wat een keuze <em>maakt</em> staat in
/// <c>Soratus.Support.FirstLine</c>, buiten deze assembly, en dat project kent
/// <see cref="SupportGround"/> niet — het kan er dus geen maken, geen wijzigen en geen noemen.</para>
///
/// <para><strong>Waarom de naad niet in zijn geheel buiten het portaal ligt, zoals §46.13
/// voorschreef.</strong> Dat is niet te bouwen: een implementatie van
/// <see cref="ISupportFirstLine"/> moet de types van deze assembly zien, en <c>Program.cs</c> moet de
/// implementatie kunnen noemen om haar te registreren. Dat zijn twee projectverwijzingen in
/// tegengestelde richting, en MSBuild weigert die met <c>MSB4006</c> — gemeten, zie §47.1 van de
/// fase-0-afwijkingen. Wat er in de plaats staat is sterker dan wat §46.13 wilde bereiken: het model
/// geeft geen grondslag terug maar een <em>plaats in een lijst die het van ons heeft gekregen</em>.
/// Een verzonnen feit is daarmee niet afgeschermd maar niet uit te drukken, in geen van beide
/// richtingen.</para>
///
/// <para><strong>Dat deze klasse in theorie zélf een grondslag zou kunnen maken — de constructor is
/// <c>internal</c> en wij staan binnen die grens — is gedekt door het derde slot en niet door
/// vertrouwen.</strong> <see cref="CosmosSupportStore.Accept"/> neemt een antwoord alleen aan als de
/// grondslag erin op waarde gelijk is aan een grondslag uit <see cref="SupportEnquiry.Grounds"/> van
/// dít verzoek. <see cref="SupportGround"/> is een <c>record</c>, dus <see cref="SupportGround.Fact"/>
/// zit in die gelijkheid: een gewijzigd of verzonnen feit valt daar om en levert een escalatie op.
/// </para>
/// </remarks>
internal sealed class ChoosingFirstLine(
    IFirstLineChooser chooser,
    ILogger<ChoosingFirstLine> logger) : ISupportFirstLine
{
    /// <inheritdoc />
    /// <remarks>
    /// <para><strong>Eén lijst, één keer, en dat is de gevoeligste regel van deze klasse.</strong> De
    /// feiten worden opgebouwd uit <c>grounds</c> en de gekozen plaats wordt teruggezocht in
    /// diezelfde <c>grounds</c> — niet in een tweede lezing van <see cref="SupportEnquiry.Grounds"/>,
    /// niet in een gesorteerde of ontdubbelde kopie, niet in een lijst die na de aanroep opnieuw is
    /// opgehaald. Zou dat wel zo zijn, dan wijst een <em>correct</em> antwoord naar het verkeerde
    /// feit: de vorm blijft kloppen, er staat een bronregel onder, en niemand ziet het. Dat is de
    /// enige fout die dit ontwerp erbij heeft gekregen ten opzichte van §46, en het is onze fout en
    /// niet die van het model. De mutaties in <c>tools/mutatie-eerstelijn.py</c> gaan hierover: plus
    /// één, min één, lijst omgekeerd, en afkappen in plaats van overdragen.</para>
    ///
    /// <para><strong>Een plaats buiten het bereik wordt een escalatie en niet een uitzondering, en
    /// vooral niet de dichtstbijzijnde geldige plaats.</strong> Afkappen zou een plausibel verkeerd
    /// feit kiezen — een antwoord dat klinkt als een antwoord en op het verkeerde gegeven rust — en
    /// dat is precies de fout die dit hele ontwerp onmogelijk wil maken. Een uitzondering zou hier
    /// evenmin passen: <c>SupportDesk</c> vangt alles op en maakt er hetzelfde van, maar dan met een
    /// stacktrace in de logregel die suggereert dat er iets stuk is in plaats van dat het model
    /// buiten de lijst wees.</para>
    /// </remarks>
    public async Task<SupportAnswer?> AnswerAsync(
        SupportEnquiry enquiry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(enquiry);

        var grounds = enquiry.Grounds;

        var question = new FirstLineQuestion
        {
            Text = enquiry.Question,
            Facts = [.. grounds.Select(ground => ground.Fact)],
        };

        var choice = await chooser.ChooseAsync(question, cancellationToken).ConfigureAwait(false);

        if (choice is null)
        {
            // De kiezer kon het niet vragen of het antwoord niet lezen. null betekent op deze naad
            // hetzelfde als AnswerNotUsable; SupportDesk en de opslag maken er een escalatie van, en
            // waarom precies staat in de logregel van de kiezer zelf.
            return null;
        }

        if (choice.Handoff is { } handoff)
        {
            return SupportAnswer.Escalate(Reason(handoff));
        }

        if (choice.Index is not { } index || index < 0 || index >= grounds.Count)
        {
            logger.LogWarning(
                "De eerstelijn wees plaats {Index} aan terwijl er {Aantal} feiten zijn aangeboden. "
                + "Er wordt niets aangewezen en de vraag gaat naar een mens.",
                choice.Index,
                grounds.Count);

            return SupportAnswer.Escalate(SupportEscalation.AnswerNotUsable);
        }

        return SupportAnswer.GroundedIn(grounds[index]);
    }

    /// <summary>
    /// De escalatiereden die bij een overdracht van de kiezer hoort.
    /// </summary>
    /// <param name="handoff">Wat de kiezer teruggaf.</param>
    /// <returns>De reden zoals het portaal hem kent.</returns>
    /// <remarks>
    /// <para><strong>Drie waarden aan de ene kant, vier aan de andere, en dat is opzet.</strong>
    /// <see cref="SupportEscalation.AnswerNotUsable"/> is het oordeel van het portaal en niet van de
    /// eerstelijn (§46.9), dus er is geen overdracht die hem kan zetten. Zou hij aan de andere kant
    /// bestaan, dan kon een model een fout van het portaal nabootsen en was in de logregel niet meer
    /// te zien wie het antwoord had afgekeurd.</para>
    ///
    /// <para>Het vangnet valt op <see cref="SupportEscalation.NotSure"/> — de enige van de vier die
    /// niets beweert. Er staat een test op dat <see cref="FirstLineHandoff"/> precies drie waarden
    /// heeft, zodat een vierde waarde niet stil op dat vangnet landt maar iemand hier langs stuurt.
    /// </para>
    /// </remarks>
    private static SupportEscalation Reason(FirstLineHandoff handoff) => handoff switch
    {
        FirstLineHandoff.OutsideTheData => SupportEscalation.OutsideTheData,
        FirstLineHandoff.NeedsAHuman => SupportEscalation.NeedsAHuman,
        _ => SupportEscalation.NotSure,
    };
}
