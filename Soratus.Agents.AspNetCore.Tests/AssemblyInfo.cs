using Xunit;

// Elke test hier bouwt een echte webhost met achtergronddiensten, een kanaal en een afsluitende
// leegdraaislag. xUnit draait testklassen standaard parallel, en dan lopen er meerdere van die
// hosts tegelijk: dezelfde reden waarom Soratus.Agents.Telemetry.Tests dit ook doet. Een test die
// soms rood is, is erger dan een test die rood is — hij leert je rood negeren.
//
// In productie draait er één host per proces, dus dit is een eigenschap van de testopstelling en
// geen defect in de bibliotheek.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
