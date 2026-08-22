using Soratus.Agents.Contracts;

namespace Soratus.Agents.Telemetry.HostedAgents;

/// <summary>
/// Eén agent die door deze host wordt geherbergd. De aankondiging, niet de agent zelf.
/// </summary>
/// <remarks>
/// <para>Dit type bestaat voor het geval dat niet in <see cref="Scheduling.IScheduledAgent"/>
/// past: een agent die geen eigen proces en geen eigen lus heeft, maar een dienst is binnen een
/// grotere host — een endpoint in een webapplicatie, een handler op een wachtrij. Zulke agents
/// komen met meer dan één per proces, dus ze kunnen niet ieder een
/// <see cref="AgentIdentity"/>-singleton zijn.</para>
///
/// <para><strong>Een schema is optioneel, en dat onderscheidt de twee soorten geherbergde agents.</strong>
/// Een dienst op aanvraag — een endpoint, een wachtrij-abonnement — draait wanneer hij wordt
/// aangeroepen; daar is geen volgende run om te voorspellen, dus <see cref="Schedule"/> blijft
/// <c>null</c>, <see cref="AgentRegistration.Schedule"/> blijft leeg en
/// <see cref="AgentRegistration.NextRunAt"/> blijft <c>null</c>. Het scherm toont dan de trigger in
/// plaats van een verzonnen tijdstip.</para>
///
/// <para>Een agent op een <em>klok</em> in dezelfde host — een nachtelijke collector, een melder die
/// elke minuut kijkt — heeft die volgende run wél, en die hoort gepubliceerd te worden. Niet als
/// versiering: zonder gepubliceerd plan is "laatste run 26 uur geleden" niet te beoordelen, want er
/// staat nergens hoe vaak deze agent hoort te draaien. Het plan is de maat waaraan stilte wordt
/// afgelezen, en daarom is het een feit dat de agent over zichzelf publiceert.</para>
/// </remarks>
public sealed record HostedAgentDeclaration
{
    /// <summary>
    /// Technische naam, kleine letters met koppelstreepjes, bijvoorbeeld
    /// <c>declaraties-import</c>. Stabiel over uitrollen heen — hier sluit alles op aan.
    /// </summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// Waardoor deze agent aan het werk gaat. Verplicht, en <see cref="TriggerKind.Timer"/> alleen
    /// samen met <see cref="Schedule"/>.
    /// </summary>
    /// <remarks>
    /// Er is geen standaardwaarde, en dat is opzet. De aanroeper is de enige die weet waar de
    /// aanroep vandaan komt, en een gegokte trigger staat straks als feit op het scherm van de
    /// klant.
    /// </remarks>
    public required TriggerKind Trigger { get; init; }

    /// <summary>
    /// Het plan waarop deze agent draait, of <c>null</c> bij een dienst die op een aanroep draait.
    /// </summary>
    /// <remarks>
    /// <para><strong>Een <see cref="SoratusSchedule"/> en geen <c>string</c>, en dat is het hele
    /// punt.</strong> Bij een geherbergde klok-agent plant de host zelf — de bibliotheek neemt die
    /// klok niet over, want dan zou het werk stoppen zodra de telemetrie niet is ingericht. De
    /// belofte van <see cref="AgentRegistration.Schedule"/> ("de expressie waarmee daadwerkelijk
    /// wordt gepland, niet een losse beschrijving die uit de pas kan lopen") is dan alleen te houden
    /// als de host wacht op precies het object dat hij aankondigt. Met een <c>string</c> in dit veld
    /// zou een tweede berekening naast de aankondiging kunnen bestaan; met dit type is er één.</para>
    ///
    /// <para>De volgende run wordt <em>niet</em> uit dit veld afgeleid bij elke hartslag. Zie
    /// <see cref="ISoratusHostedAgent.ReportNextRun"/> voor waarom dat een wezenlijk verschil is.</para>
    /// </remarks>
    public SoratusSchedule? Schedule { get; init; }

    /// <summary>
    /// Typeaanduiding voor de typekolom, bijvoorbeeld <c>Document-intake</c>. Alleen
    /// presentatie; leeg laten levert een leesbare vorm van <see cref="AgentName"/> op.
    /// </summary>
    public string? DisplayType { get; init; }

    /// <summary>
    /// Toelichting op de trigger voor op het scherm, bijvoorbeeld
    /// <c>POST /api/declaraties</c>.
    /// </summary>
    /// <remarks>
    /// Dit veld is vrije tekst en komt op het scherm van de klant terecht — dezelfde categorie
    /// als <c>msg</c> en <c>errorMessage</c>, met dezelfde eis: geen bestandspaden, geen
    /// klasse- of methodenamen, geen resource groups. Een routepatroon mag; een
    /// controllernaam niet.
    /// </remarks>
    public string? TriggerDetail { get; init; }

    /// <summary>
    /// Controleert de eigenschappen die een geherbergde agent per definitie heeft.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Als de naam leeg is, als de trigger <see cref="TriggerKind.Timer"/> is zonder
    /// <see cref="Schedule"/>, of als er een <see cref="Schedule"/> staat bij een andere trigger.
    /// </exception>
    /// <remarks>
    /// <para><strong>Timer en schema komen samen of niet.</strong> De documentatie van
    /// <see cref="AgentRegistration.Schedule"/> belooft dat bij een timer-agent een cron-expressie
    /// staat. Een timer zonder schema levert in het portaal dus een agent op schema zonder schema en
    /// met <c>nextRunAt</c> leeg — een tegenspraak die de lezer moet oplossen in plaats van de
    /// bouwer. Een schema zonder timer is de spiegel daarvan: dan staat er een plan bij een dienst
    /// die op een aanroep draait, en is de "volgende run" een tijdstip waar niets op gebeurt.</para>
    ///
    /// <para>Beide gevallen werpen en worden niet stil gecorrigeerd. Dit is een inrichtingsfout van
    /// de bouwer en die hoort bij het aankondigen zichtbaar te worden, niet pas als iemand zich
    /// afvraagt waarom er in het portaal iets anders staat dan hij bedoelde.</para>
    /// </remarks>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AgentName))
        {
            throw new InvalidOperationException(
                "Een geherbergde agent moet een naam hebben; die naam is de sleutel waarop het " +
                "portaal, de runs en de logregels aansluiten.");
        }

        if (Trigger == TriggerKind.Timer && Schedule is null)
        {
            throw new InvalidOperationException(
                $"Geherbergde agent '{AgentName}' meldt trigger '{TriggerKind.Timer}' zonder " +
                $"{nameof(Schedule)}. Het portaal zou hem dan als agent op schema tonen met een lege " +
                "volgende run. Geef het plan mee, of kies de trigger die de aanroep beschrijft (http, " +
                "queue, webhook, blob of manual).");
        }

        if (Trigger != TriggerKind.Timer && Schedule is not null)
        {
            throw new InvalidOperationException(
                $"Geherbergde agent '{AgentName}' heeft een {nameof(Schedule)} ('{Schedule.Expression}') " +
                $"maar trigger '{Trigger}'. Een plan bij een dienst die op een aanroep draait levert een " +
                $"volgende run op waar niets gebeurt. Zet de trigger op '{TriggerKind.Timer}' of laat het " +
                "plan weg.");
        }
    }
}
