using System.Globalization;
using Soratus.Agents.Telemetry;

namespace Soratus.Portal.Platform;

/// <summary>
/// De plannen van de beheeragents, als één cron-expressie per agent.
/// </summary>
/// <remarks>
/// <para><strong>Dit is de enige plek waar het plan van een beheeragent wordt uitgedrukt, en dat is
/// de hele reden dat deze klasse bestaat.</strong> De agent kondigt hetzelfde
/// <see cref="SoratusSchedule"/> aan dat zijn lus gebruikt om op te wachten. Daarmee blijft de
/// belofte van het contract overeind — <c>schedule</c> is de expressie waarop werkelijk wordt
/// gepland en niet een beschrijving die uit de pas kan lopen — zonder dat de bibliotheek de klok
/// overneemt. Zou de klok van de bibliotheek zijn, dan zou het werk van de collector en de melder
/// stoppen zodra de telemetrie niet is ingericht, en dat is de verkeerde
/// afhankelijkheidsrichting.</para>
///
/// <para><strong>Geen van deze methoden werpt, voor geen enkele invoer.</strong> Dat is geen
/// nettigheid maar een eis: ze worden aangeroepen vanuit de lus van een achtergronddienst en vanuit
/// de opstartcode, en op geen van die twee plekken mag een onzinnige configuratiewaarde het portaal
/// meenemen. Op de opties staan wel <c>Range</c>-annotaties, maar zonder <c>ValidateOnStart</c>
/// — en de eerste keer dat zo'n annotatie wordt gelezen is binnen een achtergronddienst, wat het
/// portaal vandaag al één keer heeft platgelegd. Vandaar dat er hier geklemd wordt in plaats van
/// gevalideerd.</para>
/// </remarks>
internal static class PlatformAgentPlans
{
    /// <summary>
    /// Het plan van de kostencollector: elke dag op het hele uur.
    /// </summary>
    /// <param name="runHourUtc">Het uur in UTC, geklemd op 0 tot en met 23.</param>
    /// <returns>De cron-expressie, uitgelegd in UTC.</returns>
    /// <remarks>
    /// UTC en niet de Nederlandse zone, want dat is de zone waarin het draaimoment is gekozen: Azure
    /// boekt in UTC en de boeking loopt ongeveer acht uur achter. Een plan dat met de zomertijd
    /// verschuift zou een afhankelijkheid zijn die er niet is.
    /// </remarks>
    internal static SoratusSchedule Costs(int runHourUtc)
    {
        var hour = Math.Clamp(runHourUtc, 0, 23);
        return SoratusSchedule.Parse(
            string.Create(CultureInfo.InvariantCulture, $"0 {hour} * * *"),
            TimeZoneInfo.Utc);
    }

    /// <summary>
    /// Het plan van de storingsmelder: elke <paramref name="intervalSeconds"/> seconden, afgerond op
    /// hele minuten.
    /// </summary>
    /// <param name="intervalSeconds">Het gevraagde interval in seconden.</param>
    /// <returns>De cron-expressie, uitgelegd in UTC.</returns>
    /// <remarks>
    /// <para><strong>Afgerond op hele minuten, en dat is een echte beperking en geen detail.</strong>
    /// <c>AgentRegistration.Schedule</c> draagt een cron-expressie, en een cron kan "elke negentig
    /// seconden" niet uitdrukken. Dat is een gat in het contract, geen keuze van deze klasse — zie de
    /// notitie in <c>docs/agent-portal/fase-0-afwijkingen.md</c>. Van de twee mogelijke antwoorden —
    /// een plan publiceren dat niet klopt, of het plan afronden op wat een cron kán zeggen en op dat
    /// afgeronde plan draaien — is het tweede het eerlijke: dan is wat er op het scherm staat wat er
    /// werkelijk gebeurt.</para>
    ///
    /// <para>De standaard (zestig seconden, §4: "elke minuut") is exact. Een gevraagd interval van
    /// een uur of meer wordt op het hele uur gepland; daar tussenin is het <c>*/m</c>, met de
    /// gebruikelijke cron-betekenis: op de minuten 0, m, 2m, … van elk uur, en dus met een kortere
    /// sprong bij de uurwissel als m niet op 60 past. <see cref="PlannedInterval"/> levert het
    /// interval waarop werkelijk wordt gepland, zodat de melder kan zeggen dat hij van het gevraagde
    /// afwijkt.</para>
    /// </remarks>
    internal static SoratusSchedule Alerts(int intervalSeconds)
    {
        var minutes = PlannedMinutes(intervalSeconds);

        var expression = minutes switch
        {
            1 => "* * * * *",
            >= 60 => "0 * * * *",
            _ => string.Create(CultureInfo.InvariantCulture, $"*/{minutes} * * * *"),
        };

        return SoratusSchedule.Parse(expression, TimeZoneInfo.Utc);
    }

    /// <summary>
    /// Het interval waarop de melder werkelijk plant, gegeven het gevraagde interval.
    /// </summary>
    /// <param name="intervalSeconds">Het gevraagde interval in seconden.</param>
    /// <returns>Het geplande interval.</returns>
    /// <remarks>
    /// Bestaat zodat de melder bij het opstarten kan melden dát hij afrondt. Een afronding die
    /// niemand ziet is een afwijking tussen wat er is ingesteld en wat er gebeurt.
    /// </remarks>
    internal static TimeSpan PlannedInterval(int intervalSeconds) =>
        TimeSpan.FromMinutes(Math.Min(PlannedMinutes(intervalSeconds), 60));

    /// <summary>Het gevraagde interval in hele minuten, minstens één.</summary>
    /// <param name="intervalSeconds">Het gevraagde interval in seconden.</param>
    /// <returns>Het aantal minuten.</returns>
    private static int PlannedMinutes(int intervalSeconds) =>
        Math.Max(1, (int)Math.Round(Math.Clamp(intervalSeconds, 1, 86400) / 60.0, MidpointRounding.AwayFromZero));
}
