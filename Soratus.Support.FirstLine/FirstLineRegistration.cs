using Azure.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Soratus.Support.FirstLine;

/// <summary>
/// Wat er van het aansluiten van de eerstelijn terecht is gekomen.
/// </summary>
/// <remarks>
/// <para><strong>Een uitkomst en geen stilte,</strong> in dezelfde vorm als het resultaat van
/// <c>AddSoratusPlatformAgents</c> in <c>Program.cs</c>: een niveau en een regel tekst, die ná
/// <c>Build()</c> één keer wordt gelogd omdat er op het moment van registreren nog geen logger is.
/// Een eerstelijn die niet is aangesloten hoort dat te zeggen — anders is een supportscherm zonder
/// AI-antwoorden niet van een kapotte inrichting te onderscheiden.</para>
/// </remarks>
public sealed record FirstLineSetup
{
    /// <summary>De stand.</summary>
    public required FirstLineState State { get; init; }

    /// <summary>Op welk niveau <see cref="Explanation"/> hoort te worden gelogd.</summary>
    public required LogLevel Level { get; init; }

    /// <summary>Waarom de eerstelijn wel of niet draait, in één regel Nederlands.</summary>
    public required string Explanation { get; init; }

    /// <summary>
    /// Of <c>ISupportFirstLine</c> geregistreerd hoort te worden.
    /// </summary>
    /// <remarks>
    /// <para><strong>Dit is de schakelaar, en hij zit met opzet in de registratie en niet in het
    /// gedrag.</strong> Een eerstelijn die bestaat maar niets vraagt, zet "er kijkt een agent mee" op
    /// het scherm en escaleert daarna elke vraag; dat is een storing die zich voordoet als werkende
    /// functionaliteit, en §46.9 wijst hem daarom af. Staat deze eigenschap op <c>false</c>, dan is
    /// er geen eerstelijn, leest de klant dat een mens antwoordt, en is dat waar.</para>
    ///
    /// <para>Alleen <c>true</c> als <see cref="AddSoratusFirstLineExtensions.AddSoratusFirstLine"/>
    /// ook werkelijk een <see cref="IFirstLineChooser"/> heeft neergezet. Zo kan er geen
    /// <c>ISupportFirstLine</c> in de container staan die bij zijn eerste vraag omvalt op een
    /// ontbrekende afhankelijkheid.</para>
    /// </remarks>
    public bool IsReady => State == FirstLineState.Ready;
}

/// <summary>
/// Zet de eerstelijn in de container, of zegt waarom niet.
/// </summary>
public static class AddSoratusFirstLineExtensions
{
    /// <summary>
    /// Bindt de instellingen en registreert de kiezer als hij mag draaien.
    /// </summary>
    /// <param name="builder">De hostbouwer.</param>
    /// <returns>Wat er is gebeurd, om na <c>Build()</c> één keer te loggen.</returns>
    /// <remarks>
    /// <para><strong>Deze methode werpt niet.</strong> Een verkeerd ingestelde eerstelijn is een
    /// inrichtingsfout, en een inrichtingsfout die het opstarten tegenhoudt neemt <c>/healthz</c> mee
    /// en rolt daarmee de uitrol terug. Om dezelfde reden staat er geen <c>ValidateOnStart</c> op de
    /// instellingen — dezelfde afweging als bij <c>PortalData</c>, <c>PortalMail</c>,
    /// <c>PortalCosts</c> en <c>PortalAlerts</c>.</para>
    ///
    /// <para><strong>De instellingen worden twee keer gelezen en dat is geen dubbeling van het
    /// besluit.</strong> Eén keer hier, om te weten of er iets geregistreerd moet worden — dat moet
    /// vóór <c>Build()</c> gebeuren, en dan bestaat de container nog niet. En één keer als gebonden
    /// <c>IOptions</c>, voor de aanroep zelf. Het besluit staat één keer, in
    /// <see cref="FirstLineOptions.State"/>, en beide kanten stellen diezelfde vraag.</para>
    ///
    /// <para><strong>Er wordt hier geen <see cref="TokenCredential"/> geregistreerd.</strong> Het
    /// portaal heeft er al één — dezelfde managed identity voor Cosmos, de Communication Service en
    /// nu ook het taalmodel. Een eigen <c>DefaultAzureCredential</c> zou een tweede tokencache zijn
    /// en een tweede plek waar de identiteit wordt gekozen. Deze aanroep hoort dus ná die van de
    /// credential te staan; ontbreekt hij, dan valt de eerste vraag om en staat dat in de logregel.
    /// </para>
    /// </remarks>
    public static FirstLineSetup AddSoratusFirstLine(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var section = builder.Configuration.GetSection(FirstLineOptions.SectionName);

        builder.Services.AddOptions<FirstLineOptions>()
            .Bind(section)
            .ValidateDataAnnotations();

        var setup = Describe(Read(section), builder.Environment.IsDevelopment());

        if (setup.IsReady)
        {
            builder.Services.AddHttpClient(AzureOpenAiChooser.HttpClientName);
            builder.Services.AddScoped<IFirstLineChooser, AzureOpenAiChooser>();
        }

        return setup;
    }

