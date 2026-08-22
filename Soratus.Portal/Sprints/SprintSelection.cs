namespace Soratus.Portal.Sprints;

/// <summary>
/// Welke iteratie de huidige sprint is, en wat er buiten valt.
/// </summary>
/// <param name="State">De toestand. <see cref="SprintState.Current"/> als en slechts als <paramref name="Current"/> niet <c>null</c> is.</param>
/// <param name="Current">De huidige sprint, of <c>null</c>.</param>
/// <param name="Undated">De iteraties zonder begin- én einddatum, in de volgorde waarin DevOps ze gaf.</param>
/// <param name="Overlapping">De iteraties die vandaag alle bevatten, of leeg. Alleen gevuld bij <see cref="SprintState.Ambiguous"/>.</param>
/// <param name="DatedCount">Hoeveel iteraties er een begin- én een einddatum hebben.</param>
public readonly record struct SprintChoice(
    SprintState State,
    DevOpsIteration? Current,
    IReadOnlyList<DevOpsIteration> Undated,
    IReadOnlyList<DevOpsIteration> Overlapping,
    int DatedCount);

/// <summary>
/// De regel die de huidige sprint uit de <em>datums</em> van de iteraties kiest.
/// </summary>
/// <remarks>
/// <para><strong>Dit is de harde regel van deze lane en hij staat op één plek.</strong> Het portaal leidt
/// de sprint af uit de datums van een iteratie en nooit uit de naam. De naam is voor mensen:
/// <c>2026-08 Augustus</c> hernoemen naar <c>Augustus</c> mag niets verschuiven. Dat is dezelfde klasse
/// fout als een resourcegroep die uit een weergavetekst werd afgeleid — daar levert één letter verschil
/// bij Cost Management een geslaagd leeg antwoord op, en dat rolt door naar een factuur (punt 30 en
/// punt 37).</para>
///
/// <para><strong>En het veld dat je hier zou willen gebruiken bestaat en liegt.</strong> Gemeten op 22
/// augustus 2026 geeft <c>GET .../{team}/_apis/work/teamsettings/iterations</c> per iteratie een
/// <c>timeFrame</c> mee. Op dit bord stond <c>2026-08 Augustus</c> op <c>1</c> — current — en al het
/// andere op <c>2</c>. Dat lijkt precies het antwoord, tot je kijkt wat er nog op <c>2</c> stond: de drie
/// iteraties <em>zonder datums</em>, met <c>startDate: null</c> en <c>finishDate: null</c>. Dat veld kan
/// "ligt in de toekomst" dus niet onderscheiden van "heeft geen datums", en dat is exact het onderscheid
/// waar deze klasse om bestaat. <c>timeFrame</c> wordt niet gelezen.</para>
///
/// <para><strong>De vergelijking loopt op dagen en is aan beide kanten inclusief, en dat is gemeten.</strong>
/// Er is <c>31 augustus 23:59:59</c> naar DevOps verstuurd en <c>2026-08-31T00:00:00Z</c> teruggekomen: het
/// zijn datums en geen momenten. Zou de einddatum als moment worden gelezen, dan eindigt augustus op 31
/// augustus om middernacht en is de laatste dag van elke maand geen sprintdag — één dag per maand waarop
/// het portaal <see cref="SprintState.NoCurrentSprint"/> zou melden op een bord waar niets aan de hand is.
/// </para>
///
/// <para><strong>Puur, en dat is hier meer dan een gewoonte.</strong> Deze klasse heeft geen klok, geen
/// opslag en geen HTTP: de dag komt als parameter binnen. Dat is de voorwaarde om de invariant te kunnen
/// meten in plaats van zijn gevolg — een test die de sprintkeuze op de echte klok zou doen, meet elke
/// maand iets anders en is over vier maanden groen om een reden die niemand nog kan navertellen.</para>
/// </remarks>
public static class SprintSelection
{
    /// <summary>
    /// Kiest de huidige sprint uit de iteraties van een team.
    /// </summary>
    /// <param name="iterations">De iteraties zoals DevOps ze gaf. Mag leeg zijn.</param>
    /// <param name="today">
    /// De dag waarop wordt gekeken. In de weergavezone van het portaal en niet in UTC; zie de opmerking in
    /// de code voor waarom dat hier wél uitmaakt en bij de kosten niet.
    /// </param>
    /// <returns>De keuze, met de reden erbij als er geen sprint is.</returns>
    public static SprintChoice Choose(IReadOnlyList<DevOpsIteration> iterations, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(iterations);

        if (iterations.Count == 0)
        {
            return new SprintChoice(SprintState.NoIterations, null, [], [], 0);
        }

        var undated = iterations.Where(iteration => !iteration.IsDated).ToArray();
        var dated = iterations.Where(iteration => iteration.IsDated).ToArray();

        if (dated.Length == 0)
        {
            // Dit was de werkelijke toestand van dit bord tot 21 augustus 2026: drie iteraties met
            // werkitems en geen datums, dus geen huidige sprint terwijl de teaminstelling op
            // @currentIteration stond. Een andere toestand dan "geen iteraties", want de handeling is
            // een andere: datums invullen in plaats van iteraties aanmaken.
            return new SprintChoice(SprintState.NoDatedIterations, null, undated, [], 0);
        }

        // Inclusief aan beide kanten. IsDated garandeert dat beide datums er zijn, dus de ! is hier geen
        // aanname maar een gevolg — en hij staat achter een filter dat in dit bestand staat en niet
        // ergens anders.
        var containing = dated
            .Where(iteration => iteration.Start!.Value <= today && today <= iteration.Finish!.Value)
            .ToArray();

        return containing.Length switch
        {
            0 => new SprintChoice(SprintState.NoCurrentSprint, null, undated, [], dated.Length),

            1 => new SprintChoice(SprintState.Current, containing[0], undated, [], dated.Length),

            // Meer dan één. Er wordt géén sprint gekozen: twee overlappende periodes zijn twee antwoorden
            // op "welke sprint loopt nu", en stil de eerste of de kortste kiezen is een verzonnen antwoord
            // dat op het scherm niet van een juist antwoord te onderscheiden is. Dezelfde keuze als bij
            // een geslaagd leeg antwoord van Cost Management — een ambiguïteit die niet op te lossen is
            // hoort zichtbaar te zijn in plaats van weggerekend.
            _ => new SprintChoice(SprintState.Ambiguous, null, undated, containing, dated.Length),
        };
    }
}
