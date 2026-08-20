namespace Soratus.Portal.Views;

/// <summary>
/// De tijdzone waarin het portaal "vandaag" bedoelt.
/// </summary>
/// <remarks>
/// Het portaal wordt door Soratus en door Nederlandse klanten gelezen, dus "runs vandaag" gaat over
/// de Nederlandse dag en niet over de UTC-dag. Dat scheelt in de winter een uur en in de zomer twee,
/// en dat is precies het verschil tussen een KPI die klopt en een KPI die 's ochtends vroeg
/// onverklaarbaar laag staat.
///
/// De zone wordt één keer opgezocht. Lukt dat niet — een container zonder tijdzonegegevens — dan
/// valt het portaal terug op UTC in plaats van te crashen. Het getal is dan iets anders bedoeld,
/// maar het scherm vertelt met <c>TodayStartedAt</c> alsnog vanaf welk moment het telt.
/// </remarks>
internal static class PortalTimeZone
{
    /// <summary>De tijdzone waarin dagen worden afgebakend.</summary>
    public static TimeZoneInfo Display { get; } = Resolve();

    /// <summary>
    /// Middernacht van de dag waarin dit moment valt, in <see cref="Display"/>.
    /// </summary>
    /// <param name="now">Het huidige moment.</param>
    /// <returns>Het begin van vandaag.</returns>
    public static DateTimeOffset StartOfToday(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Display);
        var midnight = local.Date;

        return new DateTimeOffset(midnight, Display.GetUtcOffset(midnight));
    }

    private static TimeZoneInfo Resolve()
    {
        // De IANA-naam eerst, de Windows-naam als tweede. .NET vertaalt ze op de meeste systemen
        // over en weer, maar niet op alle, en we draaien op Linux én op een Windows-werkplek.
        string[] identifiers = ["Europe/Amsterdam", "W. Europe Standard Time"];

        foreach (var identifier in identifiers)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(identifier);
            }
            catch (TimeZoneNotFoundException)
            {
                // Volgende proberen.
            }
            catch (InvalidTimeZoneException)
            {
                // Volgende proberen.
            }
        }

        return TimeZoneInfo.Utc;
    }
}