    /// <summary>
    /// Leest de sectie, en levert lege instellingen op als hij niet te lezen is.
    /// </summary>
    /// <param name="section">De configuratiesectie.</param>
    /// <returns>De instellingen, of een lege verzameling.</returns>
    /// <remarks>
    /// <para><strong>De binder wérpt op een waarde die hij niet kan omzetten</strong> —
    /// <c>PortalFirstLine:TimeoutSeconds=een uur</c> levert een
    /// <see cref="InvalidOperationException"/> op — en die uitzondering zou hier vóór
    /// <c>Build()</c> vallen en dus het opstarten tegenhouden. Dat is precies wat er niet mag: een
    /// inrichtingsfout die het opstarten tegenhoudt neemt <c>/healthz</c> mee en rolt de uitrol
    /// terug. Een onleesbare instelling wordt daarom "niet ingericht", en dat staat in de regel die
    /// na <c>Build()</c> wordt gelogd.</para>
    ///
    /// <para>Hier wordt niet <em>gemeld</em> wat er onleesbaar was, want er is op dit moment nog geen
    /// logger. Wat er wél gebeurt: de gebonden <c>IOptions</c> heeft dezelfde fout, dus zodra iemand
    /// hem opvraagt komt de melding er alsnog — en dat gebeurt alleen als er iets is dat hem
    /// opvraagt, en dat is er niet zolang de stand "niet ingericht" is.</para>
    /// </remarks>
    private static FirstLineOptions Read(IConfiguration section)
    {
        try
        {
            return section.Get<FirstLineOptions>() ?? new FirstLineOptions();
        }
        catch (InvalidOperationException)
        {
            return new FirstLineOptions();
        }
    }

    /// <summary>
    /// De stand met de regel die erbij hoort.
    /// </summary>
    /// <param name="settings">De gelezen instellingen.</param>
    /// <param name="isDevelopment">Of dit een ontwikkelomgeving is.</param>
    /// <returns>De uitkomst.</returns>
    /// <remarks>
    /// <para><strong>Eén van de vier standen is een waarschuwing en de andere drie niet, en dat
    /// onderscheid is het punt.</strong> "Uitgezet" is de standaardstand en dus geen probleem; een
    /// waarschuwing bij elke start zou ruis zijn, en ruis is precies wat later een échte
    /// waarschuwing onzichtbaar maakt. Maar <em>aangezet zonder endpoint of deployment</em> is iemand
    /// die dacht dat hij het had aangezet, en dat hoort op te vallen.</para>
    ///
    /// <para>Apart van <see cref="AddSoratusFirstLine"/> zodat de vier standen te meten zijn zonder
    /// een host te bouwen.</para>
    /// </remarks>
    internal static FirstLineSetup Describe(FirstLineOptions settings, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var state = settings.State(isDevelopment);

        return new FirstLineSetup
        {
            State = state,
            Level = state == FirstLineState.NotConfigured && settings.Enabled
                ? LogLevel.Warning
                : LogLevel.Information,
            Explanation = state switch
            {
                FirstLineState.Ready =>
                    $"De AI-eerstelijn is aangesloten op deployment {settings.Deployment} en "
                    + "antwoordt op vragen van klanten door één portaalgegeven aan te wijzen.",
                FirstLineState.DevelopmentMachine =>
                    "De AI-eerstelijn draait niet op een ontwikkelmachine, wat er ook in de "
                    + "instellingen staat: een aanroep kost geld uit de capaciteit van productie en "
                    + "stuurt klantgegevens naar een externe dienst. Vragen van klanten gaan naar "
                    + "een mens.",
                FirstLineState.NotConfigured when settings.Enabled =>
                    "De AI-eerstelijn is aangezet maar niet ingericht: PortalFirstLine:Endpoint of "
                    + "PortalFirstLine:Deployment is leeg. Er is niets om aan te roepen, dus vragen "
                    + "van klanten gaan naar een mens.",
                FirstLineState.NotConfigured =>
                    "De AI-eerstelijn is niet ingericht (geen endpoint of deployment). Vragen van "
                    + "klanten gaan naar een mens.",
                _ =>
                    "De AI-eerstelijn staat uit (PortalFirstLine:Enabled). Dat is de standaardstand; "
                    + "vragen van klanten gaan naar een mens.",
            },
        };
    }
}
